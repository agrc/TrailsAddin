from os.path import abspath, dirname, join

import arcpy

current_directory = abspath(dirname(__file__))

local = join(current_directory, 'UtahTrails_Local as TrailsAdmin.sde')
prod = join(current_directory, 'UtahTrails as TrailsAdmin.sde')

datasets = ['RouteLines', 'Trailheads', 'TrailSegments', 'Routes', 'RouteToTrailheads', 'RouteToTrailSegments']

for ds in datasets:
    print(ds)
    arcpy.management.DeleteRows(join(local, ds))
    arcpy.management.Append(join(prod, ds), join(local, ds))

print('compressing')
arcpy.management.Compress(local)


print('done')
