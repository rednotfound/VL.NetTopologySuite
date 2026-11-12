using VL.Core;
using VL.Core.Import;

namespace VL.NetTopologySuite;

// Step 2 of the SRID plan (2026-11-12): this package carries no CRS database (an SRID is a label
// here, CrsHints a short table), but VL.PROJ carries PROJ's whole EPSG catalogue. So the editor
// window and the Geometry IOBox ask for a catalogue through this interface, and VL.PROJ, when it
// is loaded, registers one. This package still references nothing of ours: the dependency points
// from VL.PROJ to here, as every sibling's does.
//
// The registration is VL's own: VL.PROJ's AssemblyInitializer (the pattern of VL.IO.Redis,
// VL.Skia, VL.Stride) puts it in AppHost.Global's service registry. GLOBAL, not the app host the
// initializer is given: the editor window and the IOBox viewers run in the HDE companion's own app
// host, which never loads VL.PROJ, and a per-app registry falls back to the global one, never to a
// sibling app's. Without VL.PROJ, or before a document that uses it has been opened, there is no
// catalogue and CrsHints answers from its table.
//
// Hidden smell: a type for package authors, not a node or a type to patch with.

/// <summary>A catalogue of coordinate reference systems, registered by a package that has one (VL.PROJ).</summary>
[Smell(SymbolSmell.Hidden)]
public interface ICrsCatalog
{
    /// <summary>Describes an EPSG code, or returns false when the catalogue does not know it.</summary>
    /// <param name="srid">The EPSG code.</param>
    /// <param name="name">The CRS's name, for example "Amersfoort / RD New".</param>
    /// <param name="isGeographic">True when the coordinates are longitude and latitude in degrees.</param>
    /// <param name="unitName">The unit of the first axis, for example "metre" or "degree".</param>
    bool TryDescribe(int srid, out string name, out bool isGeographic, out string unitName);
}
