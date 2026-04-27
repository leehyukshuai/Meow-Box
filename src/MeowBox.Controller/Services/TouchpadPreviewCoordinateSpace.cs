using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using MeowBox.Core.Models;
using Windows.Foundation;

namespace MeowBox.Controller.Services;

internal sealed class TouchpadPreviewCoordinateSpace
{
    public const double ContactVisualInset = 12d;
    public static readonly double CornerOverlayRadiusCompensation = ContactVisualInset * Math.Sqrt(2d);
    private const double CornerOverlayVisualInset = 0.5d;
    private const double EdgeOverlayWidthRatio = 0.06d;

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

    public double EdgeOverlayWidth => PadWidth * EdgeOverlayWidthRatio;

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
            PadLeft + ScaleToPadWidth(x),
            PadTop + ScaleToPadHeight(y));
    }

    public TouchpadPreviewCornerOverlay DescribeCorner(string regionId, TouchpadRegionBoundsConfiguration bounds)
    {
        var shape = GetCornerOverlayShape(regionId, bounds);
        var labelCenter = new Point(
            shape.IsRightTop
                ? shape.Right - Math.Max(PadCornerRadius + 12d, shape.RadiusX * 0.48d)
                : shape.Left + Math.Max(PadCornerRadius + 12d, shape.RadiusX * 0.48d),
            shape.Top + Math.Max(PadCornerRadius + 10d, shape.RadiusY * 0.48d));

        return new TouchpadPreviewCornerOverlay(
            labelCenter,
            CreateCornerGeometry(shape.IsRightTop, shape.Left, shape.Top, shape.Right, shape.Bottom, shape.RadiusX, shape.RadiusY, PadCornerRadius),
            CreateCornerStrokeGeometry(shape.IsRightTop, shape.Left, shape.Top, shape.Right, shape.Bottom, shape.RadiusX, shape.RadiusY));
    }

    public TouchpadPreviewEdgeOverlay DescribeEdge(
        bool leftSide,
        string excludedRegionId,
        TouchpadRegionBoundsConfiguration excludedBounds)
    {
        var edgeWidth = EdgeOverlayWidth;
        var left = leftSide ? PadLeft : (PadLeft + PadWidth - edgeWidth);
        var excludedCorner = GetCornerOverlayShape(excludedRegionId, excludedBounds);
        var geometry = CreatePlainEdgeGeometry(leftSide, left, edgeWidth, excludedCorner);
        var labelX = left + (edgeWidth / 2d);
        var labelTop = GetEdgeTopBoundaryY(labelX, excludedCorner);
        var labelCenter = new Point(
            labelX,
            labelTop + ((PadTop + PadHeight - labelTop) / 2d));

        return new TouchpadPreviewEdgeOverlay(
            left,
            PadTop,
            edgeWidth,
            PadHeight,
            labelCenter,
            geometry,
            CreateEdgeStrokeGeometry(leftSide, left, edgeWidth, excludedCorner));
    }

    public Geometry CreatePadClipGeometry(double offsetX = 0d, double offsetY = 0d)
    {
        return CreateRoundedRectGeometry(
            PadLeft - offsetX,
            PadTop - offsetY,
            PadLeft + PadWidth - offsetX,
            PadTop + PadHeight - offsetY,
            PadCornerRadius);
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

    private static Geometry CreateCornerStrokeGeometry(
        bool isRightTop,
        double left,
        double top,
        double right,
        double bottom,
        double radiusX,
        double radiusY)
    {
        return new PathGeometry
        {
            Figures =
            [
                isRightTop
                    ? new PathFigure
                    {
                        StartPoint = new Point(right, bottom),
                        IsClosed = false,
                        Segments =
                        [
                            new ArcSegment
                            {
                                Point = new Point(left, top),
                                Size = new Size(radiusX, radiusY),
                                SweepDirection = SweepDirection.Clockwise
                            }
                        ]
                    }
                    : new PathFigure
                    {
                        StartPoint = new Point(left, bottom),
                        IsClosed = false,
                        Segments =
                        [
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

    private static Geometry CreateRoundedRectGeometry(
        double left,
        double top,
        double right,
        double bottom,
        double radius)
    {
        var safeRadius = Math.Clamp(radius, 0d, Math.Min((right - left) / 2d, (bottom - top) / 2d));
        return new PathGeometry
        {
            Figures =
            [
                new PathFigure
                {
                    StartPoint = new Point(left + safeRadius, top),
                    IsClosed = true,
                    Segments =
                    [
                        new LineSegment
                        {
                            Point = new Point(right - safeRadius, top)
                        },
                        new ArcSegment
                        {
                            Point = new Point(right, top + safeRadius),
                            Size = new Size(safeRadius, safeRadius),
                            SweepDirection = SweepDirection.Clockwise
                        },
                        new LineSegment
                        {
                            Point = new Point(right, bottom - safeRadius)
                        },
                        new ArcSegment
                        {
                            Point = new Point(right - safeRadius, bottom),
                            Size = new Size(safeRadius, safeRadius),
                            SweepDirection = SweepDirection.Clockwise
                        },
                        new LineSegment
                        {
                            Point = new Point(left + safeRadius, bottom)
                        },
                        new ArcSegment
                        {
                            Point = new Point(left, bottom - safeRadius),
                            Size = new Size(safeRadius, safeRadius),
                            SweepDirection = SweepDirection.Clockwise
                        },
                        new LineSegment
                        {
                            Point = new Point(left, top + safeRadius)
                        },
                        new ArcSegment
                        {
                            Point = new Point(left + safeRadius, top),
                            Size = new Size(safeRadius, safeRadius),
                            SweepDirection = SweepDirection.Clockwise
                        }
                    ]
                }
            ]
        };
    }

    private TouchpadPreviewCornerShape GetCornerOverlayShape(string regionId, TouchpadRegionBoundsConfiguration bounds)
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
        return new TouchpadPreviewCornerShape(region.IsRightTop, left, originY, right, bottom, radiusX, radiusY);
    }

    private static PathFigure CloneFigure(PathFigure source)
    {
        PathSegmentCollection segments = [];
        foreach (var segment in source.Segments)
        {
            segments.Add(CloneSegment(segment));
        }

        return new PathFigure
        {
            StartPoint = source.StartPoint,
            IsClosed = source.IsClosed,
            IsFilled = source.IsFilled,
            Segments = segments
        };
    }

    private static PathSegment CloneSegment(PathSegment source)
    {
        return source switch
        {
            LineSegment line => new LineSegment
            {
                Point = line.Point
            },
            ArcSegment arc => new ArcSegment
            {
                Point = arc.Point,
                Size = arc.Size,
                RotationAngle = arc.RotationAngle,
                IsLargeArc = arc.IsLargeArc,
                SweepDirection = arc.SweepDirection
            },
            BezierSegment bezier => new BezierSegment
            {
                Point1 = bezier.Point1,
                Point2 = bezier.Point2,
                Point3 = bezier.Point3
            },
            QuadraticBezierSegment quadratic => new QuadraticBezierSegment
            {
                Point1 = quadratic.Point1,
                Point2 = quadratic.Point2
            },
            PolyLineSegment polyLine => new PolyLineSegment
            {
                Points = ClonePointCollection(polyLine.Points)
            },
            PolyBezierSegment polyBezier => new PolyBezierSegment
            {
                Points = ClonePointCollection(polyBezier.Points)
            },
            PolyQuadraticBezierSegment polyQuadratic => new PolyQuadraticBezierSegment
            {
                Points = ClonePointCollection(polyQuadratic.Points)
            },
            _ => throw new NotSupportedException($"Unsupported path segment type: {source.GetType().Name}")
        };
    }

    private static PointCollection ClonePointCollection(PointCollection source)
    {
        PointCollection points = [];
        foreach (var point in source)
        {
            points.Add(point);
        }

        return points;
    }

    private PathGeometry CreatePlainEdgeGeometry(
        bool leftSide,
        double left,
        double width,
        TouchpadPreviewCornerShape excludedCorner)
    {
        var right = left + width;
        var topPoints = leftSide
            ? SampleBoundaryPoints(right, left, 18, x => new Point(x, GetEdgeTopBoundaryY(x, excludedCorner)))
            : SampleBoundaryPoints(left, right, 18, x => new Point(x, GetEdgeTopBoundaryY(x, excludedCorner)));
        var bottomPoints = leftSide
            ? SampleBoundaryPoints(left, right, 18, x => new Point(x, GetBottomBoundaryY(x)))
            : SampleBoundaryPoints(right, left, 18, x => new Point(x, GetBottomBoundaryY(x)));

        return CreateClosedGeometry([.. topPoints, .. bottomPoints]);
    }

    private Geometry CreateEdgeStrokeGeometry(
        bool leftSide,
        double left,
        double width,
        TouchpadPreviewCornerShape excludedCorner)
    {
        var innerX = leftSide ? left + width : left;
        var top = GetEdgeTopBoundaryY(innerX, excludedCorner);
        var bottom = GetBottomBoundaryY(innerX);

        return new PathGeometry
        {
            Figures =
            [
                new PathFigure
                {
                    StartPoint = new Point(innerX, top),
                    IsClosed = false,
                    Segments =
                    [
                        new LineSegment
                        {
                            Point = new Point(innerX, bottom)
                        }
                    ]
                }
            ]
        };
    }

    private double GetEdgeTopBoundaryY(double x, TouchpadPreviewCornerShape excludedCorner)
    {
        var top = GetTopBoundaryY(x);
        if (x < excludedCorner.Left || x > excludedCorner.Right)
        {
            return top;
        }

        return Math.Max(top, GetCornerBoundaryY(excludedCorner, x));
    }

    private static PathGeometry CreateClosedGeometry(IReadOnlyList<Point> points)
    {
        PathSegmentCollection segments = [];
        foreach (var point in points.Skip(1))
        {
            segments.Add(new LineSegment { Point = point });
        }

        return new PathGeometry
        {
            Figures =
            [
                new PathFigure
                {
                    StartPoint = points[0],
                    IsClosed = true,
                    Segments = segments
                }
            ]
        };
    }

    private static List<Point> SampleBoundaryPoints(double startX, double endX, int sampleCount, Func<double, Point> selector)
    {
        var points = new List<Point>();
        for (var index = 0; index <= sampleCount; index++)
        {
            var t = index / (double)sampleCount;
            var x = startX + ((endX - startX) * t);
            points.Add(selector(x));
        }

        return points;
    }

    private static double GetCornerBoundaryY(TouchpadPreviewCornerShape corner, double x)
    {
        var normalized = corner.IsRightTop
            ? (x - corner.Left) / Math.Max(0.0001d, corner.RadiusX)
            : (x - corner.Right) / Math.Max(0.0001d, corner.RadiusX);
        normalized = Math.Clamp(normalized, -1d, 1d);
        var vertical = Math.Sqrt(Math.Max(0d, 1d - (normalized * normalized)));
        return corner.Bottom - (corner.RadiusY * vertical);
    }

    private double GetTopBoundaryY(double x)
    {
        var radius = PadCornerRadius;
        var leftCenterX = PadLeft + radius;
        var rightCenterX = PadLeft + PadWidth - radius;
        var topCenterY = PadTop + radius;

        if (x < leftCenterX)
        {
            var dx = x - leftCenterX;
            return topCenterY - Math.Sqrt(Math.Max(0d, (radius * radius) - (dx * dx)));
        }

        if (x > rightCenterX)
        {
            var dx = x - rightCenterX;
            return topCenterY - Math.Sqrt(Math.Max(0d, (radius * radius) - (dx * dx)));
        }

        return PadTop;
    }

    private double GetBottomBoundaryY(double x)
    {
        var radius = PadCornerRadius;
        var leftCenterX = PadLeft + radius;
        var rightCenterX = PadLeft + PadWidth - radius;
        var bottomCenterY = PadTop + PadHeight - radius;

        if (x < leftCenterX)
        {
            var dx = x - leftCenterX;
            return bottomCenterY + Math.Sqrt(Math.Max(0d, (radius * radius) - (dx * dx)));
        }

        if (x > rightCenterX)
        {
            var dx = x - rightCenterX;
            return bottomCenterY + Math.Sqrt(Math.Max(0d, (radius * radius) - (dx * dx)));
        }

        return PadTop + PadHeight;
    }

    private static double GetPadCornerRadius(double padWidth, double padHeight)
    {
        var radius = Math.Min(padWidth, padHeight) * 0.03;
        return Math.Clamp(radius, 10d, 16d);
    }
}

internal readonly record struct TouchpadPreviewCornerOverlay(
    Point LabelCenter,
    Geometry Geometry,
    Geometry StrokeGeometry);

internal readonly record struct TouchpadPreviewCornerShape(
    bool IsRightTop,
    double Left,
    double Top,
    double Right,
    double Bottom,
    double RadiusX,
    double RadiusY);

internal readonly record struct TouchpadPreviewEdgeOverlay(
    double Left,
    double Top,
    double Width,
    double Height,
    Point LabelCenter,
    Geometry Geometry,
    Geometry StrokeGeometry);
