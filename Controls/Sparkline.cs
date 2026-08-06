using System.Windows;
using System.Windows.Media;
using TwMarketWidget.Models;

namespace TwMarketWidget.Controls;

/// <summary>
/// 當日走勢的迷你折線。自己畫，不用圖表函式庫；資料換新陣列就重繪。
/// 參考價（昨收／前一日結算）畫成一條虛線，線的顏色跟著最後一點在參考價之上或之下。
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(IReadOnlyList<PricePoint>),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BaselineProperty = DependencyProperty.Register(
        nameof(Baseline),
        typeof(decimal?),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SessionStartProperty = DependencyProperty.Register(
        nameof(SessionStart),
        typeof(DateTime?),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SessionEndProperty = DependencyProperty.Register(
        nameof(SessionEnd),
        typeof(DateTime?),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UpBrushProperty = DependencyProperty.Register(
        nameof(UpBrush),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DownBrushProperty = DependencyProperty.Register(
        nameof(DownBrush),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Green, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FlatBrushProperty = DependencyProperty.Register(
        nameof(FlatBrush),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<PricePoint>? Series
    {
        get => (IReadOnlyList<PricePoint>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public decimal? Baseline
    {
        get => (decimal?)GetValue(BaselineProperty);
        set => SetValue(BaselineProperty, value);
    }

    /// <summary>X 軸起點（當日開盤）。與 <see cref="SessionEnd"/> 一起給定時，線就照時間比例往右長。</summary>
    public DateTime? SessionStart
    {
        get => (DateTime?)GetValue(SessionStartProperty);
        set => SetValue(SessionStartProperty, value);
    }

    /// <summary>X 軸終點（當日收盤）。</summary>
    public DateTime? SessionEnd
    {
        get => (DateTime?)GetValue(SessionEndProperty);
        set => SetValue(SessionEndProperty, value);
    }

    public Brush UpBrush
    {
        get => (Brush)GetValue(UpBrushProperty);
        set => SetValue(UpBrushProperty, value);
    }

    public Brush DownBrush
    {
        get => (Brush)GetValue(DownBrushProperty);
        set => SetValue(DownBrushProperty, value);
    }

    public Brush FlatBrush
    {
        get => (Brush)GetValue(FlatBrushProperty);
        set => SetValue(FlatBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var all = Series;
        if (all is null || all.Count < 2)
        {
            return;
        }

        // 有給盤中時段就把 X 軸釘在「開盤～收盤」，線只畫到目前時間為止，右邊留白；
        // 沒給的話退回舊行為：把所有點平均攤在整個寬度上。
        var start = SessionStart;
        var end = SessionEnd;
        var timeScaled = start is { } s && end is { } e && e > s;

        var series = timeScaled
            ? all.Where(p => p.Time >= start!.Value && p.Time <= end!.Value).ToList()
            : all;

        if (series.Count < 2)
        {
            return;
        }

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var point in series)
        {
            var value = (double)point.Price;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        var baseline = Baseline is { } b ? (double)b : double.NaN;
        if (!double.IsNaN(baseline))
        {
            // 把參考價一起納入範圍，線才看得出來在平盤上或下。
            min = Math.Min(min, baseline);
            max = Math.Max(max, baseline);
        }

        var range = max - min;
        if (range <= 0)
        {
            range = Math.Max(Math.Abs(max) * 0.001, 0.01);
            min -= range / 2;
        }

        const double pad = 2;
        var plotHeight = Math.Max(height - pad * 2, 1);
        double Y(double value) => pad + (max - value) / range * plotHeight;

        var last = (double)series[^1].Price;
        var brush = double.IsNaN(baseline) || Math.Abs(last - baseline) < double.Epsilon
            ? FlatBrush
            : last > baseline
                ? UpBrush
                : DownBrush;

        if (!double.IsNaN(baseline))
        {
            var pen = new Pen(FlatBrush, 1)
            {
                DashStyle = new DashStyle(new double[] { 3, 3 }, 0),
            };
            pen.Freeze();
            var y = Y(baseline);
            dc.DrawLine(pen, new Point(0, y), new Point(width, y));
        }

        var span = timeScaled ? (end!.Value - start!.Value).TotalSeconds : 0;
        var step = series.Count > 1 ? width / (series.Count - 1) : width;

        double X(int index) => timeScaled
            ? (series[index].Time - start!.Value).TotalSeconds / span * width
            : index * step;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y((double)series[0].Price)), false, false);

            for (var i = 1; i < series.Count; i++)
            {
                ctx.LineTo(new Point(X(i), Y((double)series[i].Price)), true, false);
            }
        }

        geometry.Freeze();

        var linePen = new Pen(brush, 1.4);
        linePen.Freeze();
        dc.DrawGeometry(null, linePen, geometry);
    }
}
