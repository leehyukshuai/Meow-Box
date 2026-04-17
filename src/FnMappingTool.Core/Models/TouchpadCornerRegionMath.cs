namespace FnMappingTool.Core.Models;

public readonly record struct TouchpadCornerRegionMetrics(
    string RegionId,
    double Left,
    double Top,
    double Right,
    double Bottom,
    double OriginX,
    double OriginY,
    double RadiusX,
    double RadiusY)
{
    public bool IsRightTop => string.Equals(RegionId, TouchpadCornerRegionId.RightTop, StringComparison.OrdinalIgnoreCase);
}

public static class TouchpadCornerRegionMath
{
    public static TouchpadCornerRegionMetrics Describe(string regionId, TouchpadRegionBoundsConfiguration bounds)
    {
        var left = Math.Min(bounds.Left, bounds.Right);
        var right = Math.Max(bounds.Left, bounds.Right);
        var top = Math.Min(bounds.Top, bounds.Bottom);
        var bottom = Math.Max(bounds.Top, bounds.Bottom);
        var radiusX = Math.Max(1d, right - left);
        var radiusY = Math.Max(1d, bottom - top);
        var isRightTop = string.Equals(regionId, TouchpadCornerRegionId.RightTop, StringComparison.OrdinalIgnoreCase);

        return new TouchpadCornerRegionMetrics(
            regionId,
            left,
            top,
            right,
            bottom,
            isRightTop ? right : left,
            top,
            radiusX,
            radiusY);
    }

    public static bool ContainsPoint(string regionId, TouchpadRegionBoundsConfiguration bounds, double x, double y)
    {
        var region = Describe(regionId, bounds);
        if (x < region.Left || x > region.Right || y < region.Top || y > region.Bottom)
        {
            return false;
        }

        var normalizedX = (x - region.OriginX) / region.RadiusX;
        var normalizedY = (y - region.OriginY) / region.RadiusY;
        return (normalizedX * normalizedX) + (normalizedY * normalizedY) <= 1d;
    }
}
