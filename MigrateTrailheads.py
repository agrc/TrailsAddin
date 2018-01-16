from os import path

import arcpy

sde = path.join(path.dirname(path.realpath(__file__)), 'ProProject', 'UtahTrails as TrailsAdmin.sde')
# sde = path.join(path.dirname(path.realpath(__file__)), 'ProProject', 'Trails.gdb')

routes = 'Routes'
routeToTrailheads = 'RouteToTrailheads'

arcpy.env.workspace = sde

print('truncating')
arcpy.management.TruncateTable(routeToTrailheads)

with arcpy.da.SearchCursor(routes, ['RouteID', 'THID_FK']) as routeCursor, arcpy.da.InsertCursor(routeToTrailheads, ['RouteID', 'USNG_TH']) as insertCursor:
    for routeID, thid_FK in routeCursor:
        if thid_FK is not None:
            print(routeID)
            insertCursor.insertRow((routeID, thid_FK))
