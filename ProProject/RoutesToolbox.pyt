'''
RoutesToolbox.py

A tool that builds trail routes polylines from the Routes and Segments data.
'''
from os.path import dirname

import arcpy

routeLinesFC = 'RouteLines'
routeToSegmentsTable = 'RouteToTrailSegments'
segmentsFC = 'TrailSegments'

#: fields
fldRouteName = 'RouteName'
fldTHID_FK = 'THID_FK'
fldRouteID = 'RouteID'
fldRouteType = 'RouteType'
fldURL = 'URL'
fldUSNG_SEG = 'USNG_SEG'
fldRoutePart = 'RoutePart'

shapeToken = 'SHAPE@'
utm = arcpy.SpatialReference(26912)
outAndBack = 'Out & Back'


class Toolbox(object):
    def __init__(self):
        """Define the toolbox (the name of the toolbox is the name of the
        .pyt file)."""
        self.label = "Toolbox"
        self.alias = ""

        # List of tool classes associated with this toolbox
        self.tools = [BuildRouteLines]


class BuildRouteLines(object):
    def __init__(self):
        """Define the tool (tool name is the name of the class)."""
        self.label = "BuildRouteLines"
        self.description = "Build routes as polylines."
        self.canRunInBackground = False

    def getParameterInfo(self):
        """Define parameter definitions"""
        routes_table_param = arcpy.Parameter(displayName='Routes Table',
                                             name='routes_table',
                                             datatype='GPTableView',
                                             parameterType='Required',
                                             direction='input')

        return [routes_table_param]

    def isLicensed(self):
        """Set whether tool is licensed to execute."""
        return True

    def updateParameters(self, parameters):
        """Modify the values and properties of parameters before internal
        validation is performed.  This method is called whenever a parameter
        has been changed."""
        return

    def updateMessages(self, parameters):
        """Modify the messages created by internal validation for each tool
        parameter.  This method is called after internal validation."""
        return

    def execute(self, parameters, messages):
        routesTable = parameters[0].valueAsText
        routesDescribe = arcpy.Describe(routesTable)

        arcpy.env.workspace = dirname(routesDescribe.catalogPath)

        with arcpy.da.Editor(arcpy.env.workspace):
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

            with arcpy.da.UpdateCursor(routeLinesFC, ['OID@'], deleteQuery) as updateCursor:
                for row in updateCursor:
                    updateCursor.deleteRow()

            messages.addMessage('building new routes')
            fields = [fldRouteName, fldRouteID, fldRouteType, fldURL]
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
                                    #: flip part direction
                                    reversedArray = arcpy.Array()
                                    for index in range(part.count - 1, -1, -1):
                                        reversedArray.add(part[index])
                                    part = reversedArray
                            for point in part:
                                points.add(point)

                        if routeType == outAndBack:
                            #: add new part(s) in reverse for the "back" of out and back
                            copyPoints = list(points)
                            for index in range(len(copyPoints) - 1, -1, -1):
                                points.add(copyPoints[index])

                        line = arcpy.Polyline(points, utm)

                        insertCursor.insertRow((routeName, routeID, routeType, url, line))
                    except Exception as e:
                        errorMessage = 'Error with {}. \n{}'.format(routeName, e)
                        messages.addErrorMessage(errorMessage)
                        errors.append(errorMessage)

                    count += 1
                    arcpy.SetProgressorPosition()

            messages.AddMessage('calculating lengths')
            arcpy.SetProgressor('default')

            arcpy.management.AddGeometryAttributes(routeLinesFC, 'LENGTH', Length_Unit='MILES_US')
        messages.addMessage('{} routes processed.'.format(count))

        if len(errors) > 0:
            messages.addErrorMessage('***ERRORS***')
            for errorMessage in errors:
                messages.addErrorMessage(errorMessage)
