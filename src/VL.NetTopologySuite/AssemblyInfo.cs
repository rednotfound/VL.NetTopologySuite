using System.Runtime.CompilerServices;
using VL.Core.Import;

// The defensive-copy helpers the tests assert on are internal: they exist to catch a regression,
// not to be part of the node surface.
[assembly: InternalsVisibleTo("VL.NetTopologySuite.Tests")]

// Without this every public static method is demoted to a raw .NET reflection node, which the
// NodeBrowser hides. The package still loads, compiles and packs with zero warnings, so the
// symptom is indistinguishable from the package not loading at all. Nine VL.GIS releases
// shipped that way.
//
// The "VL" prefix is also what makes the category rule work: category is the namespace minus
// this prefix, plus the type name. Namespace VL.NTS therefore gives NTS.
[assembly: ImportAsIs(Namespace = "VL")]
