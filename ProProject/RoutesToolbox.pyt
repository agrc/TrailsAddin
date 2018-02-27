'''
RoutesToolbox.py

A tool that builds trail routes polylines from the Routes and Segments data.
'''
from os.path import dirname, join

import arcpy

routeLinesFC = 'UtahTrails.TRAILSADMIN.RouteLines'
routeToSegmentsTable = 'UtahTrails.TRAILSADMIN.RouteToTrailSegments'
segmentsFC = 'UtahTrails.TRAILSADMIN.TrailSegments'

#: fields
fldRouteName = 'RouteName'
fldTHID_FK = 'THID_FK'
fldRouteID = 'RouteID'
fldRouteType = 'RouteType'
fldUrl = 'Url'
fldUSNG_SEG = 'USNG_SEG'
fldRoutePart = 'RoutePart'
fldLENGTH = 'LENGTH'
fldUSNG_TH = 'USNG_TH'

shapeToken = 'SHAPE@'
utm = arcpy.SpatialReference(26912)
outAndBack = 'Out & Back'


def reverse(input_array):
    reversedArray = arcpy.Array()
    for index in range(input_array.count - 1, -1, -1):
        reversedArray.add(input_array[index])

    return reversedArray


class Toolbox(object):
    def __init__(self):
        """Define the toolbox (the name of the toolbox is the name of the
        .pyt file)."""
        self.label = "Toolbox"
        self.alias = ""

        # List of tool classes associated with this toolbox
        self.tools = [BuildRouteLines, FindRouteLineIssues]


class FindRouteLineIssues(object):
    def __init__(self):
        self.label = 'FindRouteLineIssues'
        self.description = 'Find RouteLines features that are not overlapped by TrailSegments features.'
        self.canRunInBackground = True

    def getParameterInfo(self):
        return []

    def execute(self, parameters, messages):
        routeLinesLayer = 'RouteLines'
        segmentsLayer = 'TrailSegments'
        trailheadsLayer = 'Trailheads'
        routeToTrailheadsTable = 'RouteToTrailheads'
        nonoverlapping = join(arcpy.env.scratchGDB, 'nonoverlapping')

        messages.addMessage('clearing selection on layers')
        arcpy.management.SelectLayerByAttribute(routeLinesLayer, 'CLEAR_SELECTION')
        arcpy.management.SelectLayerByAttribute(segmentsLayer, 'CLEAR_SELECTION')
        arcpy.management.SelectLayerByAttribute(trailheadsLayer, 'CLEAR_SELECTION')
        arcpy.management.SelectLayerByAttribute(routeToTrailheadsTable, 'CLEAR_SELECTION')

        ids = set()
        warning_messages = []

        #: Find RouteLines that are not overlapped by TrailSegments
        if arcpy.Exists(nonoverlapping):
            messages.addMessage('removing old output')
            arcpy.management.Delete(nonoverlapping)

        messages.addMessage('running symmetrical difference tool')
        arcpy.analysis.SymDiff(routeLinesLayer, segmentsLayer, nonoverlapping)

        messages.addMessage('gathering routeIDs from output')
        with arcpy.da.SearchCursor(nonoverlapping, [fldRouteID, fldRouteName], 'RouteID IS NOT NULL') as cursor:
            for routeID, routeName in cursor:
                warning_messages.append(routeName + ': route not overlapped by segments')
                ids.add(routeID)

        #: Find RouteLines that do not cover all selected segments for a particular route
        with arcpy.da.SearchCursor(routeLinesLayer, [fldRouteID]) as cursor:
            all_ids = [routeID for routeID, in cursor]

        arcpy.SetProgressor('step', 'looking for route lines that do not cover all route segments',
                            min_range=0,
                            max_range=len(all_ids),
                            step_value=1)

        for routeID in all_ids:
            lines_query = '{} = \'{}\''.format(fldRouteID, routeID)
            segments_query = '{0} IN (SELECT {0} FROM {1} WHERE {2})'.format(fldUSNG_SEG, routeToSegmentsTable, lines_query)
            segments_union = None
            with arcpy.da.SearchCursor(routeLinesLayer, [fldRouteID, shapeToken, fldRouteName], lines_query) as line_cursor, \
                    arcpy.da.SearchCursor(segmentsLayer, [shapeToken], segments_query) as segment_cursor:
                for seg_shape, in segment_cursor:
                    if segments_union is None:
                        segments_union = seg_shape
                    else:
                        segments_union = segments_union.union(seg_shape)

                routeID, route_shape, routeName = line_cursor.next()

                if not segments_union.within(route_shape):
                    warning_messages.append(routeName + ': route does not cover all related segments')
                    ids.add(routeID)

            arcpy.SetProgressorPosition()

        arcpy.SetProgressor('default')
        arcpy.management.SelectLayerByAttribute(segmentsLayer, 'CLEAR_SELECTION')

        #: Found OutAndBack RouteLines that may be oriented in the wrong direction
        head_ids_lookup = {}
        with arcpy.da.SearchCursor(trailheadsLayer, [fldUSNG_TH, shapeToken]) as cursor:
            for usng_id, point in cursor:
                head_ids_lookup[usng_id] = point

        head_route_lookup = {}
        with arcpy.da.SearchCursor(routeToTrailheadsTable, [fldRouteID, fldUSNG_TH]) as cursor:
            for routeID, usng_id in cursor:
                head_route_lookup.setdefault(routeID, []).append(head_ids_lookup[usng_id])

        with arcpy.da.SearchCursor(routeLinesLayer, [fldRouteID, shapeToken, fldRouteName], '{} = \'{}\''.format(fldRouteType, outAndBack)) as cursor:
            for routeID, line, routeName in cursor:
                #: skip routes with more than one trail head
                try:
                    if len(head_route_lookup[routeID]) > 1:
                        continue
                except KeyError:
                    #: skip routes that do not have any associated trailheads
                    continue

                head = head_route_lookup[routeID][0]
                distance_from_start = head.distanceTo(line.firstPoint)
                distance_from_end = head.distanceTo(line.lastPoint)

                if distance_from_start > distance_from_end:
                    warning_messages.append(routeName + ': route may be oriented in the wrong direction')
                    ids.add(routeID)

        if len(ids) > 0:
            messages.addMessage('selecting route lines')
            query = 'RouteID IN (\'{}\')'.format('\', \''.join(ids))
            arcpy.management.SelectLayerByAttribute(routeLinesLayer, 'NEW_SELECTION', query)

            warning_messages.sort()
            for warning in warning_messages:
                messages.addWarningMessage(warning)
        else:
            messages.addMessage('No problems found!')


class BuildRouteLines(object):
    def __init__(self):
        """Define the tool (tool name is the name of the class)."""
        self.label = "BuildRouteLines"
        self.description = "Build routes as polylines."
        self.canRunInBackground = True

    def getParameterInfo(self):
        """Define parameter definitions"""
        routes_table_param = arcpy.Parameter(displayName='Routes Table',
                                             name='routes_table',
                                             datatype='GPTableView',
                                             parameterType='Required',
                                             direction='input')

        return [routes_table_param]

    def execute(self, parameters, messages):
        routesTable = parameters[0].valueAsText
        routesDescribe = arcpy.Describe(routesTable)

        arcpy.env.workspace = dirname(routesDescribe.catalogPath)

        if routesDescribe.FIDSet in [None, '']:
            messages.addMessage('truncating all route lines')
            deleteQuery = None
            totalRoutes = int(arcpy.management.GetCount(routesTable)[0])
        else:
            with arcpy.da.SearchCursor(routesTable, ['RouteID']) as routesCursor:
                deleteRouteIDs = [row[0] for row in routesCursor]

            totalRoutes = len(deleteRouteIDs)
            messages.addMessage('removing any previous route lines for {} selected route(s)'.format(totalRoutes))
            deleteQuery = '{} IN (\'{}\')'.format(fldRouteID, '\', \''.join(deleteRouteIDs))

        with arcpy.da.Editor(arcpy.env.workspace):
            with arcpy.da.UpdateCursor(routeLinesFC, '*', deleteQuery) as updateCursor:
                for row in updateCursor:
                    updateCursor.deleteRow()

            messages.addMessage('building new routes')
            fields = [fldRouteName, fldRouteID, fldRouteType, fldUrl]
            count = 0
            errors = []
            arcpy.SetProgressor('step', 'building {} route line(s)'.format(totalRoutes), count, totalRoutes, 1)
            with arcpy.da.SearchCursor(routesTable, fields) as routeCursor, \
                    arcpy.da.InsertCursor(routeLinesFC, fields + [shapeToken]) as insertCursor:
                for routeName, routeID, routeType, url in routeCursor:
                    try:
                        messages.addMessage(routeName)

                        #: gather seg id's by part
                        partIDs = {}
                        where = '{} = \'{}\''.format(fldRouteID, routeID)
                        with arcpy.da.SearchCursor(routeToSegmentsTable, [fldRouteID, fldUSNG_SEG, fldRoutePart], where) as relationshipCursor:
                            for routeID, segID, partNum in relationshipCursor:
                                partIDs.setdefault(partNum, []).append(segID)

                        #: build feature one part at a time
                        parts = []
                        for partNum in partIDs:
                            partLine = None
                            segsWhere = '{} IN (\'{}\')'.format(fldUSNG_SEG, '\', \''.join(partIDs[partNum]))
                            with arcpy.da.SearchCursor(segmentsFC, [shapeToken], segsWhere) as segsCursor:
                                for segLine, in segsCursor:
                                    if partLine is None:
                                        partLine = segLine
                                    else:
                                        partLine = partLine.union(segLine)
                            parts.append(partLine)

                        points = arcpy.Array()

                        for linePart in parts:
                            part = linePart.getPart(0)
                            if points.count > 0:
                                #: check for line direction
                                if not points[points.count - 1].equals(part[0]):

                                    #: try flipping next part direction
                                    if points[points.count - 1].equals(part[-1]):
                                        part = reverse(part)
                                    elif points[0].equals(part[0]):
                                        #: flip first part direction
                                        points = reverse(points)

                            for point in part:
                                points.add(point)

                        line = arcpy.Polyline(points, utm)

                        insertCursor.insertRow((routeName, routeID, routeType, url, line))
                    except Exception as e:
                        errorMessage = 'Error with {}. \n{}'.format(routeName, e)
                        messages.addErrorMessage(errorMessage)
                        errors.append(errorMessage)

                    count += 1
                    arcpy.SetProgressorPosition()

            arcpy.SetProgressor('default')

            if deleteQuery:
                dataForPostProcessing = arcpy.management.MakeFeatureLayer(routeLinesFC, 'routeLinesLyr', deleteQuery)
            else:
                dataForPostProcessing = routeLinesFC

            messages.AddMessage('calculating lengths')
            arcpy.management.AddGeometryAttributes(dataForPostProcessing, fldLENGTH, Length_Unit='MILES_US')

        messages.addMessage('{} routes processed.'.format(count))

        if len(errors) > 0:
            messages.addErrorMessage('***ERRORS***')
            for errorMessage in errors:
                messages.addErrorMessage(errorMessage)
