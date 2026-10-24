using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Point = System.Windows.Point;

namespace RadialConvert.UI;

public static class RadialGeometryHelper
{
    public static PathGeometry CreatePieSliceGeometry(
        Point center,
        double innerRadius,
        double outerRadius,
        double startAngleDeg,
        double endAngleDeg)
    {
        var geom = new PathGeometry();
        var figure = new PathFigure();

        double startRad = (startAngleDeg - 90) * Math.PI / 180.0;
        double endRad = (endAngleDeg - 90) * Math.PI / 180.0;
        double angleDiff = endAngleDeg - startAngleDeg;
        bool isLargeArc = angleDiff > 180.0;

        Point pOuterStart = new Point(
            center.X + outerRadius * Math.Cos(startRad),
            center.Y + outerRadius * Math.Sin(startRad));

        Point pOuterEnd = new Point(
            center.X + outerRadius * Math.Cos(endRad),
            center.Y + outerRadius * Math.Sin(endRad));

        Point pInnerEnd = new Point(
            center.X + innerRadius * Math.Cos(endRad),
            center.Y + innerRadius * Math.Sin(endRad));

        Point pInnerStart = new Point(
            center.X + innerRadius * Math.Cos(startRad),
            center.Y + innerRadius * Math.Sin(startRad));

        figure.StartPoint = pOuterStart;

        // Outer arc
        figure.Segments.Add(new ArcSegment(
            pOuterEnd,
            new Size(outerRadius, outerRadius),
            0,
            isLargeArc,
            SweepDirection.Clockwise,
            true));

        // Line to inner end
        figure.Segments.Add(new LineSegment(pInnerEnd, true));

        // Inner arc back
        figure.Segments.Add(new ArcSegment(
            pInnerStart,
            new Size(innerRadius, innerRadius),
            0,
            isLargeArc,
            SweepDirection.Counterclockwise,
            true));

        figure.IsClosed = true;
        geom.Figures.Add(figure);
        return geom;
    }

    public static Point GetLabelPosition(
        Point center,
        double radius,
        double startAngleDeg,
        double endAngleDeg)
    {
        double midAngleDeg = (startAngleDeg + endAngleDeg) / 2.0;
        double midRad = (midAngleDeg - 90) * Math.PI / 180.0;

        return new Point(
            center.X + radius * Math.Cos(midRad),
            center.Y + radius * Math.Sin(midRad));
    }
}
