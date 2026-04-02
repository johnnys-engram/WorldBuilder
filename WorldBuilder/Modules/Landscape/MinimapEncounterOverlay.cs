using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldBuilder.Modules.Landscape;

/// <summary>
/// Draws ACE encounter spawn dots on the minimap. Zoom and highlight are controlled from the landscape toolbar.
/// Orange = mob spawn position, Yellow = generator position.
/// </summary>
public class MinimapEncounterOverlay : Control {
    public static readonly StyledProperty<IReadOnlyList<EncounterMapDot>?> DotsProperty =
        AvaloniaProperty.Register<MinimapEncounterOverlay, IReadOnlyList<EncounterMapDot>?>(nameof(Dots));

    public static readonly StyledProperty<Vector3> CameraPositionProperty =
        AvaloniaProperty.Register<MinimapEncounterOverlay, Vector3>(nameof(CameraPosition));

    /// <summary>Values &gt; 1 zoom in (fewer world units across the minimap).</summary>
    public static readonly StyledProperty<double> ZoomFactorProperty =
        AvaloniaProperty.Register<MinimapEncounterOverlay, double>(nameof(ZoomFactor), 1.35);

    public static readonly StyledProperty<bool> HighlightEncountersProperty =
        AvaloniaProperty.Register<MinimapEncounterOverlay, bool>(nameof(HighlightEncounters));

    public IReadOnlyList<EncounterMapDot>? Dots {
        get => GetValue(DotsProperty);
        set => SetValue(DotsProperty, value);
    }

    public Vector3 CameraPosition {
        get => GetValue(CameraPositionProperty);
        set => SetValue(CameraPositionProperty, value);
    }

    public double ZoomFactor {
        get => GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    public bool HighlightEncounters {
        get => GetValue(HighlightEncountersProperty);
        set => SetValue(HighlightEncountersProperty, value);
    }

    static MinimapEncounterOverlay() {
        DotsProperty.Changed.AddClassHandler<MinimapEncounterOverlay>((x, _) => x.InvalidateVisual());
        CameraPositionProperty.Changed.AddClassHandler<MinimapEncounterOverlay>((x, _) => x.InvalidateVisual());
        ZoomFactorProperty.Changed.AddClassHandler<MinimapEncounterOverlay>((x, _) => x.InvalidateVisual());
        HighlightEncountersProperty.Changed.AddClassHandler<MinimapEncounterOverlay>((x, _) => x.InvalidateVisual());
    }

    private static readonly IBrush _generatorBrush = new SolidColorBrush(Color.FromRgb(255, 235, 80));
    private static readonly IBrush _monsterBrush   = new SolidColorBrush(Color.FromRgb(255, 120, 0));
    private static readonly IBrush _monsterBrushHi = new SolidColorBrush(Color.FromRgb(255, 200, 40));
    private static readonly IPen   _outlinePen    = new Pen(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), 0.5);
    private static readonly IPen   _outlinePenHi   = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 1.0);

    public override void Render(DrawingContext context) {
        var dots = Dots;
        if (dots == null || dots.Count == 0) return;

        var cam   = CameraPosition;
        double w  = Bounds.Width;
        double h  = Bounds.Height;

        const float baseRange = 20f * 192f;
        double zoom = ZoomFactor;
        if (zoom < 0.5) zoom = 0.5;
        if (zoom > 6.0) zoom = 6.0;
        float totalRange = (float)(baseRange / zoom);
        double scale = w / totalRange;
        double cx    = w / 2.0;
        double cy    = h / 2.0;

        bool hi = HighlightEncounters;
        double dotR = hi ? 5.0 : 2.5;
        double halfRange = totalRange / 2.0;

        foreach (var dot in dots) {
            double relX = dot.WorldX - cam.X;
            double relY = dot.WorldY - cam.Y;

            if (Math.Abs(relX) > halfRange || Math.Abs(relY) > halfRange) continue;

            double px = cx + relX * scale;
            double py = cy - relY * scale;

            var brush  = dot.IsGenerator ? _generatorBrush : (hi ? _monsterBrushHi : _monsterBrush);
            var pen    = hi ? _outlinePenHi : _outlinePen;
            context.DrawEllipse(brush, pen, new Point(px, py), dotR, dotR);
        }
    }
}
