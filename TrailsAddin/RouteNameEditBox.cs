using ArcGIS.Core.Data;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrailsAddin
{
    internal class RouteNameEditBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        protected override void OnTextChange(string text)
        {
            // force to title case
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            text = textInfo.ToTitleCase(text);
            TrailsAddin.TrailsModule.Current.NewRouteName = text;

            Text = text;
        }
        
        protected override async void OnEnter()
        {
            var map = MapView.Active.Map;
            var segmentsLayer = map.GetLayersAsFlattenedList().First(l => l.Name == "TrailSegments") as FeatureLayer;
            var sgidTrailsLayer = map.GetLayersAsFlattenedList().First(l => l.Name == "SGID10.RECREATION.Trails") as FeatureLayer;
            var routesStandaloneTable = map.StandaloneTables.First(l => l.Name == "Routes") as StandaloneTable;
            string routeName = TrailsAddin.TrailsModule.Current.NewRouteName;

            await QueuedTask.Run(() =>
            {
                using (Table routesTable = routesStandaloneTable.GetTable())
                using (FeatureClass segmentsFeatureClass = segmentsLayer.GetFeatureClass())
                using (RowBuffer routeBuf = routesTable.CreateRowBuffer())
                using (RowCursor selectionCursor = sgidTrailsLayer.GetSelection().Search(null, false))
                using (Geodatabase geodatabase = segmentsLayer.GetTable().GetDatastore() as Geodatabase)
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
                            RowBuffer segRowBuf = segmentsLayer.GetFeatureClass().CreateRowBuffer();

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

                        Text = "";
                        sgidTrailsLayer.ClearSelection();
                    } else
                    {
                        MessageBox.Show(operation.ErrorMessage);
                    }
                }
            });
        }
    }
}
