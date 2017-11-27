import arcpy
from os.path import join, dirname

arcpy.env.workspace = join(dirname(__file__), r'TrailsAndRoutes_v0_0_3_scott.gdb')
print(arcpy.env.workspace)

for item in arcpy.ListFeatureClasses() + arcpy.ListTables():
    print('truncating: ' + item)
    arcpy.management.TruncateTable(item)

print('truncating: RouteToTrailSegments')
arcpy.management.TruncateTable('RouteToTrailSegments')

print('complete')

