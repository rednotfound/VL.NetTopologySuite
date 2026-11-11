using System;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;

namespace VL.NetTopologySuite;

// The view of GeometryEditor's window: geometry coordinates (y north) to window pixels (y down)
// and back. A centre in geometry units and a scale in pixels per unit; the window's own size is
// passed in each time, so a resized window keeps the same centre and scale. Tested by
// SketchViewTests; the layer only holds one.

/// <summary>Geometry coordinates to window pixels and back.</summary>
internal readonly record struct SketchView(double CentreX, double CentreY, double PixelsPerUnit)
{
    /// <summary>How much of the window a fitted shape fills.</summary>
    public const double Fill = 0.8;

    /// <summary>A view that shows <paramref name="envelope"/> filling <see cref="Fill"/> of the window.</summary>
    public static SketchView Fit(Envelope? envelope, Vector2 windowSize)
    {
        var side = Math.Max(1.0, Math.Min(windowSize.X, windowSize.Y));
        if (envelope is null || envelope.IsNull)
            return new SketchView(0, 0, side * Fill / 2); // nothing yet: -1 .. 1 fills the window

        var extent = Math.Max(envelope.Width, envelope.Height);
        if (extent <= 0) // one point, or several on one spot
            return new SketchView(envelope.Centre.X, envelope.Centre.Y, side * Fill / 2);

        // Fit both sides: the window is not square.
        var scale = Math.Min(
            windowSize.X * Fill / Math.Max(envelope.Width, extent * 1e-9),
            windowSize.Y * Fill / Math.Max(envelope.Height, extent * 1e-9));
        return new SketchView(envelope.Centre.X, envelope.Centre.Y, scale);
    }

    public Vector2 ToPixels(Coordinate c, Vector2 windowSize) => new(
        (float)((c.X - CentreX) * PixelsPerUnit + windowSize.X / 2),
        (float)(-(c.Y - CentreY) * PixelsPerUnit + windowSize.Y / 2));

    public Coordinate ToGeometry(Vector2 pixels, Vector2 windowSize) => new(
        CentreX + (pixels.X - windowSize.X / 2) / PixelsPerUnit,
        CentreY - (pixels.Y - windowSize.Y / 2) / PixelsPerUnit);

    /// <summary>Zooms by <paramref name="factor"/>, keeping the point under <paramref name="pixels"/> still.</summary>
    public SketchView ZoomAt(Vector2 pixels, Vector2 windowSize, double factor)
    {
        var anchor = ToGeometry(pixels, windowSize);
        var scale = PixelsPerUnit * factor;
        // The anchor stays under the cursor: centre moves towards it by (1 - 1/factor).
        var k = 1 - 1 / factor;
        return new SketchView(
            CentreX + (anchor.X - CentreX) * k,
            CentreY + (anchor.Y - CentreY) * k,
            scale);
    }

    /// <summary>Moves the view so the content follows a drag of <paramref name="deltaPixels"/>.</summary>
    public SketchView Pan(Vector2 deltaPixels) => this with
    {
        CentreX = CentreX - deltaPixels.X / PixelsPerUnit,
        CentreY = CentreY + deltaPixels.Y / PixelsPerUnit,
    };

    /// <summary>A round grid step: 1, 2 or 5 times a power of ten, at least <paramref name="minPixels"/> apart.</summary>
    public double GridStep(double minPixels)
    {
        var raw = minPixels / PixelsPerUnit;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var m in new[] { 1.0, 2.0, 5.0, 10.0 })
            if (m * power >= raw) return m * power;
        return 10 * power;
    }
}
