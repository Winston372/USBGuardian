using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace USBGuardian.App.UI
{
    /// <summary>
    /// Continuous-corner alternative to WPF's circular CornerRadius.
    /// Rounded corner regions are sampled from a superellipse (exponent 4.4),
    /// transitioning smoothly into horizontal and vertical edges.
    /// Decorative only: preserves WPF Border layout, content and accessibility.
    /// </summary>
    public sealed class ContinuousBorder : Border
    {
        private const double Exponent = 4.4;
        private const int SegmentsPerCorner = 18;

        protected override void OnRender(DrawingContext dc)
        {
            double thickness = Math.Max(0, Math.Max(
                Math.Max(BorderThickness.Left, BorderThickness.Top),
                Math.Max(BorderThickness.Right, BorderThickness.Bottom)));
            var r = new Rect(thickness / 2, thickness / 2,
                Math.Max(0, ActualWidth - thickness), Math.Max(0, ActualHeight - thickness));
            if (r.Width <= 0 || r.Height <= 0) return;
            Geometry g = CreateShape(r, CornerRadius);
            if (Background != null) dc.DrawGeometry(Background, null, g);
            if (BorderBrush != null && thickness > 0)
                dc.DrawGeometry(null, new Pen(BorderBrush, thickness), g);
        }

        private static Geometry CreateShape(Rect box, CornerRadius cr)
        {
            double cap = Math.Min(box.Width, box.Height) / 2;
            double tl = Math.Min(cap, Math.Max(0, cr.TopLeft));
            double tr = Math.Min(cap, Math.Max(0, cr.TopRight));
            double br = Math.Min(cap, Math.Max(0, cr.BottomRight));
            double bl = Math.Min(cap, Math.Max(0, cr.BottomLeft));
            double x = box.X, y = box.Y, w = box.Width, h = box.Height;
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(new Point(x + tl, y), isFilled: true, isClosed: true);
                context.LineTo(new Point(x + w - tr, y), true, false);
                Corner(context, x + w - tr, y + tr, tr, -Math.PI / 2, 0);
                context.LineTo(new Point(x + w, y + h - br), true, false);
                Corner(context, x + w - br, y + h - br, br, 0, Math.PI / 2);
                context.LineTo(new Point(x + bl, y + h), true, false);
                Corner(context, x + bl, y + h - bl, bl, Math.PI / 2, Math.PI);
                context.LineTo(new Point(x, y + tl), true, false);
                Corner(context, x + tl, y + tl, tl, Math.PI, Math.PI * 1.5);
            }
            geometry.Freeze();
            return geometry;
        }
        private static void Corner(StreamGeometryContext context,
            double cx, double cy, double radius, double from, double to)
        {
            if (radius <= 0) return;
            double power = 2 / Exponent;
            for (int i = 1; i <= SegmentsPerCorner; i++)
            {
                double t = from + (to - from) * i / SegmentsPerCorner;
                double cos = Math.Cos(t), sin = Math.Sin(t);
                double xx = cx + radius * Math.Sign(cos) * Math.Pow(Math.Abs(cos), power);
                double yy = cy + radius * Math.Sign(sin) * Math.Pow(Math.Abs(sin), power);
                context.LineTo(new Point(xx, yy), true, false);
            }
        }
    }
}
