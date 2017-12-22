'''
BuildRoutes.py

A script that builds trail routes polylines from the Routes and Segments data.
'''

from os import path

import arcpy

sde = path.join(path.dirname(path.realpath(__file__)), 'ProProject', 'UtahTrails as TrailsAdmin.sde')
# sde = path.join(path.dirname(path.realpath(__file__)), 'ProProject', 'Trails.gdb')
routeLinesFC = 'RouteLines'
routesTable = 'Routes'
routeToSegmentsTable = 'RouteToTrailSegments'
segmentsFC = 'TrailSegments'

#: fields
fldRouteName = 'RouteName'
fldTHID_FK = 'THID_FK'
fldRouteID = 'RouteID'
fldOutAndBack = 'OutAndBack'
fldURL = 'URL'
fldUSNG_SEG = 'USNG_SEG'
fldRoutePart = 'RoutePart'

shapeToken = 'SHAPE@'
utm = arcpy.SpatialReference(26912)

arcpy.env.workspace = sde

print('truncating {}'.format(routeLinesFC))
arcpy.management.TruncateTable(routeLinesFC)

fields = [fldRouteName, fldRouteID, fldTHID_FK, fldOutAndBack, fldURL]
count = 0
with arcpy.da.SearchCursor(routesTable, fields) as routeCursor, \
        arcpy.da.InsertCursor(routeLinesFC, fields + [shapeToken]) as insertCursor:
    for routeName, routeID, thID, outAndBack, url in routeCursor:
        print(routeName)

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
                    print('flipping')
                    reversedArray = arcpy.Array()
                    for index in range(part.count - 1, -1, -1):
                        reversedArray.add(part[index])
                    part = reversedArray
            for point in part:
                points.add(point)

        line = arcpy.Polyline(points, utm)

        insertCursor.insertRow((routeName, routeID, thID, outAndBack, url, line))
        count += 1

print('{} routes created.'.format(count))
