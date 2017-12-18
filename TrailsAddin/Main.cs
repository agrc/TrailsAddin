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

namespace TrailsAddin
{
    internal class Main : Module
    {
        private static Main _this = null;
        public FeatureLayer SegmentsLayer;
        public FeatureLayer HeadsLayer;
        public StandaloneTable RoutesStandaloneTable;
        public StandaloneTable RouteToTrailSegmentsTable;
        private FeatureLayer TempSegmentsLayer;
        public bool BuildOnSelect = false;
        private List<string> tempSegmentIDs = new List<string>();
        private int currentPart = 1;
        private FeatureLayer USNGLayer;
        public event EventHandler<OnNumPartsChangedArgs> OnNumPartsChanged;

        // field names
        private string RouteName = "RouteName";
        private string THID_FK = "THID_FK";
        private string RouteID = "RouteID";
        private string OutAndBack = "OutAndBack";

        private string USNG_SEG = "USNG_SEG";
        private string RoutePart = "RoutePart";

        private string USNG_TH = "USNG_TH";

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
            try
            {
                // get layer references
                SegmentsLayer = GetLayer("TrailSegments");
                HeadsLayer = GetLayer("Trailheads");
                TempSegmentsLayer = GetLayer("Temporary Segments");
                USNGLayer = GetLayer("SGID10.INDICES.NationalGrid");
                RoutesStandaloneTable = GetStandAloneTable("Routes");
                RouteToTrailSegmentsTable = GetStandAloneTable("RouteToTrailSegments");
            } catch
            {
                MessageBox.Show("Missing layer!");
            }

            MapSelectionChangedEvent.Subscribe((MapSelectionChangedEventArgs args) =>
            {
                if (BuildOnSelect && args.Selection.Keys.Contains(SegmentsLayer as MapMember))
                {
                    AddSelectedToTemp();
                }
            });
        }

        private async void AddSelectedToTemp()
        {
            await QueuedTask.Run(() =>
            {
                using (RowCursor segmentsCursor = SegmentsLayer.GetSelection().Search(null))
                {
                    EditOperation operation = new EditOperation();
                    operation.Name = "add selected to temp segments";
                    while (segmentsCursor.MoveNext())
                    {
                        var id = EnsureIDForSegment(segmentsCursor.Current, operation);
                        CopyRowValues(segmentsCursor.Current, currentPart, operation);

                        if (tempSegmentIDs.Contains(id))
                        {
                            MessageBox.Show($"This segment ({id}) has already been selected for the current part. Try creating a new part.");
                        }
                        tempSegmentIDs.Add(id);
                    }

                    bool success = operation.Execute();
                    if (success)
                    {
                        SegmentsLayer.ClearSelection();
                        TempSegmentsLayer.ClearSelection();
                    } else
                    {
                        MessageBox.Show(operation.ErrorMessage);
                    }
                }
            });
        }

        internal FeatureLayer GetLayer(string name)
        {
            // TODO: add try statement so that error message can show missing layer name
            return MapView.Active.Map.GetLayersAsFlattenedList().First(l => l.Name == name) as FeatureLayer;
        }

        internal StandaloneTable GetStandAloneTable(string name)
        {
            return MapView.Active.Map.StandaloneTables.First(l => l.Name == name) as StandaloneTable;
        }

        public async void AddNewRoute(string name)
        {
            var map = MapView.Active.Map;
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            string routeName = textInfo.ToTitleCase(name);

            if (HeadsLayer.SelectionCount > 1)
            {
                MessageBox.Show("A route may have only one trail head!", "New Route Aborted");
                return;
            }

            if (!BuildOnSelect && SegmentsLayer.SelectionCount == 0)
            {
                MessageBox.Show("At least one segment must be selected!");
                return;
            }

            await QueuedTask.Run(() =>
            {
                using (Table routesTable = RoutesStandaloneTable.GetTable())
                using (Table routeToSegmentsTable = RouteToTrailSegmentsTable.GetTable())
                using (RowBuffer routeBuf = routesTable.CreateRowBuffer())
                using (FeatureClass tempSegsFeatureClass = TempSegmentsLayer.GetFeatureClass())
                {
                    QueryFilter namesFilter = new QueryFilter()
                    {
                        WhereClause = $"Upper({RouteName}) = '{routeName.ToUpper()}'"
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

                    EnsureIDsForSelected(operation);
                    operation.Callback(context =>
                    {
                        // create route row
                        routeBuf[RouteName] = routeName;
                        routeBuf[RouteID] = $"{{{Guid.NewGuid()}}}";
                        routeBuf[OutAndBack] = "Yes";
                        using (Row routeRow = routesTable.CreateRow(routeBuf))
                        using (RowCursor headsCursor = HeadsLayer.GetSelection().Search(null, false))
                        using (RowCursor segmentCursor = SegmentsLayer.GetSelection().Search((QueryFilter)null, false))
                        {
                            if (BuildOnSelect)
                            {
                                // get segments from TempSegments layer
                                bool atLeastOne = false;
                                using (RowCursor tempSegsCursor = tempSegsFeatureClass.Search(null, false))
                                {
                                    while (tempSegsCursor.MoveNext())
                                    {
                                        atLeastOne = true;
                                        Row row = tempSegsCursor.Current;

                                        CreateRoutePart((string)row[USNG_SEG], (string)routeRow[RouteID], int.Parse(row[RoutePart].ToString()), context, routeToSegmentsTable);
                                    }
                                    Reset();
                                    tempSegsFeatureClass.DeleteRows(new QueryFilter());
                                    context.Invalidate(tempSegsFeatureClass);
                                }

                                if (!atLeastOne)
                                {
                                    context.Abort("There must be at least one feature in TempSegments!");
                                }
                            }
                            else
                            {
                                //get segments from selected features
                                while (segmentCursor.MoveNext())
                                {
                                    var segRow = segmentCursor.Current;

                                    CreateRoutePart((string)segRow[USNG_SEG], (string)routeRow[RouteID], 1, context, routeToSegmentsTable);
                                }
                            }

                            // trailhead
                            if (HeadsLayer.SelectionCount == 1)
                            {
                                headsCursor.MoveNext();
                                routeRow[THID_FK] = headsCursor.Current[USNG_TH];
                            }

                            context.Invalidate(routeRow);
                        }
                    }, routesTable, routeToSegmentsTable, tempSegsFeatureClass);

                    operation.Execute();
                    if (operation.IsSucceeded)
                    {
                        Notification notification = new Notification();
                        notification.Title = FrameworkApplication.Title;
                        notification.Message = string.Format("Route: \"{0}\" added successfully!", routeName);
                        FrameworkApplication.AddNotification(notification);

                        SegmentsLayer.ClearSelection();
                        HeadsLayer.ClearSelection();
                    }
                    else
                    {
                        MessageBox.Show(operation.ErrorMessage);
                    }
                }
            });
        }

        private void CreateRoutePart(string segID, string routeID, int part, IEditContext context, Table routeToSegmentsTable)
        {
            using (RowBuffer routeToSegBuf = routeToSegmentsTable.CreateRowBuffer())
            {
                routeToSegBuf[RouteID] = routeID;
                routeToSegBuf[USNG_SEG] = segID;
                routeToSegBuf[RoutePart] = part;

                var row = routeToSegmentsTable.CreateRow(routeToSegBuf);
                context.Invalidate(row);
            }
        }

        internal void EnsureIDsForSelected(EditOperation operation)
        {
            using (RowCursor segmentsCursor = SegmentsLayer.GetSelection().Search()) {
                while (segmentsCursor.MoveNext())
                {
                    var row = segmentsCursor.Current;
                    EnsureIDForSegment(row, operation);
                }
            }

            using (RowCursor headsCursor = HeadsLayer.GetSelection().Search())
            {
                while (headsCursor.MoveNext())
                {
                    var row = headsCursor.Current;
                    if (row[USNG_TH] == null || (string)row[USNG_TH] == "")
                    {
                        operation.Modify(HeadsLayer, row.GetObjectID(), new Dictionary<string, object> { [USNG_TH] = GetUSNGID_Point((MapPoint)row["Shape"]) });
                    }
                }
            }
        }

        internal void AddPart()
        {
            currentPart++;
            tempSegmentIDs.Clear();
            OnNumPartsChanged(this, new OnNumPartsChangedArgs(currentPart));
        }

        private string EnsureIDForSegment(Row row, EditOperation operation)
        {
            if (row[USNG_SEG] == null || (string)row[USNG_SEG] == "")
            {
                var id = GetUSNGID_Line(row);
                operation.Modify(SegmentsLayer, row.GetObjectID(), new Dictionary<string, object> { [USNG_SEG] = id });
                return id;
            } else
            {
                return (string)row[USNG_SEG];
            }
        }

        internal void CopyRowValues(Row originRow, int partNumber, EditOperation operation)
        {
            var attributes = new Dictionary<string, object>();

            attributes["SHAPE"] = originRow["SHAPE"];
            attributes[USNG_SEG] = GetUSNGID_Line(originRow);
            attributes[RoutePart] = partNumber;

            operation.Create(TempSegmentsLayer, attributes);
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
            OnNumPartsChanged(this, new OnNumPartsChangedArgs(currentPart));
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

    class OnNumPartsChangedArgs : EventArgs
    {
        public int numParts { get; set; }
        public OnNumPartsChangedArgs(int newNumber)
        {
            numParts = newNumber;
        }
    }
}
