using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using RadialConvert.Core.Models;
using WpfPath = System.Windows.Shapes.Path;

namespace RadialConvert.UI;

public partial class RadialMenuWindow : Window
{
    private readonly string _sourceFilePath;
    private readonly IReadOnlyList<ConversionOption> _options;
    private readonly double _centerCoord = 180.0;
    private readonly double _innerRadius = 48.0;
    private readonly double _outerRadius = 140.0;
    private double _originScreenX;
    private double _originScreenY;
    private int _hoveredIndex = -1;

    private readonly List<WpfPath> _slicePaths = new();
    private readonly List<Border> _sliceLabels = new();

    public RadialMenuWindow(string sourceFilePath, IReadOnlyList<ConversionOption> options, double screenX, double screenY)
    {
        InitializeComponent();

        _sourceFilePath = sourceFilePath;
        _options = options;
        _originScreenX = screenX;
        _originScreenY = screenY;

        FileNameText.Text = System.IO.Path.GetFileName(sourceFilePath);

        PositionWindow(screenX, screenY);
        BuildSlices();
    }

    public void PositionWindow(double screenX, double screenY)
    {
        _originScreenX = screenX;
        _originScreenY = screenY;

        // Position window centered at mouse point
        Left = screenX - (Width / 2.0);
        Top = screenY - (Height / 2.0);
    }

    private void BuildSlices()
    {
        SlicesCanvas.Children.Clear();
        _slicePaths.Clear();
        _sliceLabels.Clear();

        if (_options == null || _options.Count == 0) return;

        int count = _options.Count;
        double sliceSpan = 360.0 / count;
        var centerPoint = new Point(_centerCoord, _centerCoord);

        for (int i = 0; i < count; i++)
        {
            var opt = _options[i];
            double startAngle = i * sliceSpan;
            double endAngle = (i + 1) * sliceSpan;

            // Geometry for slice
            var geom = RadialGeometryHelper.CreatePieSliceGeometry(
                centerPoint,
                _innerRadius,
                _outerRadius,
                startAngle,
                endAngle);

            var path = new WpfPath
            {
                Data = geom,
                Fill = new SolidColorBrush(Color.FromArgb(0xEA, 0x1E, 0x1E, 0x28)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0x55, 0x55, 0x6E)),
                StrokeThickness = 1.5,
                Tag = i
            };

            SlicesCanvas.Children.Add(path);
            _slicePaths.Add(path);

            // Label container positioned inside slice
            double midRadius = (_innerRadius + _outerRadius) / 2.0;
            Point labelPos = RadialGeometryHelper.GetLabelPosition(centerPoint, midRadius, startAngle, endAngle);

            var labelBorder = CreateSliceLabel(opt);
            Canvas.SetLeft(labelBorder, labelPos.X - 45);
            Canvas.SetTop(labelBorder, labelPos.Y - 24);

            SlicesCanvas.Children.Add(labelBorder);
            _sliceLabels.Add(labelBorder);
        }
    }

    private Border CreateSliceLabel(ConversionOption opt)
    {
        var border = new Border
        {
            Width = 90,
            Height = 48,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var sp = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = opt.DisplayName,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var sub = new TextBlock
        {
            Text = opt.Description,
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 86
        };

        sp.Children.Add(title);
        sp.Children.Add(sub);
        border.Child = sp;

        return border;
    }

    public void UpdateCursor(double screenX, double screenY)
    {
        double dx = screenX - _originScreenX;
        double dy = screenY - _originScreenY;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        int newHoveredIndex = -1;

        if (dist > _innerRadius && _options.Count > 0)
        {
            // Angle from 12 o'clock (0 to 360 deg clockwise)
            double angle = (Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90.0;
            if (angle < 0) angle += 360.0;

            double sliceSpan = 360.0 / _options.Count;
            newHoveredIndex = (int)(angle / sliceSpan);
            if (newHoveredIndex >= _options.Count) newHoveredIndex = _options.Count - 1;
        }

        if (newHoveredIndex != _hoveredIndex)
        {
            _hoveredIndex = newHoveredIndex;
            HighlightHoveredSlice(_hoveredIndex);
        }
    }

    private void HighlightHoveredSlice(int index)
    {
        for (int i = 0; i < _slicePaths.Count; i++)
        {
            var path = _slicePaths[i];
            var label = _sliceLabels[i];
            var opt = _options[i];

            if (i == index)
            {
                // Hovered styling
                Color accentColor = (Color)ColorConverter.ConvertFromString(opt.AccentColor);
                path.Fill = new SolidColorBrush(Color.FromArgb(0xDF, accentColor.R, accentColor.G, accentColor.B));
                path.Stroke = Brushes.White;
                path.StrokeThickness = 2.5;

                if (label.Child is StackPanel sp && sp.Children.Count >= 2)
                {
                    if (sp.Children[0] is TextBlock title) title.Foreground = Brushes.White;
                    if (sp.Children[1] is TextBlock sub) sub.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xFF));
                }
            }
            else
            {
                // Normal styling
                path.Fill = new SolidColorBrush(Color.FromArgb(0xEA, 0x1E, 0x1E, 0x28));
                path.Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0x55, 0x55, 0x6E));
                path.StrokeThickness = 1.5;

                if (label.Child is StackPanel sp && sp.Children.Count >= 2)
                {
                    if (sp.Children[0] is TextBlock title) title.Foreground = Brushes.White;
                    if (sp.Children[1] is TextBlock sub) sub.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
                }
            }
        }

        // Center cancel hub feedback
        if (index == -1)
        {
            CenterEllipse.Fill = new SolidColorBrush(Color.FromRgb(0x32, 0x20, 0x26)); // Subtle red tint
            CenterEllipse.Stroke = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)); // Red highlight
            CenterIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            CenterText.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        }
        else
        {
            CenterEllipse.Fill = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x24));
            CenterEllipse.Stroke = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x4A));
            CenterIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            CenterText.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
        }
    }

    public ConversionOption? GetSelectedOption()
    {
        if (_hoveredIndex >= 0 && _hoveredIndex < _options.Count)
        {
            return _options[_hoveredIndex];
        }
        return null;
    }

    public void ShowConvertingFeedback(string targetExt)
    {
        StatusText.Text = $"Converting to {targetExt.TrimStart('.').ToUpper()}...";
        StatusBadge.Visibility = Visibility.Visible;
    }
}
