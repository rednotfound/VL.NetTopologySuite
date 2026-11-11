using NetTopologySuite.Geometries;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// SetSRID: a label, never a transform. Same coordinates, the new SRID on the geometry and on its
/// factory, and the input untouched.
/// </summary>
public class SetSridTests
{
    private static Polygon Square(int srid)
    {
        var f = new GeometryFactory(new PrecisionModel(), srid);
        return f.CreatePolygon(new[] { new Coordinate(0, 0), new Coordinate(2, 0), new Coordinate(2, 2), new Coordinate(0, 2), new Coordinate(0, 0) });
    }

    [Fact]
    public void The_label_changes_and_the_numbers_do_not()
    {
        var input = Square(0);
        var output = GeometryNodes.SetSRID(input, 28992)!;
        Assert.Equal(28992, output.SRID);
        Assert.True(output.EqualsExact(input));
    }

    [Fact]
    public void The_factory_carries_the_SRID_so_a_later_operation_keeps_it()
    {
        var output = GeometryNodes.SetSRID(Square(0), 28992)!;
        Assert.Equal(28992, output.Factory.SRID);
        Assert.Equal(28992, output.Buffer(1).SRID);
        Assert.Equal(28992, OperationNodes.Union(output, output)!.SRID);
    }

    [Fact]
    public void The_input_is_never_touched_and_the_output_shares_no_coordinate_with_it()
    {
        var input = Square(4326);
        var output = GeometryNodes.SetSRID(input, 28992)!;
        Assert.Equal(4326, input.SRID);
        output.Coordinates[0].X = 99; // NTS hands out live references; this must not reach the input
        Assert.Equal(0, input.Coordinates[0].X);
    }

    [Fact]
    public void Zero_clears_the_label_and_null_stays_null()
    {
        Assert.Equal(0, GeometryNodes.SetSRID(Square(4326), 0)!.SRID);
        Assert.Null(GeometryNodes.SetSRID(null, 4326));
    }

    [Fact]
    public void Collections_are_labelled_all_the_way_down()
    {
        var f = new GeometryFactory();
        var mp = f.CreateMultiPolygon(new[] { Square(0), Square(0) });
        var output = (MultiPolygon)GeometryNodes.SetSRID(mp, 3857)!;
        Assert.Equal(3857, output.SRID);
        Assert.Equal(3857, output.GetGeometryN(1).SRID);
    }
}
