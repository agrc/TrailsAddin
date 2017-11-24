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

            await QueuedTask.Run(() =>
            {
                using (Table routesTable = RoutesStandaloneTable.GetTable())
                using (FeatureClass segmentsFeatureClass = SegmentsLayer.GetFeatureClass())
                using (RowBuffer routeBuf = routesTable.CreateRowBuffer())
                using (RowCursor selectionCursor = SGIDTrailsLayer.GetSelection().Search(null, false))
                using (Geodatabase geodatabase = SegmentsLayer.GetTable().GetDatastore() as Geodatabase)
                using (AttributedRelationshipClass relationshipClass = geodatabase.OpenDataset<AttributedRelationshipClass>("RouteToTrailSegments"))
                {
                    // TODO: QA/QC route name unique, ordered segments are connected

                    var operation = new EditOperation();
                    operation.Name = "Create new trails route";

                    operation.Callback(context =>
                    {
                        routeBuf["RouteName"] = routeName;
                        Row routeRow = routesTable.CreateRow(routeBuf);

                        int order = 1;
                        while (selectionCursor.MoveNext())
                        {
                            RowBuffer segRowBuf = SegmentsLayer.GetFeatureClass().CreateRowBuffer();

                            foreach (Field field in selectionCursor.Current.GetFields())
                            {
                                if (field.IsEditable)
                                {
                                    segRowBuf[field.Name] = selectionCursor.Current[field.Name];
                                }
                            }

                            segRowBuf["USNG_SEG"] = Guid.NewGuid().ToString().Substring(0, 13);

                            using (Row segRow = segmentsFeatureClass.CreateRow(segRowBuf))
                            {
                                context.Invalidate(segRow);
                                RowBuffer relationshipRowBuf = relationshipClass.CreateRowBuffer();
                                relationshipRowBuf["RoutePart"] = order;
                                relationshipClass.CreateRelationship(routeRow, segRow, relationshipRowBuf);
                            }

                            order++;
                        }

                        context.Invalidate(routeRow);
                        routeRow.Dispose();
                    }, routesTable, segmentsFeatureClass, relationshipClass);

                    operation.Execute();
                    if (operation.IsSucceeded)
                    {
                        Notification notification = new Notification();
                        notification.Title = FrameworkApplication.Title;
                        notification.Message = string.Format("Route: \"{0}\" added successfully!", routeName);
                        FrameworkApplication.AddNotification(notification);

                        SGIDTrailsLayer.ClearSelection();
                    } else
                    {
                        MessageBox.Show(operation.ErrorMessage);
                    }
                }
            });
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
