# TESTS
To be manually performed before cutting each release.

## Add route from selected features
1. Unselect "Build route on select".
1. Select several connected segments and multiple trailheads.
1. Enter a name and hit enter.

Assert that the new route was successfully created.


## Add route using "Build route on select"
1. Select "Build route on select".
1. Build a route with multiple parts.

Assert that the new route was successfully created.


## Can undo and re-add segments
1. Select "Build route on select".
1. Select multiple segments.
1. Click on the "undo" button.

Assert that the segments are removed from the part

1. Select one of the previously removed segments.

Assert that the segment is added back to the route.


## Connectivity validation within a single part
1. Unselect "Build route on select"
1. Select several unconnected segments.
1. Attempt to add a new route.

Assert that an error message is displayed and that the colored overlays are drawn on the map.


## Connectivity validation between parts
1. Select "Build route on select".
1. Attempt to build a multi-part route so that the parts are not connected.

Assert that an error message is displayed and that the colored overlays are drawn on the map.


## Zoom to route
1. Unselect "Build route on select".
1. Select a single route in the routes table.

Assert that the map zoomed to the route and all associated segments and trailheads are selected.


## Zoom to route - multiple rows selected
1. Unselect "Build route on select".
1. Select multiple routes in the routes table.

Assert that the zooming and selecting doesn't fire.


## Cancel New Route Button
1. Select "Build route on select".
1. Start building a multiple part route.
1. Click on "Cancel New Route".

Assert:
- the temporary segments feature class has been truncated

- all trail segments have been unselected
- the "Parts" edit box has been reset to 1

## AddIDs Button
1. Unselect "Build route on select".
1. Select multiple trailheads and segments that do not have USNG IDs populated.
1. Click on "AddIDs".

Assert that the selected features were assigned IDs.

## Review Drop Down
1. Unselect "Build route on select".
1. Select an option in the review drop down and verify that the appropriate records in the associated table were selected.

## Delete Route
1. Select a single route in the routes table.
1. Click on the "Delete Route" button.

Assert that the row in the Routes table and related records in RoutesToTrailSegments and RoutesToTrailheads tables were deleted.
