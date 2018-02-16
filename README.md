Trails ArcGIS Pro Add-in & Toolbox
===========

An ArcGIS Pro add-in & toolbox for building trail routes.


# Add-in Installation
1. Download the latest version of the `*.esriAddinX` file from the root of this repository.
1. Download `ProProject/TrailsTemp.gdb.zip` and unzip the geodatabase into `C:\temp`.
1. Download `ProProject/Trails.aprx` and open. The addin should show up as a new "Trails" ribbon.

# Toolbox Installation
1. Download `ProProject/RoutesToolbox.pyt` and add it to your copy of `Trails.aprx`.

# Required map layers (names must be exact):
- `TrailSegments`
- `Trailheads`
- `Temporary Segments`
- `SGID10.INDICES.NationalGrid`
- `Routes` (table)
- `RoutesToTrailSegments` (table)
- `RouteToTrailheads` (table)
