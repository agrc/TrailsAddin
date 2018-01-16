
from os import path

import arcpy

# sde = path.join(path.dirname(path.realpath(__file__)), 'ProProject', 'UtahTrails as TrailsAdmin.sde')
sde = path.join(path.dirname(path.realpath(__file__)), 'ProProject', 'Trails.gdb')

routes = 'Routes'


arcpy.env.workspace = sde
with arcpy.da.UpdateCursor(routes, ['RouteType']) as cursor:
    for routeType, in cursor:
        if routeType == 'Yes':
            newRouteType = 'Loop'
        else:
            newRouteType = 'Out & Back'
        cursor.updateRow((newRouteType,))
