using System.Collections.Generic;
using NetTopologySuite.Geometries;
using SkiaSharp;
using Stride.Core.Mathematics;
using VL.Lib.IO.Notifications;
using VL.Skia;

namespace VL.NetTopologySuite;

// GeometryFillNode's layer: one EvenOdd path per polygon, rebuilt when the geometry reference changes.
internal sealed class GeometryFillLayer : ILayer
{
    private Geometry? _geometry;
    private readonly SKPaint _paint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly List<SKPath> _paths = new();

    public void Set(Geometry? geometry, Color4 color)
    {
        _paint.Color = new SKColor((byte)(Clamp(color.R) * 255), (byte)(Clamp(color.G) * 255), (byte)(Clamp(color.B) * 255), (byte)(Clamp(color.A) * 255));
        if (ReferenceEquals(geometry, _geometry)) return;
        _geometry = geometry;
        foreach (var p in _paths) p.Dispose();
        _paths.Clear();
        foreach (var rings in GeometryFillNode.PolygonRings(geometry))
        {
            var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var ring in rings)
            {
                if (ring.Length < 3) continue;
                path.MoveTo(ring[0].X, ring[0].Y);
                for (var i = 1; i < ring.Length; i++) path.LineTo(ring[i].X, ring[i].Y);
                path.Close();
            }
            _paths.Add(path);
        }
    }

    private static float Clamp(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

    public RectangleF? Bounds => null;

    public void Render(CallerInfo caller)
    {
        foreach (var path in _paths) caller.Canvas.DrawPath(path, _paint);
    }

    public bool Notify(INotification notification, CallerInfo caller) => false;
}
