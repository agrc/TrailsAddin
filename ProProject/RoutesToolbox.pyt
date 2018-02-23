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
fldElevationGain = 'ElevationGain'
fldElevationProfile = 'ElevationProfile'

shapeToken = 'SHAPE@'
utm = arcpy.SpatialReference(26912)
outAndBack = 'Out & Back'
interpolated = join(arcpy.env.scratchGDB, 'interpolatedRouteLines')
profile_points_max_num = 100
meters_to_feet = 3.28084


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
        route_lines_param = arcpy.Parameter(displayName='RouteLines Layer',
                                            name='route_lines_layer',
                                            datatype='GPFeatureLayer',
                                            parameterType='Required',
                                            direction='input')
        segments_param = arcpy.Parameter(displayName='TrailSegments Layer',
                                         name='segments_layer',
                                         datatype='GPFeatureLayer',
                                         parameterType='Required',
                                         direction='input')

        return [route_lines_param, segments_param]

    def execute(self, parameters, messages):
        routeLinesLayer = parameters[0].valueAsText
        segmentsLayer = parameters[1].valueAsText
        nonoverlapping = join(arcpy.env.scratchGDB, 'nonoverlapping')

        messages.addMessage('clearing selection on layers')
        arcpy.management.SelectLayerByAttribute(routeLinesLayer, 'CLEAR_SELECTION')
        arcpy.management.SelectLayerByAttribute(segmentsLayer, 'CLEAR_SELECTION')

        #: Find RouteLines that are not overlapped by TrailSegments
        if arcpy.Exists(nonoverlapping):
            messages.addMessage('removing old output')
            arcpy.management.Delete(nonoverlapping)

        messages.addMessage('running symmetrical difference tool')
        arcpy.analysis.SymDiff(routeLinesLayer, segmentsLayer, nonoverlapping)

        messages.addMessage('gathering routeIDs from output')
        ids = set()
        with arcpy.da.SearchCursor(nonoverlapping, 'RouteID', 'RouteID IS NOT NULL') as cursor:
            for routeID, in cursor:
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
            with arcpy.da.SearchCursor(routeLinesLayer, [fldRouteID, shapeToken], lines_query) as line_cursor, \
                    arcpy.da.SearchCursor(segmentsLayer, [shapeToken], segments_query) as segment_cursor:
                for seg_shape, in segment_cursor:
                    if segments_union is None:
                        segments_union = seg_shape
                    else:
                        segments_union = segments_union.union(seg_shape)

                routeID, route_shape = line_cursor.next()

                if not segments_union.within(route_shape):
                    ids.add(routeID)

            arcpy.SetProgressorPosition()

        arcpy.SetProgressor('default')
        arcpy.management.SelectLayerByAttribute(segmentsLayer, 'CLEAR_SELECTION')

        if len(ids) > 0:
            messages.addMessage('selecting route lines')
            query = 'RouteID IN (\'{}\')'.format('\', \''.join(ids))
            arcpy.management.SelectLayerByAttribute(routeLinesLayer, 'NEW_SELECTION', query)
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
        dem_param = arcpy.Parameter(displayName='SGID10.RASTER.USGS_DEM_10Meter',
                                    name='dem',
                                    datatype='DERasterDataset',
                                    parameterType='Required',
                                    direction='input')

        return [routes_table_param, dem_param]

    def execute(self, parameters, messages):
        routesTable = parameters[0].valueAsText
        dem = parameters[1].valueAsText
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

            if arcpy.Exists(interpolated):
                messages.AddMessage('cleaning up old interpolated output')
                arcpy.management.Delete(interpolated)

            messages.AddMessage('interpolating shapes')
            arcpy.ddd.InterpolateShape(dem, dataForPostProcessing, interpolated, method='BILINEAR')
            interpolated_lookup = {}
            with arcpy.da.SearchCursor(interpolated, [fldRouteID, 'Shape@']) as cursor:
                for routeID, shape in cursor:
                    interpolated_lookup[routeID] = shape

            messages.AddMessage('updating elevation gains and profiles')
            with arcpy.da.UpdateCursor(dataForPostProcessing, [fldRouteID, fldElevationGain, fldElevationProfile]) as cursor:
                for routeID, gain, profile in cursor:
                    interp_line = interpolated_lookup[routeID]
                    gain = calculate_elevation_gain(interp_line, messages)
                    profile = generate_elevation_profile(interp_line, messages)

                    cursor.updateRow((routeID, gain, profile))

        messages.addMessage('{} routes processed.'.format(count))

        if len(errors) > 0:
            messages.addErrorMessage('***ERRORS***')
            for errorMessage in errors:
                messages.addErrorMessage(errorMessage)


def calculate_elevation_gain(line, messages):
    if line.partCount > 1:
        messages.addErrorMessage('MULTI-PART LINE Detected!!')

    gain = 0
    previous_elevation = None
    for point in line.getPart(0):
        if previous_elevation is not None and previous_elevation < point.Z:
            gain += point.Z - previous_elevation

        previous_elevation = point.Z

    return round(gain * meters_to_feet)


def generate_elevation_profile(line, messages):
    elevations = [point.Z * meters_to_feet for point in line.getPart(0)]

    #: generalize elevations if there are more than the max num
    num_points = len(elevations)
    if num_points > profile_points_max_num:
        factor = round(num_points/profile_points_max_num)
        generalized_elevations = []
        for index in range(0, num_points - 1, factor):
            generalized_elevations.append(elevations[index])

        elevations = generalized_elevations

    #: convert elevations to percentages (of max - min) to save on space
    max_elevation = max(elevations)
    min_elevation = min(elevations)

    percent_elevations = []
    for elev in elevations:
        percent_elevations.append(str(round(((elev - min_elevation) / (max_elevation - min_elevation)) * 100)))

    return ','.join([str(round(min_elevation)), str(round(max_elevation))] + percent_elevations)
