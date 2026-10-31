using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace RadialConvert.UI;

public static class RadialGeometryHelper
{
    public const double CenterX = 190.0;
    public const double CenterY = 190.0;
    public const double OuterRadius = 174.0;
    public const double InnerRadius = 72.0;

    public static Point GetPointOnCircle(Point center, double radius, double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180.0;
        return new Point(
            center.X + radius * Math.Cos(rad),
            center.Y + radius * Math.Sin(rad));
    }

    public static PathGeometry CreateRoundedGlassSegmentGeometry(
        Point center,
        int index,
        int count,
        double outerRadius = OuterRadius,
        double innerRadius = InnerRadius)
    {
        var geom = new PathGeometry();
        if (count <= 0) return geom;

        if (count == 1)
        {
            // Full doughnut ring
            var fig = new PathFigure
            {
                StartPoint = new Point(center.X + outerRadius, center.Y),
                IsClosed = true
            };
            fig.Segments.Add(new ArcSegment(new Point(center.X - outerRadius, center.Y), new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new ArcSegment(new Point(center.X + outerRadius, center.Y), new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true));

            var innerFig = new PathFigure
            {
                StartPoint = new Point(center.X + innerRadius, center.Y),
                IsClosed = true
            };
            innerFig.Segments.Add(new ArcSegment(new Point(center.X - innerRadius, center.Y), new Size(innerRadius, innerRadius), 0, false, SweepDirection.Counterclockwise, true));
            innerFig.Segments.Add(new ArcSegment(new Point(center.X + innerRadius, center.Y), new Size(innerRadius, innerRadius), 0, false, SweepDirection.Counterclockwise, true));

            geom.Figures.Add(fig);
            geom.Figures.Add(innerFig);
            geom.FillRule = FillRule.EvenOdd;
            return geom;
        }

        double step = 360.0 / count;
        // Centers sector i at (index * step - 90 deg) like Open Tangerine
        double midAngle = index * step - 90.0;
        double gapDeg = count >= 6 ? 2.5 : 2.0;
        double startAngle = midAngle - (step / 2.0) + gapDeg;
        double endAngle = midAngle + (step / 2.0) - gapDeg;

        double rounding = Math.Min(5.0, step / 8.0);
        double midInnerRadius = innerRadius + 13.0; // corner curve transition
        double midOuterRadius = outerRadius - 13.0;

        Point p1 = GetPointOnCircle(center, outerRadius, startAngle + rounding);
        Point p2 = GetPointOnCircle(center, outerRadius, endAngle - rounding);
        Point cornerOuterEnd = GetPointOnCircle(center, outerRadius, endAngle);
        Point radialOuterEnd = GetPointOnCircle(center, midOuterRadius, endAngle);
        Point radialInnerEnd = GetPointOnCircle(center, midInnerRadius, endAngle);
        Point cornerInnerEnd = GetPointOnCircle(center, innerRadius, endAngle);
        Point p3 = GetPointOnCircle(center, innerRadius, endAngle - (rounding * 1.7));
        Point p4 = GetPointOnCircle(center, innerRadius, startAngle + (rounding * 1.7));
        Point cornerInnerStart = GetPointOnCircle(center, innerRadius, startAngle);
        Point radialInnerStart = GetPointOnCircle(center, midInnerRadius, startAngle);
        Point radialOuterStart = GetPointOnCircle(center, midOuterRadius, startAngle);
        Point cornerOuterStart = GetPointOnCircle(center, outerRadius, startAngle);

        var figure = new PathFigure
        {
            StartPoint = p1,
            IsClosed = true,
            IsFilled = true
        };

        // Outer arc from p1 to p2
        bool isLargeOuter = (endAngle - startAngle - 2 * rounding) > 180.0;
        figure.Segments.Add(new ArcSegment(p2, new Size(outerRadius, outerRadius), 0, isLargeOuter, SweepDirection.Clockwise, true));

        // Rounded corner at outer end to radial line
        figure.Segments.Add(new QuadraticBezierSegment(cornerOuterEnd, radialOuterEnd, true));

        // Radial line down towards inner radius
        figure.Segments.Add(new LineSegment(radialInnerEnd, true));

        // Rounded corner into inner arc
        figure.Segments.Add(new QuadraticBezierSegment(cornerInnerEnd, p3, true));

        // Inner arc back from p3 to p4
        bool isLargeInner = (endAngle - startAngle - 3.4 * rounding) > 180.0;
        figure.Segments.Add(new ArcSegment(p4, new Size(innerRadius, innerRadius), 0, isLargeInner, SweepDirection.Counterclockwise, true));

        // Rounded corner into radial line
        figure.Segments.Add(new QuadraticBezierSegment(cornerInnerStart, radialInnerStart, true));

        // Radial line up towards outer radius
        figure.Segments.Add(new LineSegment(radialOuterStart, true));

        // Rounded corner back to p1
        figure.Segments.Add(new QuadraticBezierSegment(cornerOuterStart, p1, true));

        geom.Figures.Add(figure);
        return geom;
    }

    public static Point GetLabelPosition(Point center, int index, int count, double radius = 123.0)
    {
        double step = 360.0 / count;
        double midAngle = index * step - 90.0;
        return GetPointOnCircle(center, radius, midAngle);
    }

    public static int GetSectorIndex(Point mousePoint, Point center, int count, double minRadius = InnerRadius, double maxRadius = OuterRadius + 2.0)
    {
        if (count <= 0) return -1;

        double dx = mousePoint.X - center.X;
        double dy = mousePoint.Y - center.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < minRadius || dist > maxRadius)
        {
            return -1; // Center hub or outside wheel
        }

        double step = 360.0 / count;
        // In Open Tangerine:
        // ((math.degrees(math.atan2(dy, dx)) + 90 + 180/count) % 360) / (360/count)
        double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double normalized = (deg + 90.0 + (step / 2.0)) % 360.0;
        if (normalized < 0) normalized += 360.0;

        int index = (int)(normalized / step);
        if (index >= count) index = count - 1;
        return index;
    }
}
