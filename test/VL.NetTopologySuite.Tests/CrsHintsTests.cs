using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// The short SRID table GeometryEditor's window uses: lon/lat codes are degrees, the common grids
/// are metres, and anything else says it does not know rather than guessing.
/// </summary>
public class CrsHintsTests
{
    [Theory]
    [InlineData(4326)] [InlineData(4258)] [InlineData(4490)] [InlineData(6668)] [InlineData(4979)]
    public void Geographic_codes_are_degrees(int srid)
        => Assert.Equal(CrsHints.Units.Degrees, CrsHints.UnitsOf(srid));

    [Theory]
    [InlineData(3857)] [InlineData(28992)] [InlineData(7415)] [InlineData(32631)] [InlineData(32755)] [InlineData(25832)] [InlineData(6677)]
    public void Projected_grids_are_metres(int srid)
        => Assert.Equal(CrsHints.Units.Metres, CrsHints.UnitsOf(srid));

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(2263)] [InlineData(123456)]
    public void Anything_else_is_unknown(int srid)
        => Assert.Equal(CrsHints.Units.Unknown, CrsHints.UnitsOf(srid));

    [Fact]
    public void Labels_say_the_units()
    {
        Assert.Equal("SRID unset", CrsHints.Label(0));
        Assert.Equal("EPSG:4326 · degrees", CrsHints.Label(4326));
        Assert.Equal("EPSG:28992 · metres", CrsHints.Label(28992));
        Assert.Equal("EPSG:2263 · units unknown", CrsHints.Label(2263)); // NY state plane is in US feet
    }
   // ---- a registered catalogue (step 2: VL.PROJ registers one) ---------------------------------

    private sealed class FakeCatalog : ICrsCatalog
    {
        public int Asked;
        public bool TryDescribe(int srid, out string name, out bool isGeographic, out string unitName)
        {
            Asked++;
            (name, isGeographic, unitName) = srid switch
            {
                28992 => ("Amersfoort / RD New", false, "metre"),
                4326 => ("WGS 84", true, "degree"),
                2263 => ("NAD83 / New York Long Island (ftUS)", false, "US survey foot"),
                _ => ("", false, ""),
            };
            return name.Length > 0;
        }
    }

    [Fact]
    public void A_catalogue_names_the_CRS_and_knows_codes_the_table_does_not()
    {
        var catalog = new FakeCatalog();
        CrsHints.CatalogOverride = catalog;
        try
        {
            Assert.Equal("EPSG:28992 Amersfoort / RD New · metres", CrsHints.Label(28992));
            Assert.Equal("EPSG:4326 WGS 84 · degrees", CrsHints.Label(4326));
            Assert.Equal("EPSG:2263 NAD83 / New York Long Island (ftUS) · US survey foot", CrsHints.Label(2263));
            Assert.Equal(CrsHints.Units.Other, CrsHints.UnitsOf(2263));
            Assert.Equal("EPSG:7415 · metres", CrsHints.Label(7415)); // the catalogue does not know it: the table answers
            Assert.Equal("EPSG:2263 · US survey foot", CrsHints.Label(2263, named: false)); // a narrow status bar
        }
        finally { CrsHints.CatalogOverride = null; }
    }

    [Fact]
    public void A_catalogue_is_asked_once_per_code()
    {
        var catalog = new FakeCatalog();
        CrsHints.CatalogOverride = catalog;
        try
        {
            for (var i = 0; i < 60; i++) CrsHints.Label(28992); // a second of frames
            Assert.Equal(1, catalog.Asked);
        }
        finally { CrsHints.CatalogOverride = null; }
    }
}