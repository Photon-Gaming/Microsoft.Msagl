using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Point = Microsoft.Msagl.Core.Geometry.Point;

namespace Microsoft.Msagl.AvaloniaGraphControl {
    internal class Common {
        internal static Avalonia.Point AvaloniaPoint(Point p) {
            return new Avalonia.Point(p.X, p.Y);
        }

        internal static Point MsaglPoint(Avalonia.Point p) {
            return new Point(p.X, p.Y);
        }


        public static Brush BrushFromMsaglColor(Microsoft.Msagl.Drawing.Color color) {
            Color avalonColor = new(color.A, color.R, color.G, color.B);
            return new SolidColorBrush(avalonColor);
        }

        public static Brush BrushFromMsaglColor(byte colorA, byte colorR, byte colorG, byte colorB) {
            Color avalonColor = new(colorA, colorR, colorG, colorB);
            return new SolidColorBrush(avalonColor);
        }




        internal static void PositionControl(Control frameworkElement, Point center, double scale) {
            PositionControl(frameworkElement, center.X, center.Y, scale);
        }

        static void PositionControl(Control frameworkElement, double x, double y, double scale) {
            if (frameworkElement == null)
                return;
            frameworkElement.RenderTransformOrigin = RelativePoint.TopLeft;
            frameworkElement.RenderTransform =
                new MatrixTransform(new Matrix(scale, 0, 0, -scale, x - scale*frameworkElement.Width/2,
                    y + scale*frameworkElement.Height/2));
        }

    }
}
