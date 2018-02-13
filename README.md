TrailsAddin
===========

An ArcGIS Pro addin for building trail routes.


# Installation
1. Download the latest version of the `*.esriAddinX` file from the root of this repository.
1. Download `ProProject/TrailsTemp.gdb.zip` and unzip the geodatabase into `C:\temp`.
1. Download `ProProject/Trails.aprx` and open. The addin should show up as a new "Trails" ribbon.

# Required map layers (names must be exact):
- `TrailSegments`
- `Trailheads`
- `Temporary Segments`
- `SGID10.INDICES.NationalGrid`
- `Routes` (table)
- `RoutesToTrailSegments` (table)
- `RouteToTrailheads` (table)
