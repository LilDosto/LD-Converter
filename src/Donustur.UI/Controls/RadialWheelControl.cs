using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Donustur.Core.Math;
using Donustur.Core.Models;

namespace Donustur.UI.Controls;

public sealed class RadialWheelControl : Control
{
    public static readonly DependencyProperty ChoicesProperty =
        DependencyProperty.Register(
            nameof(Choices),
            typeof(IReadOnlyList<ToolChoice>),
            typeof(RadialWheelControl),
            new FrameworkPropertyMetadata(Array.Empty<ToolChoice>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoveredIndexProperty =
        DependencyProperty.Register(
            nameof(HoveredIndex),
            typeof(int),
            typeof(RadialWheelControl),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender, OnHoveredIndexChanged));

    public static readonly DependencyProperty FileCountProperty =
        DependencyProperty.Register(
            nameof(FileCount),
            typeof(int),
            typeof(RadialWheelControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FileSummaryProperty =
        DependencyProperty.Register(
            nameof(FileSummary),
            typeof(string),
            typeof(RadialWheelControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ToolChoice> Choices
    {
        get => (IReadOnlyList<ToolChoice>)GetValue(ChoicesProperty);
        set => SetValue(ChoicesProperty, value);
    }

    public int HoveredIndex
    {
        get => (int)GetValue(HoveredIndexProperty);
        set => SetValue(HoveredIndexProperty, value);
    }

    public int FileCount
    {
        get => (int)GetValue(FileCountProperty);
        set => SetValue(FileCountProperty, value);
    }

    public string FileSummary
    {
        get => (string)GetValue(FileSummaryProperty);
        set => SetValue(FileSummaryProperty, value);
    }

    public event Action<int>? HoverChanged;

    private static void OnHoveredIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RadialWheelControl wheel)
        {
            wheel.HoverChanged?.Invoke((int)e.NewValue);
        }
    }

    public double OuterRadius { get; set; } = 175.0;
    public double InnerRadius { get; set; } = 48.0;

    // --- Performans ve Akışkanlık İçin Dondurulmuş (Frozen) Grafik Nesneleri ---
    private static readonly RadialGradientBrush GlowBrush;
    private static readonly LinearGradientBrush ActiveSegmentBrush;
    private static readonly Pen ActiveSegmentPen;
    private static readonly SolidColorBrush DefaultSegmentBrush;
    private static readonly Pen DefaultSegmentPen;

    private static readonly RadialGradientBrush HubShadowBrush;
    private static readonly SolidColorBrush HubFillBrush;
    private static readonly Pen HubBorderPen;

    private static readonly Typeface SegmentTypefaceRegular;
    private static readonly Typeface SegmentTypefaceBold;
    private static readonly Typeface HubTitleTypeface;
    private static readonly Typeface HubSubtitleTypeface;

    private static readonly SolidColorBrush TextWhiteBrush;
    private static readonly SolidColorBrush TextNormalBrush;
    private static readonly SolidColorBrush TextSubBrush;

    static RadialWheelControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(RadialWheelControl),
            new FrameworkPropertyMetadata(typeof(RadialWheelControl)));

        // Arka plan parlaması (Glow)
        GlowBrush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        GlowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(40, 99, 102, 241), 0.3));
        GlowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        GlowBrush.Freeze();

        // Aktif dilim fırçası ve kalemi
        ActiveSegmentBrush = new LinearGradientBrush(
            Color.FromRgb(99, 102, 241),
            Color.FromRgb(14, 165, 233),
            new Point(0, 0),
            new Point(1, 1));
        ActiveSegmentBrush.Freeze();

        ActiveSegmentPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 2.0);
        ActiveSegmentPen.Brush.Freeze();
        ActiveSegmentPen.Freeze();

        // Varsayılan dilim fırçası ve kalemi
        DefaultSegmentBrush = new SolidColorBrush(Color.FromArgb(200, 24, 27, 36));
        DefaultSegmentBrush.Freeze();

        DefaultSegmentPen = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1.0);
        DefaultSegmentPen.Brush.Freeze();
        DefaultSegmentPen.Freeze();

        // Merkez gösterge (Hub)
        HubShadowBrush = new RadialGradientBrush();
        HubShadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(160, 0, 0, 0), 0.7));
        HubShadowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        HubShadowBrush.Freeze();

        HubFillBrush = new SolidColorBrush(Color.FromRgb(18, 20, 28));
        HubFillBrush.Freeze();

        HubBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 99, 102, 241)), 2.0);
        HubBorderPen.Brush.Freeze();
        HubBorderPen.Freeze();

        // Tipografiler
        SegmentTypefaceRegular = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI, Arial"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        SegmentTypefaceBold = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI, Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        HubTitleTypeface = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        HubSubtitleTypeface = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // Metin fırçaları
        TextWhiteBrush = Brushes.White;
        TextNormalBrush = new SolidColorBrush(Color.FromRgb(220, 225, 235));
        TextNormalBrush.Freeze();
        TextSubBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));
        TextSubBrush.Freeze();
    }

    public RadialWheelControl()
    {
        Width = (OuterRadius * 2) + 20;
        Height = (OuterRadius * 2) + 20;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double centerX = ActualWidth / 2.0;
        double centerY = ActualHeight / 2.0;

        if (centerX <= 0 || centerY <= 0)
        {
            centerX = Width / 2.0;
            centerY = Height / 2.0;
        }

        var choices = Choices;
        int total = choices?.Count ?? 0;
        if (total == 0) return;

        double segmentAngle = 360.0 / total;
        double gapDegrees = total > 1 ? 2.5 : 0.0;

        // Arka plan parlaması (Glow)
        dc.DrawEllipse(GlowBrush, null, new Point(centerX, centerY), OuterRadius + 10, OuterRadius + 10);

        // Dilimleri çiz
        for (int i = 0; i < total; i++)
        {
            bool isHovered = (i == HoveredIndex);
            var choice = choices![i];

            // Açı aralıkları
            double midAngle = WheelMath.GetSegmentCenterAngle(i, total);
            double startAngle = midAngle - ((segmentAngle - gapDegrees) / 2.0);
            double sweepAngle = segmentAngle - gapDegrees;

            // Segment Geometrisi
            var geom = CreateAnnularSectorGeometry(
                centerX, centerY,
                InnerRadius, isHovered ? OuterRadius + 5 : OuterRadius,
                startAngle, sweepAngle);

            Brush fillBrush = isHovered ? ActiveSegmentBrush : DefaultSegmentBrush;
            Pen borderPen = isHovered ? ActiveSegmentPen : DefaultSegmentPen;

            dc.DrawGeometry(fillBrush, borderPen, geom);

            // Metin ve İkon Konumlandırma
            double labelRadius = (InnerRadius + OuterRadius) / 2.0;
            var labelPos = WheelMath.GetPointOnCircle(centerX, centerY, labelRadius, midAngle);

            DrawSegmentLabel(dc, choice.DisplayName, labelPos.X, labelPos.Y, isHovered);
        }

        // Merkez Yuvarlak Gösterge (Center Hub)
        DrawCenterHub(dc, centerX, centerY);
    }

    private void DrawSegmentLabel(DrawingContext dc, string text, double x, double y, bool isHovered)
    {
        var typeface = isHovered ? SegmentTypefaceBold : SegmentTypefaceRegular;
        var brush = isHovered ? TextWhiteBrush : TextNormalBrush;

        var ft = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            isHovered ? 14 : 12.5,
            brush,
            1.0);

        dc.DrawText(ft, new Point(x - (ft.Width / 2.0), y - (ft.Height / 2.0)));
    }

    private void DrawCenterHub(DrawingContext dc, double centerX, double centerY)
    {
        // Merkez gölge
        dc.DrawEllipse(HubShadowBrush, null, new Point(centerX, centerY), InnerRadius + 6, InnerRadius + 6);

        // Merkez arkaplan
        dc.DrawEllipse(HubFillBrush, HubBorderPen, new Point(centerX, centerY), InnerRadius, InnerRadius);

        // Merkez metin
        string centerTitle;
        string centerSubtitle;

        if (HoveredIndex >= 0 && Choices != null && HoveredIndex < Choices.Count)
        {
            var choice = Choices[HoveredIndex];
            centerTitle = choice.DisplayName;
            centerSubtitle = choice.Category;
        }
        else
        {
            centerTitle = "LD Converter";
            centerSubtitle = FileCount > 0 ? $"{FileCount} dosya" : "Bırakın";
        }

        var titleFt = new FormattedText(
            centerTitle,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            HubTitleTypeface,
            11.0,
            TextWhiteBrush,
            1.0);

        dc.DrawText(titleFt, new Point(centerX - (titleFt.Width / 2.0), centerY - 13));

        var subFt = new FormattedText(
            centerSubtitle,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            HubSubtitleTypeface,
            9.5,
            TextSubBrush,
            1.0);

        dc.DrawText(subFt, new Point(centerX - (subFt.Width / 2.0), centerY + 3));
    }

    private static Geometry CreateAnnularSectorGeometry(
        double centerX, double centerY,
        double innerR, double outerR,
        double startAngleDeg, double sweepAngleDeg)
    {
        double endAngleDeg = startAngleDeg + sweepAngleDeg;
        var p1 = WheelMath.GetPointOnCircle(centerX, centerY, innerR, startAngleDeg);
        var p2 = WheelMath.GetPointOnCircle(centerX, centerY, outerR, startAngleDeg);
        var p3 = WheelMath.GetPointOnCircle(centerX, centerY, outerR, endAngleDeg);
        var p4 = WheelMath.GetPointOnCircle(centerX, centerY, innerR, endAngleDeg);

        bool isLargeArc = sweepAngleDeg > 180.0;

        var figure = new PathFigure
        {
            StartPoint = new Point(p1.X, p1.Y),
            IsClosed = true,
            IsFilled = true
        };

        figure.Segments.Add(new LineSegment(new Point(p2.X, p2.Y), isStroked: true));
        figure.Segments.Add(new ArcSegment(new Point(p3.X, p3.Y), new Size(outerR, outerR), 0, isLargeArc, SweepDirection.Clockwise, isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(p4.X, p4.Y), isStroked: true));
        figure.Segments.Add(new ArcSegment(new Point(p1.X, p1.Y), new Size(innerR, innerR), 0, isLargeArc, SweepDirection.Counterclockwise, isStroked: true));

        var geom = new PathGeometry();
        geom.Figures.Add(figure);
        geom.Freeze(); // GPU donanım hızlandırma ve sıfır GC tahsisi için dondur
        return geom;
    }
}
