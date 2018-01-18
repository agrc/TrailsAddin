# TrailsAddin Changelog

## v1.6.0
- Make zoom to route work with multiple trailheads.
- Create RoutesToolbox.pyt and BuildRouteLines tool.

## v1.5.0
- Add support for multiple trailheads per route. NOTE: Need to add the RouteToTrailheads table to your project.
- Add route name character count box.
- Change "OneWay" field to "RouteType"

## v1.4.0
- Increased the route name edit box width to allow for longer route names.
- Better line connection validation with line flipping (in memory only).
- Add temporary overlays to help identify disconnected segments.

## v1.3.2
- Remove automatic title-casing of route names. This was causing issues with words that should not have been capitalized (e.g. "via").
- Add support for route names with apostrophes.
- Better validation of segment connectivity. This fixes the bug introduced in v1.3.0 that prevented multipart routes from being created.
- Prevent the related route segment selection functionality from firing when "Build On Select" is selected.

## v1.3.1
- Zoom to route on selection.

## v1.3.0
- Added validation logic for checking that all segments are connected when building a new route.

## v1.2.1
- Fixed bug preventing the selection of the same route twice with clearing the selection in-between.
- Added trailheads to route selection.

## v1.2.0
- Fixed bug preventing USNG IDs from being assigned to newly created segments or trailheads.
- Added function that automatically selects all rows in RouteToTrailSegments and Segments when a row in the Routes table is selected.
