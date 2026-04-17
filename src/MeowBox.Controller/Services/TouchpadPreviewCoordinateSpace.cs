using Microsoft.UI.Xaml.Media;
using MeowBox.Core.Models;
using Windows.Foundation;

namespace MeowBox.Controller.Services;

internal sealed class TouchpadPreviewCoordinateSpace
{
    public const double ContactVisualInset = 12d;
    public static readonly double CornerOverlayRadiusCompensation = ContactVisualInset * Math.Sqrt(2d);
    private const double CornerOverlayVisualInset = 0.5d;

    private readonly double _surfaceWidth;
    private readonly double _surfaceHeight;

    private TouchpadPreviewCoordinateSpace(
        double surfaceWidth,
        double surfaceHeight,
        double padLeft,
        double padTop,
        double padWidth,
        double padHeight)
    {
        _surfaceWidth = surfaceWidth;
        _surfaceHeight = surfaceHeight;
        PadLeft = padLeft;
        PadTop = padTop;
        PadWidth = padWidth;
        PadHeight = padHeight;
        PadCornerRadius = GetPadCornerRadius(padWidth, padHeight);
    }

    public double PadLeft { get; }

    public double PadTop { get; }

    public double PadWidth { get; }

    public double PadHeight { get; }

    public double PadCornerRadius { get; }

    public static TouchpadPreviewCoordinateSpace? Create(
        double layoutWidth,
        double layoutHeight,
        int surfaceWidth,
        int surfaceHeight)
    {
        if (layoutWidth <= 0 || layoutHeight <= 0)
        {
            return null;
        }

        var safeSurfaceWidth = Math.Max(1d, surfaceWidth);
        var safeSurfaceHeight = Math.Max(1d, surfaceHeight);
        var targetAspect = safeSurfaceWidth / safeSurfaceHeight;
        var padWidth = Math.Min(layoutWidth, layoutHeight * targetAspect);
        var padHeight = padWidth / targetAspect;
        var padLeft = Math.Max(0d, (layoutWidth - padWidth) / 2d);
        var padTop = Math.Max(0d, (layoutHeight - padHeight) / 2d);

        return new TouchpadPreviewCoordinateSpace(
            safeSurfaceWidth,
            safeSurfaceHeight,
            padLeft,
            padTop,
            padWidth,
            padHeight);
    }

    public Point MapContact(double x, double y)
    {
        return new Point(
            PadLeft + ContactVisualInset + ScaleToContactWidth(x),
            PadTop + ContactVisualInset + ScaleToContactHeight(y));
    }

    public TouchpadPreviewCornerOverlay DescribeCorner(string regionId, TouchpadRegionBoundsConfiguration bounds)
    {
        var region = TouchpadCornerRegionMath.Describe(regionId, bounds);
        var radiusX = ScaleToPadWidth(region.RadiusX) + CornerOverlayRadiusCompensation;
        var radiusY = ScaleToPadHeight(region.RadiusY) + CornerOverlayRadiusCompensation;
        var originX = region.IsRightTop
            ? (PadLeft + PadWidth) - CornerOverlayVisualInset
            : PadLeft + CornerOverlayVisualInset;
        var originY = PadTop + CornerOverlayVisualInset;
        var left = region.IsRightTop ? originX - radiusX : originX;
        var right = region.IsRightTop ? originX : originX + radiusX;
        var bottom = originY + radiusY;
        var labelCenter = new Point(
            region.IsRightTop
                ? right - Math.Max(PadCornerRadius + 12d, radiusX * 0.48d)
                : left + Math.Max(PadCornerRadius + 12d, radiusX * 0.48d),
            originY + Math.Max(PadCornerRadius + 10d, radiusY * 0.48d));

        return new TouchpadPreviewCornerOverlay(
            labelCenter,
            CreateCornerGeometry(region.IsRightTop, left, originY, right, bottom, radiusX, radiusY, PadCornerRadius));
    }

    private double ScaleToPadWidth(double value)
    {
        return (value / _surfaceWidth) * PadWidth;
    }

    private double ScaleToPadHeight(double value)
    {
        return (value / _surfaceHeight) * PadHeight;
    }

    private double ScaleToContactWidth(double value)
    {
        return (value / _surfaceWidth) * Math.Max(0d, PadWidth - (ContactVisualInset * 2d));
    }

    private double ScaleToContactHeight(double value)
    {
        return (value / _surfaceHeight) * Math.Max(0d, PadHeight - (ContactVisualInset * 2d));
    }

    private static Geometry CreateCornerGeometry(
        bool isRightTop,
        double left,
        double top,
        double right,
        double bottom,
        double radiusX,
        double radiusY,
        double padCornerRadius)
    {
        var cornerRadius = Math.Clamp(padCornerRadius, 0d, Math.Max(0d, Math.Min(radiusX, radiusY) - 0.5d));
        if (isRightTop)
        {
            var startPoint = new Point(left, top);
            var topRightStart = new Point(right - cornerRadius, top);
            var topRightEnd = new Point(right, top + cornerRadius);
            var rightEdgeEnd = new Point(right, bottom);
            return new PathGeometry
            {
                Figures =
                [
                    new PathFigure
                    {
                        StartPoint = startPoint,
                        IsClosed = true,
                        Segments =
                        [
                            new LineSegment
                            {
                                Point = topRightStart
                            },
                            new ArcSegment
                            {
                                Point = topRightEnd,
                                Size = new Size(cornerRadius, cornerRadius),
                                SweepDirection = SweepDirection.Clockwise
                            },
                            new LineSegment
                            {
                                Point = rightEdgeEnd
                            },
                            new ArcSegment
                            {
                                Point = startPoint,
                                Size = new Size(radiusX, radiusY),
                                SweepDirection = SweepDirection.Clockwise
                            }
                        ]
                    }
                ]
            };
        }

        return new PathGeometry
        {
            Figures =
            [
                new PathFigure
                {
                    StartPoint = new Point(right, top),
                    IsClosed = true,
                    Segments =
                    [
                        new LineSegment
                        {
                            Point = new Point(left + cornerRadius, top)
                        },
                        new ArcSegment
                        {
                            Point = new Point(left, top + cornerRadius),
                            Size = new Size(cornerRadius, cornerRadius),
                            SweepDirection = SweepDirection.Counterclockwise
                        },
                        new LineSegment
                        {
                            Point = new Point(left, bottom)
                        },
                        new ArcSegment
                        {
                            Point = new Point(right, top),
                            Size = new Size(radiusX, radiusY),
                            SweepDirection = SweepDirection.Counterclockwise
                        }
                    ]
                }
            ]
        };
    }

    private static double GetPadCornerRadius(double padWidth, double padHeight)
    {
        var radius = Math.Min(padWidth, padHeight) * 0.03;
        return Math.Clamp(radius, 10d, 16d);
    }
}

internal readonly record struct TouchpadPreviewCornerOverlay(
    Point LabelCenter,
    Geometry Geometry);
