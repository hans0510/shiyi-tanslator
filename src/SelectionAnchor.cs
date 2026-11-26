using System;
using System.Collections.Generic;
using System.Windows;

namespace Shiyi
{
    // UI Automation supplies one screen-pixel rectangle per visible line of a selection.
    internal struct SelectionAnchor
    {
        internal readonly Rect Line, Bounds;
        internal readonly PixelPoint End;

        private SelectionAnchor(Rect line, Rect bounds, PixelPoint end)
        {
            Line = line;
            Bounds = bounds;
            End = end;
        }

        internal static SelectionAnchor At(PixelPoint end)
        {
            var point = new Rect(end.X, end.Y, 0, 0);
            return new SelectionAnchor(point, point, end);
        }

        internal static SelectionAnchor FromLines(IEnumerable<Rect> lines, PixelPoint pointer)
        {
            Rect nearest = Rect.Empty, bounds = Rect.Empty;
            double best = double.PositiveInfinity;
            var end = pointer;
            foreach (var line in lines)
            {
                if (line.IsEmpty || !(line.Width > 0 && line.Height > 0) ||
                    double.IsNaN(line.X + line.Y + line.Width + line.Height) ||
                    double.IsInfinity(line.X + line.Y + line.Width + line.Height)) continue;
                bounds.Union(line);
                double x = Clamp(pointer.X, line.Left, line.Right), y = Clamp(pointer.Y, line.Top, line.Bottom);
                double dx = pointer.X - x, dy = pointer.Y - y, distance = dx * dx + dy * dy;
                if (distance >= best) continue;
                best = distance;
                nearest = line;
                end = new PixelPoint((int)Math.Floor(Clamp(x, line.Left, line.Right - 1)),
                    (int)Math.Floor(Clamp(y, line.Top, line.Bottom - 1)));
            }
            return nearest.IsEmpty ? At(pointer) : new SelectionAnchor(nearest, bounds, end);
        }

        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }

        internal PixelPoint Position(Rect work, double width, double height, double gap, bool dot)
        {
            double x, y;
            bool atRight = End.X >= Line.Left + Line.Width / 2;
            double side = atRight ? Line.Right + gap : Line.Left - gap - width;
            if (dot && side >= work.Left && side + width <= work.Right)
            {
                x = side;
                y = Line.Top + (Line.Height - height) / 2;
            }
            else
            {
                x = dot ? End.X - width / 2 : Clamp(End.X - width / 2, Line.Left, Line.Right - width);
                double below = Bounds.Bottom + gap, above = Bounds.Top - gap - height;
                bool nearTop = Line.Top - Bounds.Top < Bounds.Bottom - Line.Bottom;
                bool fitsAbove = above >= work.Top, fitsBelow = below + height <= work.Bottom;
                if (fitsAbove && (nearTop || !fitsBelow)) y = above;
                else if (fitsBelow) y = below;
                else if (Bounds.Right + gap + width <= work.Right || Bounds.Left - gap - width >= work.Left)
                {
                    x = Bounds.Right + gap + width <= work.Right ? Bounds.Right + gap : Bounds.Left - gap - width;
                    y = Line.Top;
                }
                else y = work.Bottom - Line.Bottom >= Line.Top - work.Top ? Line.Bottom + gap : Line.Top - gap - height;
            }
            return new PixelPoint((int)Math.Round(Clamp(x, work.Left, work.Right - width)),
                (int)Math.Round(Clamp(y, work.Top, work.Bottom - height)));
        }
    }
}
