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
using ArcGIS.Core.CIM;
using System.Threading.Tasks;

namespace TrailsAddin
{
    internal class Main : Module
    {
        private static Main _this = null;
        public FeatureLayer SegmentsLayer;
        public FeatureLayer HeadsLayer;
        public StandaloneTable RoutesStandaloneTable;
        public StandaloneTable RouteToTrailSegmentsTable;
        public StandaloneTable RouteToTrailheadsTable;
        private FeatureLayer TempSegmentsLayer;
        public bool BuildOnSelect = false;
        private List<string> tempSegmentIDs = new List<string>();
        private int currentPart = 1;
        private FeatureLayer USNGLayer;
        public event EventHandler<OnNumPartsChangedArgs> OnNumPartsChanged;
        public event EventHandler<OnRouteNameChangedArgs> OnRouteNameChanged;
        private string SelectedRoute;
        private List<IDisposable> overlays = new List<IDisposable>();

        // field names
        internal string RouteName = "RouteName";
        internal string RouteID = "RouteID";

        internal string USNG_SEG = "USNG_SEG";
        internal string RoutePart = "RoutePart";

        internal string USNG_TH = "USNG_TH";

        // dataset names
        internal string RouteToTrailheads = "RouteToTrailheads";
        internal string RouteToTrailSegments = "RouteToTrailSegments";
        internal string TrailSegments = "TrailSegments";
        internal string Trailheads = "Trailheads";
        internal string Routes = "Routes";

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
            SegmentsLayer = GetLayer(TrailSegments);
            HeadsLayer = GetLayer(Trailheads);
            TempSegmentsLayer = GetLayer("Temporary Segments");
            USNGLayer = GetLayer("SGID10.INDICES.NationalGrid");
            RoutesStandaloneTable = GetStandAloneTable(Routes);
            RouteToTrailSegmentsTable = GetStandAloneTable(RouteToTrailSegments);
            RouteToTrailheadsTable = GetStandAloneTable(RouteToTrailheads);

            MapSelectionChangedEvent.Subscribe((MapSelectionChangedEventArgs args) =>
            {
                if (BuildOnSelect && args.Selection.Keys.Contains(SegmentsLayer as MapMember))
                {
                    AddSelectedToTemp();
                }

                if (args.Selection.Keys.Contains(RoutesStandaloneTable) && !BuildOnSelect)
                {
                    if (RoutesStandaloneTable.SelectionCount == 0)
                    {
                        SelectedRoute = null;
                        return;
                    }

                    if (RoutesStandaloneTable.SelectionCount > 1)
                    {
                        SelectedRoute = null;
                        return;
                    }

                    ShowRoute();
                } else
                {
                    SelectedRoute = null;
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
                    List<string> newIDs = new List<string>();
                    while (segmentsCursor.MoveNext())
                    {
                        var id = EnsureIDForSegment(segmentsCursor.Current, operation);

                        if (tempSegmentIDs.Contains(id))
                        {
                            MessageBox.Show($"This segment ({id}) has already been selected for the current part. Try creating a new part.");
                            continue;
                        }

                        CopyRowValues(segmentsCursor.Current, currentPart, operation);

                        newIDs.Add(id);
                    }

                    tempSegmentIDs.AddRange(newIDs);

                    operation.SetOnUndone(() =>
                    {
                        tempSegmentIDs.RemoveRange(tempSegmentIDs.Count - newIDs.Count, newIDs.Count);
                    });

                    bool success = operation.Execute();
                    if (!success)
                    {
                        MessageBox.Show(operation.ErrorMessage);
                    }

                    SegmentsLayer.ClearSelection();
                    TempSegmentsLayer.ClearSelection();
                }
            });
        }

        internal FeatureLayer GetLayer(string name)
        {
            try
            {
                return MapView.Active.Map.GetLayersAsFlattenedList().First(l => l.Name == name) as FeatureLayer;
            } catch
            {
                var msg = $"Missing layer: {name}!";
                MessageBox.Show(msg);
                throw new Exception(msg);
            }
        }

        internal StandaloneTable GetStandAloneTable(string name)
        {
            return MapView.Active.Map.StandaloneTables.First(l => l.Name == name) as StandaloneTable;
        }

        public async void AddNewRoute(string routeName)
        {
            var map = MapView.Active.Map;

            if (!BuildOnSelect && SegmentsLayer.SelectionCount == 0)
            {
                MessageBox.Show("At least one segment must be selected!");
                return;
            }

            await QueuedTask.Run(async () =>
            {
                using (var routesTable = RoutesStandaloneTable.GetTable())
                using (var routeToHeadsTable = RouteToTrailheadsTable.GetTable())
                using (var routeToSegmentsTable = RouteToTrailSegmentsTable.GetTable())
                using (var routeBuf = routesTable.CreateRowBuffer())
                using (var tempSegsFeatureClass = TempSegmentsLayer.GetFeatureClass())
                {
                    var namesFilter = new QueryFilter()
                    {
                        WhereClause = $"Upper({RouteName}) = '{routeName.ToUpper().Replace("'", "''")}'"
                    };
                    using (var namesCursor = RoutesStandaloneTable.Search(namesFilter))
                    {
                        if (namesCursor.MoveNext())
                        {
                            MessageBox.Show($"There is already a route named: {routeName}!");
                            return;
                        }
                    }

                    var operation = new EditOperation();
                    operation.Name = "Create new trails route: " + routeName;

                    await EnsureIDsForSelectedAsync(operation);
                    await operation.ExecuteAsync();

                    if (!operation.IsSucceeded)
                    {
                        MessageBox.Show(operation.ErrorMessage);
                        return;
                    }

                    var operation2 = operation.CreateChainedOperation();

                    operation2.Callback(context =>
                    {
                        // create route row
                        routeBuf[RouteName] = routeName;
                        routeBuf[RouteID] = $"{{{Guid.NewGuid()}}}";
                        using (var routeRow = routesTable.CreateRow(routeBuf))
                        using (var headsCursor = HeadsLayer.GetSelection().Search(null, false))
                        using (var segmentCursor = SegmentsLayer.GetSelection().Search((QueryFilter)null, false))
                        {
                            var segments = new List<string>();
                            var parts = new Dictionary<int, List<Polyline>>();
                            if (BuildOnSelect)
                            {
                                // get segments from TempSegments layer
                                bool atLeastOne = false;
                                using (var tempSegsCursor = tempSegsFeatureClass.Search(null, false))
                                {
                                    while (tempSegsCursor.MoveNext())
                                    {
                                        atLeastOne = true;
                                        var row = tempSegsCursor.Current;

                                        var partNum = int.Parse(row[RoutePart].ToString());
                                        var segID = (string)row[USNG_SEG];
                                        CreateRoutePart(segID, (string)routeRow[RouteID], partNum, context, routeToSegmentsTable);

                                        segments.Add(segID);

                                        var geometry = (Polyline)row["SHAPE"];
                                        if (parts.ContainsKey(partNum))
                                        {
                                            parts[partNum].Add(geometry);
                                        } else
                                        {
                                            parts[partNum] = new List<Polyline>() { geometry };
                                        }
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

                                    var segID = (string)segRow[USNG_SEG];
                                    const int partNum = 1;
                                    CreateRoutePart(segID, routeRow[RouteID], partNum, context, routeToSegmentsTable);

                                    segments.Add(segID);

                                    var geometry = (Polyline)segRow["SHAPE"];
                                    if (parts.ContainsKey(partNum))
                                    {
                                        parts[partNum].Add(geometry);
                                    } else
                                    {
                                        parts[partNum] = new List<Polyline>() { geometry };
                                    }
                                }
                            }

                            if (segments.Count > 1 && !ValidateConnectivity(parts))
                            {
                                context.Abort("Not all segments are connected!");
                                return;
                            }

                            // trailhead
                            if (HeadsLayer.SelectionCount > 0)
                            {
                                while (headsCursor.MoveNext())
                                {
                                    using (var headBuffer = routeToHeadsTable.CreateRowBuffer())
                                    {
                                        headBuffer[RouteID] = (string)routeRow[RouteID];
                                        headBuffer[USNG_TH] = headsCursor.Current[USNG_TH];

                                        using (var headRow = routeToHeadsTable.CreateRow(headBuffer))
                                        { 
                                            context.Invalidate(headRow);
                                        }
                                    }
                                }
                            }

                            context.Invalidate(routeRow);
                        }
                    }, routesTable, routeToSegmentsTable, routeToHeadsTable, tempSegsFeatureClass);

                    await operation2.ExecuteAsync();
                    if (operation2.IsSucceeded)
                    {
                        FrameworkApplication.AddNotification(new Notification
                        {
                            Title = FrameworkApplication.Title,
                            Message = $"Route: \"{routeName}\" added successfully!",
                            ImageUrl = "pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericCheckMark32.png"
                        });

                        SegmentsLayer.ClearSelection();
                        HeadsLayer.ClearSelection();
                    }
                    else
                    {
                        MessageBox.Show(operation2.ErrorMessage);
                    }
                }
            });
        }

        private bool ValidateConnectivity(Dictionary<int, List<Polyline>> parts)
        {
            var geoEngine = GeometryEngine.Instance;
            var builder = new PolylineBuilder(SpatialReferenceBuilder.CreateSpatialReference(26912));
            var partLines = new List<Polyline>();
            foreach (int partNum in parts.Keys)
            {
                foreach (Polyline polyline in parts[partNum])
                {
                    builder.AddPart(polyline.Points);
                }

                var partLine = geoEngine.SimplifyPolyline(builder.ToGeometry(), SimplifyType.Network);

                if (partLine.PartCount > 1)
                {
                    ShowInvalidOverlays(partLine);

                    return false;
                }

                partLines.Add(partLine);
                builder.SetEmpty();
            }

            foreach (Polyline partLine in partLines)
            {
                // check to make sure that the last point of the main polyline matches with the first point of the new part
                var mainLine = builder.ToGeometry();
                if (mainLine.PointCount > 1 && mainLine.Points[mainLine.PointCount - 1].IsEqual(partLine.Points[0]))
                {
                    builder.AddPart(partLine.Points);
                } else
                {
                    builder.AddPart(partLine.Points.Reverse());
                }
            }
            var routeLine = geoEngine.SimplifyPolyline(builder.ToGeometry(), SimplifyType.Network, true);

            if (routeLine.PartCount > 1)
            {
                ShowInvalidOverlays(routeLine);

                return false;
            }

            return true;
        }

        private void ShowInvalidOverlays(Polyline multipartLine)
        {
            var colorFactory = ColorFactory.Instance;
            var colors = new CIMColor[4] { colorFactory.BlueRGB, colorFactory.RedRGB, colorFactory.GreenRGB, colorFactory.WhiteRGB };

            for (var i = 0; i < multipartLine.Parts.Count; i++)
            {
                var part = multipartLine.Parts[i];
                var partBuilder = new PolylineBuilder(SpatialReferenceBuilder.CreateSpatialReference(26912));
                partBuilder.AddPart(part);
                var colorNum = 50 * i;
                var symbol = SymbolFactory.Instance.ConstructLineSymbol(colors[i], 8).MakeSymbolReference();
                overlays.Add(MapView.Active.AddOverlay(partBuilder.ToGeometry(), symbol));
            }
        }

        private void CreateRoutePart(string segID, object routeID, int part, IEditContext context, Table routeToSegmentsTable)
        {
            using (var routeToSegBuf = routeToSegmentsTable.CreateRowBuffer())
            {
                routeToSegBuf[RouteID] = routeID;
                routeToSegBuf[USNG_SEG] = segID;
                routeToSegBuf[RoutePart] = part;

                using (var row = routeToSegmentsTable.CreateRow(routeToSegBuf))
                {
                    context.Invalidate(row);
                }
            }
        }

        internal Task EnsureIDsForSelectedAsync(EditOperation operation)
        {
            return QueuedTask.Run(() =>
            {
                using (var segmentsCursor = SegmentsLayer.GetSelection().Search(null, false))
                {
                    while (segmentsCursor.MoveNext())
                    {
                        var row = segmentsCursor.Current;
                        EnsureIDForSegment(row, operation);
                    }
                }

                using (RowCursor headsCursor = HeadsLayer.GetSelection().Search(null, false))
                {
                    while (headsCursor.MoveNext())
                    {
                        var row = headsCursor.Current;
                        if (row[USNG_TH] == null || (string)row[USNG_TH] == "" || (string)row[USNG_TH] == "<Null>")
                        {
                            operation.Modify(HeadsLayer, row.GetObjectID(), new Dictionary<string, object> { [USNG_TH] = GetUSNGID_Point((MapPoint)row["Shape"]) });
                        }
                    }
                }

            });
        }

        internal void AddPart()
        {
            currentPart++;
            tempSegmentIDs.Clear();
            OnNumPartsChanged(this, new OnNumPartsChangedArgs(currentPart));
        }

        private string EnsureIDForSegment(Row row, EditOperation operation)
        {
            if (row[USNG_SEG] == null || (string)row[USNG_SEG] == "" || (string)row[USNG_SEG] == "<Null>")
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
            
            foreach (var overlay in overlays)
            {
                overlay.Dispose();
            }

            overlays.Clear();
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

        internal void OnDeleteRouteButtonClick()
        {
            QueuedTask.Run(() =>
            {
                using (var cursor = RoutesStandaloneTable.GetSelection().Search(null))
                {
                    var operation = new EditOperation();

                    cursor.MoveNext();
                    var routeRow = cursor.Current;
                    string routeID = (string)routeRow[RouteID];
                    string routeName = (string)routeRow[RouteName];

                    operation.Name = $"Delete route: {routeName}";
                    operation.Delete(RoutesStandaloneTable, routeRow.GetObjectID());

                    var query = new QueryFilter() { WhereClause = $"{RouteID} = '{routeID}'" };
                    using (var segsCursor = RouteToTrailSegmentsTable.Search(query))
                    using (var headsCursor = RouteToTrailheadsTable.Search(query))
                    {
                        while (segsCursor.MoveNext())
                        {
                            operation.Delete(RouteToTrailSegmentsTable, segsCursor.Current.GetObjectID());
                        }
                        while (headsCursor.MoveNext())
                        {
                            operation.Delete(RouteToTrailheadsTable, headsCursor.Current.GetObjectID());
                        }
                    }

                    operation.Execute();
                    
                    if (operation.IsSucceeded)
                    {
                        Notification notification = new Notification();
                        notification.Title = FrameworkApplication.Title;
                        notification.Message = $"Route: \"{routeName}\" deleted successfully!";
                        FrameworkApplication.AddNotification(notification);

                        RoutesStandaloneTable.ClearSelection();
                    } else
                    {
                        MessageBox.Show(operation.ErrorMessage);
                    }
                }
            });
        }

        internal bool CanOnDeleteRouteButtonClick
        {
            get
            {
                return RoutesStandaloneTable.SelectionCount == 1;
            }
        }

        private void ShowRoute()
        {
            Reset();

            QueuedTask.Run(() =>
            {
                using (var routesCursor = RoutesStandaloneTable.GetSelection().Search())
                {
                    routesCursor.MoveNext();
                    var routeID = (string)routesCursor.Current[RouteID];

                    if (SelectedRoute == routeID)
                    {
                        return;
                    }
                    SelectedRoute = routeID;

                    SegmentsLayer.ClearSelection();
                    HeadsLayer.ClearSelection();
                    RouteToTrailSegmentsTable.ClearSelection();

                    var routeIDQuery = $"{RouteID} = '{routeID}'";
                    RouteToTrailSegmentsTable.Select(new QueryFilter() { WhereClause = routeIDQuery });
                    RouteToTrailheadsTable.Select(new QueryFilter() { WhereClause = routeIDQuery });

                    SegmentsLayer.Select(new QueryFilter() { WhereClause = $"{USNG_SEG} IN (SELECT {USNG_SEG} FROM UtahTrails.TRAILSADMIN.{RouteToTrailSegments}_evw WHERE {routeIDQuery})" });

                    HeadsLayer.Select(new QueryFilter() { WhereClause = $"{USNG_TH} IN (SELECT {USNG_TH} FROM UtahTrails.TRAILSADMIN.{RouteToTrailheads}_evw WHERE {routeIDQuery})" });

                    MapView.Active.ZoomToAsync(new [] { SegmentsLayer, HeadsLayer }, true);
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

        internal void ChangeRouteName(string text)
        {
            OnRouteNameChanged(this, new OnRouteNameChangedArgs(text));
        }
    }

    class OnNumPartsChangedArgs : EventArgs
    {
        public int numParts { get; set; }
        public OnNumPartsChangedArgs(int newNumber)
        {
            numParts = newNumber;
        }
    }

    class OnRouteNameChangedArgs : EventArgs
    {
        public string name { get; set; }
        public OnRouteNameChangedArgs(string newName)
        {
            name = newName;
        }
    }
}
