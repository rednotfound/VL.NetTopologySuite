# VL.NetTopologySuite

Geometry for vvvv gamma, built on [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite):

* Points, lines and polygons from coordinates, from shapes by position and size, or drawn by hand in an editor window
* Measuring: area, length, distance, bounds, validity
* Operations: buffer, union, intersection, difference, simplify, hulls, moving and rotating, a point along a line
* Tests: intersects, contains, within, touches and the rest of the spatial predicates
* Well-Known Text and Well-Known Binary, including the hex text PostGIS prints
* Attributes on a geometry, a spatial index over many geometries, shortest paths through a network
* A geometry draws itself in IOBoxes and tooltips, the way a GIS draws it

Coordinates are just numbers: nothing here knows about maps or projections. For those, see the related packages below.

For use with vvvv, the visual live-programming environment for .NET: http://vvvv.org

## Getting started
- Install as [described here](https://thegraybook.vvvv.org/reference/hde/managing-nugets.html) via commandline:

    `nuget install VL.NetTopologySuite -pre`

- Usage examples and more information are included in the pack and can be found via the [Help Browser](https://thegraybook.vvvv.org/reference/hde/findinghelp.html). Start with *Explanation Overview of available nodes*; every node opens its own example with **F1**.
- What changed in each version: [Releases](https://github.com/rednotfound/VL.NetTopologySuite/releases)

## Related packages
- [VL.NetTopologySuite.GeoJSON](https://github.com/rednotfound/VL.NetTopologySuite.GeoJSON): read and write GeoJSON
- [VL.Mapsui](https://github.com/rednotfound/VL.Mapsui): draw geometry on a map
- [VL.PROJ](https://github.com/rednotfound/VL.PROJ): move coordinates from one coordinate system to another

## Contributing
- Report issues on [GitHub](https://github.com/rednotfound/VL.NetTopologySuite/issues)
- How the package is built, tested and released, and why it is shaped the way it is: [the guide](https://github.com/rednotfound/VL.NetTopologySuite/blob/main/docs/GUIDE.md)
- When making a pull-request, please make sure to read the general [guidelines on contributing to vvvv libraries](https://thegraybook.vvvv.org/reference/extending/contributing.html)

## Credits
Based on [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) (BSD-3-Clause) and [NetTopologySuite.Features](https://github.com/NetTopologySuite/NetTopologySuite.Features).

## License
[MIT](https://github.com/rednotfound/VL.NetTopologySuite/blob/main/LICENSE)
