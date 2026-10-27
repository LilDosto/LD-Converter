using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    static RadialWheelControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(RadialWheelControl),
            new FrameworkPropertyMetadata(typeof(RadialWheelControl)));
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
        var glowBrush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(40, 99, 102, 241), 0.3));
        glowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        dc.DrawEllipse(glowBrush, null, new Point(centerX, centerY), OuterRadius + 10, OuterRadius + 10);

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

            Brush fillBrush;
            Pen borderPen;

            if (isHovered)
            {
                // Vurgulu modern gradyan
                var activeBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                activeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(99, 102, 241), 0.0)); // Indigo
                activeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(14, 165, 233), 1.0)); // Sky blue
                fillBrush = activeBrush;
                borderPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 2.0);
            }
            else
            {
                fillBrush = new SolidColorBrush(Color.FromArgb(200, 24, 27, 36));
                borderPen = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1.0);
            }

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
        var typeface = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI, Arial"),
            FontStyles.Normal,
            isHovered ? FontWeights.Bold : FontWeights.SemiBold,
            FontStretches.Normal);

        var ft = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            isHovered ? 14 : 12.5,
            isHovered ? Brushes.White : new SolidColorBrush(Color.FromRgb(220, 225, 235)),
            1.0);

        dc.DrawText(ft, new Point(x - (ft.Width / 2.0), y - (ft.Height / 2.0)));
    }

    private void DrawCenterHub(DrawingContext dc, double centerX, double centerY)
    {
        // Merkez gölge
        var hubShadow = new RadialGradientBrush();
        hubShadow.GradientStops.Add(new GradientStop(Color.FromArgb(160, 0, 0, 0), 0.7));
        hubShadow.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        dc.DrawEllipse(hubShadow, null, new Point(centerX, centerY), InnerRadius + 6, InnerRadius + 6);

        // Merkez arkaplan
        var hubFill = new SolidColorBrush(Color.FromRgb(18, 20, 28));
        var hubPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 99, 102, 241)), 2.0);
        dc.DrawEllipse(hubFill, hubPen, new Point(centerX, centerY), InnerRadius, InnerRadius);

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

        var typefaceBold = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var titleFt = new FormattedText(
            centerTitle,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typefaceBold,
            11.0,
            Brushes.White,
            1.0);

        dc.DrawText(titleFt, new Point(centerX - (titleFt.Width / 2.0), centerY - 13));

        var typefaceRegular = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var subFt = new FormattedText(
            centerSubtitle,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typefaceRegular,
            9.5,
            new SolidColorBrush(Color.FromRgb(156, 163, 175)),
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
        return geom;
    }
}
