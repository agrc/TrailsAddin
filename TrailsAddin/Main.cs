using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using System.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using System.Globalization;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Dialogs;

namespace TrailsAddin
{
    internal class Main : Module
    {
        private static Main _this = null;
        public static FeatureLayer SGIDTrailheadsLayer;
        public static FeatureLayer SGIDTrailsLayer;
        public static FeatureLayer SegmentsLayer;
        public static FeatureLayer TrailheadsLayer;
        public static StandaloneTable RoutesStandaloneTable;

        /// <summary>
        /// Retrieve the singleton instance to this module here
        /// </summary>
        public static Main Current
        {
            get
            {
                return _this ?? (_this = (Main)FrameworkApplication.FindModule("TrailsAddin_Module"));
            }
        }

        static Main()
        {
            // get layer references
            SGIDTrailheadsLayer = GetLayer("SGID10.RECREATION.Trailheads");
            SGIDTrailsLayer = GetLayer("SGID10.RECREATION.Trails");
            SegmentsLayer = GetLayer("TrailSegments");
            TrailheadsLayer = GetLayer("Trailheads");
            RoutesStandaloneTable = MapView.Active.Map.StandaloneTables.First(l => l.Name == "Routes") as StandaloneTable;
        }

        private static FeatureLayer GetLayer(string name)
        {
            return MapView.Active.Map.GetLayersAsFlattenedList().First(l => l.Name == name) as FeatureLayer;
        }

        public static async void AddNewRoute(string name)
        {
            var map = MapView.Active.Map;
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            string routeName = textInfo.ToTitleCase(name);

            if (SGIDTrailheadsLayer.SelectionCount > 1)
            {
                MessageBox.Show("A route may have only one trail head!", "New Route Aborted");
                return;
            }

            await QueuedTask.Run(() =>
            {
                using (Table routesTable = RoutesStandaloneTable.GetTable())
                using (FeatureClass segmentsFeatureClass = SegmentsLayer.GetFeatureClass())
                using (FeatureClass trailheadsFeatureClass = TrailheadsLayer.GetFeatureClass())
                using (RowBuffer routeBuf = routesTable.CreateRowBuffer())
                using (RowCursor trailsSelectionCursor = SGIDTrailsLayer.GetSelection().Search(null, false))
                using (RowCursor trailheadsSelectionCursor = SGIDTrailheadsLayer.GetSelection().Search(null, false))
                using (Geodatabase geodatabase = SegmentsLayer.GetTable().GetDatastore() as Geodatabase)
                using (AttributedRelationshipClass segmentsRelationshipClass = geodatabase.OpenDataset<AttributedRelationshipClass>("RouteToTrailSegments"))
                using (RelationshipClass trailheadsRelationshipClass = geodatabase.OpenDataset<RelationshipClass>("TrailheadToRoute"))
                {
                    // TODO: QA/QC route name unique, ordered segments are connected

                    var operation = new EditOperation();
                    operation.Name = "Create new trails route";

                    operation.Callback(context =>
                    {
                        routeBuf["RouteName"] = routeName;
                        Row routeRow = routesTable.CreateRow(routeBuf);

                        if (SGIDTrailheadsLayer.SelectionCount == 1)
                        {
                            trailheadsSelectionCursor.MoveNext();
                            RowBuffer trailheadRowBuf = CopyRowValues(trailheadsSelectionCursor.Current, trailheadsFeatureClass);
                            trailheadRowBuf["USNG_TH"] = Guid.NewGuid().ToString().Substring(0, 13);
                            routeRow["THID_FK"] = trailheadRowBuf["USNG_TH"];
                            using (Row trailheadRow = trailheadsFeatureClass.CreateRow(trailheadRowBuf))
                            {
                                trailheadsRelationshipClass.CreateRelationship(routeRow, trailheadRow);
                            }
                        }

                        int order = 1;
                        while (trailsSelectionCursor.MoveNext())
                        {
                            RowBuffer segRowBuf = CopyRowValues(trailsSelectionCursor.Current, segmentsFeatureClass);

                            segRowBuf["USNG_SEG"] = Guid.NewGuid().ToString().Substring(0, 13);

                            using (Row segRow = segmentsFeatureClass.CreateRow(segRowBuf))
                            {
                                context.Invalidate(segRow);
                                RowBuffer relationshipRowBuf = segmentsRelationshipClass.CreateRowBuffer();
                                relationshipRowBuf["RoutePart"] = order;
                                segmentsRelationshipClass.CreateRelationship(routeRow, segRow, relationshipRowBuf);
                            }

                            order++;
                        }

                        context.Invalidate(routeRow);
                        routeRow.Dispose();
                    }, routesTable, segmentsFeatureClass, segmentsRelationshipClass);

                    operation.Execute();
                    if (operation.IsSucceeded)
                    {
                        Notification notification = new Notification();
                        notification.Title = FrameworkApplication.Title;
                        notification.Message = string.Format("Route: \"{0}\" added successfully!", routeName);
                        FrameworkApplication.AddNotification(notification);

                        SGIDTrailsLayer.ClearSelection();
                        SGIDTrailheadsLayer.ClearSelection();
                    } else
                    {
                        MessageBox.Show(operation.ErrorMessage);
                    }
                }
            });
        }

        private static RowBuffer CopyRowValues(Row originRow, FeatureClass destinationFeatureClass)
        {
            RowBuffer segRowBuf = destinationFeatureClass.CreateRowBuffer();

            foreach (Field field in originRow.GetFields())
            {
                if (field.IsEditable)
                {
                    segRowBuf[field.Name] = originRow[field.Name];
                }
            }

            return segRowBuf;
        }

        #region Overrides
        /// <summary>
        /// Called by Framework when ArcGIS Pro is closing
        /// </summary>
        /// <returns>False to prevent Pro from closing, otherwise True</returns>
        protected override bool CanUnload()
        {
            //TODO - add your business logic
            //return false to ~cancel~ Application close
            return true;
        }

        #endregion Overrides

    }
}
