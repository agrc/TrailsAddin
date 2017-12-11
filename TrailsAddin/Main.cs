using System;
using System.Linq;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Mapping;
using System.Globalization;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Core.Geometry;
using System.Collections.Generic;
using static ArcGIS.Desktop.Editing.EditOperation;
using ArcGIS.Desktop.Mapping.Events;
using System.Diagnostics;

namespace TrailsAddin
{
    internal class Main : Module
    {
        private static Main _this = null;
        public FeatureLayer SegmentsLayer;
        public FeatureLayer TrailheadsLayer;
        public StandaloneTable RoutesStandaloneTable;
        public StandaloneTable RouteToTrailSegmentsTable;
        private FeatureLayer TempSegmentsLayer;
        public bool BuildOnSelect = false;
        private List<string> tempSegmentIDs = new List<string>();
        private int currentPart = 1;
        private FeatureLayer USNGLayer;

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

        Main()
        {
            // get layer references
            SGIDTrailheadsLayer = GetLayer("SGID10.RECREATION.Trailheads");
            SGIDTrailsLayer = GetLayer("SGID10.RECREATION.Trails");
            SegmentsLayer = GetLayer("TrailSegments");
            TrailheadsLayer = GetLayer("Trailheads");
            TempSegmentsLayer = GetLayer("Temp Segments");
            USNGLayer = GetLayer("SGID10.INDICES.NationalGrid");
            RoutesStandaloneTable = MapView.Active.Map.StandaloneTables.First(l => l.Name == "Routes") as StandaloneTable;

            MapSelectionChangedEvent.Subscribe((MapSelectionChangedEventArgs args) =>
            {
                if (BuildOnSelect && args.Selection.Keys.Contains(SGIDTrailsLayer as MapMember))
                {
                    AddSelectedToTemp();
                }
            });
        }

        private void AddSelectedToTemp()
        {
            QueuedTask.Run(() =>
            {
                using (FeatureClass tempSegsFC = TempSegmentsLayer.GetFeatureClass())
                using (RowCursor sgidSegs = SGIDTrailsLayer.GetSelection().Search(null, false))
                {
                    EditOperation operation = new EditOperation();
                    operation.Name = "add selected to temp segments";
                    bool newPartCreated = false;
                    operation.Callback(context =>
                    {
                        while (sgidSegs.MoveNext())
                        {
                            string id = GetUSNGID_Line(sgidSegs.Current);
                            RowBuffer tempRowBuf = CopyRowValues(sgidSegs.Current, tempSegsFC);
                            tempRowBuf["USNG_SEG"] = id;
                            if (tempSegmentIDs.Contains(id))
                            {
                                currentPart++;
                                tempSegmentIDs.Clear();
                                newPartCreated = true;
                            }
                            tempSegmentIDs.Add(id);
                            tempRowBuf["RoutePart"] = currentPart;
                            tempSegsFC.CreateRow(tempRowBuf);
                            tempRowBuf.Dispose();
                        }
                        context.Invalidate(tempSegsFC);
                    }, tempSegsFC);
                    if (newPartCreated)
                    {
                        // NOT WORKING ref: https://community.esri.com/message/733381-re-examples-for-setonundone-setonredone-setoncomitted?commentID=733381#comment-733381
                        operation.SetOnUndone(() =>
                        {
                            currentPart--;
                        });
                        operation.SetOnRedone(() =>
                        {
                            currentPart++;
                        });
                    }

                    bool success = operation.Execute();
                    if (success)
                    {
                        SGIDTrailsLayer.ClearSelection();
                    }
                }
            });
        }

        internal FeatureLayer GetLayer(string name)
        {
            return MapView.Active.Map.GetLayersAsFlattenedList().First(l => l.Name == name) as FeatureLayer;
        }

        public async void AddNewRoute(string name)
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
                using (FeatureClass tempSegsFC = TempSegmentsLayer.GetFeatureClass())
                {
                    if (!BuildOnSelect && SGIDTrailsLayer.SelectionCount == 0)
                    {
                        MessageBox.Show("At least one segment must be selected!");
                        return;
                    }

                    QueryFilter namesFilter = new QueryFilter()
                    {
                        WhereClause = $"Upper(RouteName) = '{routeName.ToUpper()}'"
                    };
                    using (RowCursor namesCursor = RoutesStandaloneTable.Search(namesFilter))
                    {
                        if (namesCursor.MoveNext())
                        {
                            MessageBox.Show($"There is already a route named: {routeName}!");
                            return;
                        }
                    }

                    var operation = new EditOperation();
                    operation.Name = "Create new trails route: " + routeName;

                    operation.Callback(context =>
                    {
                        routeBuf["RouteName"] = routeName;
                        routeBuf["RouteID"] = $"{{{Guid.NewGuid()}}}";
                        Row routeRow = routesTable.CreateRow(routeBuf);

                        if (BuildOnSelect)
                        {
                            // get segments from TempSegments layer
                            bool atLeastOne = false;
                            using (RowCursor tempSegsCursor = tempSegsFC.Search(null, false))
                            {
                                while (tempSegsCursor.MoveNext())
                                {
                                    atLeastOne = true;
                                    Row row = tempSegsCursor.Current;
                                    CopySegment(row, (short)row["RoutePart"], segmentsFeatureClass, segmentsRelationshipClass, context, routeRow);
                                }
                                Reset();
                                tempSegsFC.DeleteRows(new QueryFilter());
                                context.Invalidate(tempSegsFC);
                            }

                            if (!atLeastOne)
                            {
                                context.Abort("There must be at least one feature in TempSegments!");
                            }
                        }
                        else
                        {
                            // get segments from selected features
                            while (trailsSelectionCursor.MoveNext())
                            {
                                CopySegment(trailsSelectionCursor.Current, 1, segmentsFeatureClass, segmentsRelationshipClass, context, routeRow);
                            }
                        }

                        // trailhead
                        if (SGIDTrailheadsLayer.SelectionCount == 1)
                        {
                            trailheadsSelectionCursor.MoveNext();
                            RowBuffer trailheadRowBuf = CopyRowValues(trailheadsSelectionCursor.Current, trailheadsFeatureClass);
                            trailheadRowBuf["USNG_TH"] = GetUSNGID_Point(((Feature)trailheadsSelectionCursor.Current).GetShape() as MapPoint);
                            routeRow["THID_FK"] = trailheadRowBuf["USNG_TH"];
                            using (Row trailheadRow = trailheadsFeatureClass.CreateRow(trailheadRowBuf))
                            {
                                trailheadsRelationshipClass.CreateRelationship(routeRow, trailheadRow);
                            }
                        }

                        context.Invalidate(routeRow);
                        routeRow.Dispose();
                    }, routesTable, segmentsFeatureClass, segmentsRelationshipClass, tempSegsFC);

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

        private void CopySegment (Row row, Int16 partNum, FeatureClass segmentsFeatureClass, Table routeToSegsTable, IEditContext context, Row routeRow) {
            RowBuffer segRowBuf = CopyRowValues(row, segmentsFeatureClass);

            segRowBuf["USNG_SEG"] = GetUSNGID_Line(row);

            using (Row segRow = segmentsFeatureClass.CreateRow(segRowBuf))
            {
                context.Invalidate(segRow);
                RowBuffer relationshipRowBuf = segmentsRelationshipClass.CreateRowBuffer();
                relationshipRowBuf["RoutePart"] = partNum;
                segmentsRelationshipClass.CreateRelationship(routeRow, segRow, relationshipRowBuf);
            }
        }

        internal RowBuffer CopyRowValues(Row originRow, FeatureClass destinationFeatureClass)
        {
            RowBuffer segRowBuf = destinationFeatureClass.CreateRowBuffer();

            foreach (Field field in originRow.GetFields())
            {
                if (field.IsEditable && field.Name != "RoutePart")
                {
                    segRowBuf[field.Name] = originRow[field.Name];
                }
            }

            return segRowBuf;
        }

        private string GetUSNGID_Point(MapPoint point)
        {
            SpatialQueryFilter filter = new SpatialQueryFilter()
            {
                FilterGeometry = point,
                SpatialRelationship = SpatialRelationship.Intersects,
                SubFields = "GRID1MIL,GRID100K"
            };
            RowCursor cursor = USNGLayer.Search(filter);
            cursor.MoveNext();
            Row row = cursor.Current;
            string grid1mil = (string)row["GRID1MIL"];
            string grid100k = (string)row["GRID100K"];
            cursor.Dispose();

            // this code is from gregs roads code: https://gist.github.com/gregbunce/1733a741d8b4343a7a60fc42acf5086b
            double dblMeterX = (double)point.X;
            double dblMeterY = (double)point.Y;
            // add .5 to so when we conver to long and the value gets truncated, it will still regain our desired value (if you need more info on this, talk to Bert)
            dblMeterX = dblMeterX + .5;
            dblMeterY = dblMeterY + .5;
            long lngMeterX = (long)dblMeterX;
            long lngMeterY = (long)dblMeterY;

            // trim the x and y meter values to get the needed four characters from each value
            string strMeterX_NoDecimal = lngMeterX.ToString();
            string strMeterY_NoDecimal = lngMeterY.ToString();

            // remove the begining characters
            strMeterX_NoDecimal = strMeterX_NoDecimal.Remove(0, 1);
            strMeterY_NoDecimal = strMeterY_NoDecimal.Remove(0, 2);

            //remove the ending characters
            strMeterY_NoDecimal = strMeterY_NoDecimal.Remove(strMeterY_NoDecimal.Length - 1);
            strMeterX_NoDecimal = strMeterX_NoDecimal.Remove(strMeterX_NoDecimal.Length - 1);

            // piece all the unique_id fields together
            return grid1mil + grid100k + strMeterX_NoDecimal + strMeterY_NoDecimal;
        }

        private string GetUSNGID_Line(Row segmentRow)
        {
            Polyline line = ((Feature)segmentRow).GetShape() as Polyline;
            MapPoint midpoint = GeometryEngine.Instance.MovePointAlongLine(line, 0.5, true, 0, SegmentExtension.NoExtension);

            return GetUSNGID_Point(midpoint);
        }

        internal bool CanOnCancelButtonClick
        {
            get
            {
                return tempSegmentIDs.Count > 0;
            }
        }

        internal void Reset()
        {
            tempSegmentIDs.Clear();
            currentPart = 1;
        }

        internal void OnCancelButtonClick()
        {
            Reset();

            QueuedTask.Run(() =>
            {
                EditOperation operation = new EditOperation();
                operation.Name = "Cancel New Route";
                FeatureClass featureClass = TempSegmentsLayer.GetFeatureClass();

                operation.Callback(context =>
                {
                    featureClass.DeleteRows(new QueryFilter());
                    context.Invalidate(featureClass);
                }, featureClass);

                bool success = operation.Execute();

                if (!success)
                {
                    MessageBox.Show("Error cancelling new route!");
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
