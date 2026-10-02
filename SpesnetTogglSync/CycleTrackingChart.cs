using System.Drawing.Drawing2D;
using System.Globalization;
using SpesnetTogglSync.Services;

namespace SpesnetTogglSync;

internal sealed class CycleTrackingChart : Control
{
    private static readonly Color TargetColor = Color.FromArgb(70, 110, 150);
    private static readonly Color ActualColor = Color.FromArgb(22, 140, 68);
    private static readonly Color GridColor = Color.FromArgb(226, 230, 234);
    private static readonly Color TodayColor = Color.FromArgb(180, 140, 40);

    private CycleTrackingSnapshot? _snapshot;

    public CycleTrackingChart()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.White;
    }

    public void SetSnapshot(CycleTrackingSnapshot? snapshot)
    {
        _snapshot = snapshot;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackColor);

        if (_snapshot == null || _snapshot.Days.Count == 0)
        {
            TextRenderer.DrawText(
                graphics,
                "Cycle tracking loads from Toggl when you open this tab.",
                Font,
                ClientRectangle,
                SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        const int left = 64;
        const int right = 18;
        const int top = 36;
        const int bottom = 42;
        var plot = new Rectangle(
            left,
            top,
            Math.Max(1, Width - left - right),
            Math.Max(1, Height - top - bottom));

        var maxHours = Math.Max(_snapshot.TargetHoursFull, _snapshot.ActualHours);
        if (maxHours < 8)
        {
            maxHours = 8;
        }

        maxHours *= 1.08m;
        var step = NiceStep(maxHours);
        var axisMax = Math.Ceiling(maxHours / step) * step;
        if (axisMax <= 0)
        {
            axisMax = step;
        }

        using var gridPen = new Pen(GridColor);
        using var axisPen = new Pen(Color.FromArgb(160, 168, 176));
        using var labelFont = new Font(Font.FontFamily, 8f);
        using var legendFont = new Font(Font.FontFamily, 9f);
        var count = _snapshot.Days.Count;

        for (var tick = 0m; tick <= axisMax; tick += step)
        {
            var y = Y(plot, tick, axisMax);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var label = tick.ToString("0", CultureInfo.InvariantCulture) + "h";
            var size = TextRenderer.MeasureText(label, labelFont);
            TextRenderer.DrawText(
                graphics,
                label,
                labelFont,
                new Point(plot.Left - size.Width - 6, (int)y - size.Height / 2),
                Color.FromArgb(80, 88, 96));
        }

        graphics.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        graphics.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        var labeled = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            var date = _snapshot.Days[i].Date;
            var isEdge = i == 0 || i == count - 1;
            var isMonday = date.DayOfWeek == DayOfWeek.Monday;
            if (!isEdge && !isMonday)
            {
                continue;
            }

            if (!labeled.Add(i))
            {
                continue;
            }

            var x = X(plot, i, count);
            var text = date.ToString("d MMM", CultureInfo.InvariantCulture);
            var size = TextRenderer.MeasureText(text, labelFont);
            TextRenderer.DrawText(
                graphics,
                text,
                labelFont,
                new Point((int)x - size.Width / 2, plot.Bottom + 6),
                Color.FromArgb(80, 88, 96));
        }

        var todayIndex = -1;
        for (var i = 0; i < count; i++)
        {
            if (_snapshot.Days[i].Date == _snapshot.Today)
            {
                todayIndex = i;
                break;
            }
        }

        if (todayIndex >= 0)
        {
            var x = X(plot, todayIndex, count);
            using var todayPen = new Pen(TodayColor) { DashStyle = DashStyle.Dash };
            graphics.DrawLine(todayPen, x, plot.Top, x, plot.Bottom);
        }

        DrawSeries(graphics, plot, axisMax, count, TargetColor, 2.2f, markEnd: false, point => point.CumulativeTargetHours);
        DrawSeries(
            graphics,
            plot,
            axisMax,
            count,
            ActualColor,
            2.8f,
            markEnd: true,
            point => point.CumulativeActualHours);

        DrawLegend(graphics, legendFont, plot);
    }

    private void DrawSeries(
        Graphics graphics,
        Rectangle plot,
        decimal axisMax,
        int count,
        Color color,
        float width,
        bool markEnd,
        Func<CycleDayPoint, decimal?> value)
    {
        using var pen = new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        PointF? previous = null;
        PointF? last = null;
        for (var i = 0; i < count; i++)
        {
            var hours = value(_snapshot!.Days[i]);
            if (hours is not decimal yValue)
            {
                previous = null;
                continue;
            }

            var point = new PointF(X(plot, i, count), Y(plot, yValue, axisMax));
            if (previous is PointF from)
            {
                graphics.DrawLine(pen, from, point);
            }

            previous = point;
            last = point;
        }

        if (markEnd && last is PointF end)
        {
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, end.X - 3.5f, end.Y - 3.5f, 7f, 7f);
        }
    }

    private static void DrawLegend(Graphics graphics, Font font, Rectangle plot)
    {
        var x = plot.Left;
        var y = 8;
        DrawSwatch(graphics, TargetColor, ref x, y);
        TextRenderer.DrawText(graphics, "Target", font, new Point(x, y), TargetColor);
        x += TextRenderer.MeasureText("Target", font).Width + 16;
        DrawSwatch(graphics, ActualColor, ref x, y);
        TextRenderer.DrawText(graphics, "Actual", font, new Point(x, y), ActualColor);
        x += TextRenderer.MeasureText("Actual", font).Width + 16;
        DrawSwatch(graphics, TodayColor, ref x, y);
        TextRenderer.DrawText(graphics, "Today", font, new Point(x, y), TodayColor);
    }

    private static void DrawSwatch(Graphics graphics, Color color, ref int x, int y)
    {
        using var pen = new Pen(color, 2.4f);
        graphics.DrawLine(pen, x, y + 8, x + 18, y + 8);
        x += 22;
    }

    private static float X(Rectangle plot, int index, int count)
    {
        if (count <= 1)
        {
            return plot.Left;
        }

        return plot.Left + (plot.Width * index / (float)(count - 1));
    }

    private static float Y(Rectangle plot, decimal hours, decimal axisMax) =>
        plot.Bottom - (float)(hours / axisMax) * plot.Height;

    private static decimal NiceStep(decimal maxHours)
    {
        var rough = maxHours / 5m;
        decimal[] steps = [8, 16, 20, 40, 50, 80, 100];
        foreach (var step in steps)
        {
            if (rough <= step)
            {
                return step;
            }
        }

        return 100;
    }
}
