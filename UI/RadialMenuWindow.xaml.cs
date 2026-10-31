using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using RadialConvert.Core.Models;
using WpfPath = System.Windows.Shapes.Path;
using Point = System.Windows.Point;

namespace RadialConvert.UI;

public partial class RadialMenuWindow : Window
{
    private readonly IReadOnlyList<string> _sourceFilePaths;
    private readonly IReadOnlyList<ConversionOption> _options;
    private readonly Point _centerPoint = new Point(190.0, 190.0);
    private double _originScreenX;
    private double _originScreenY;
    private int _hoveredIndex = -1;

    private readonly List<WpfPath> _slicePaths = new();
    private readonly List<TextBlock> _sliceLabels = new();

    private readonly LinearGradientBrush _normalSliceBrush;
    private readonly LinearGradientBrush _activeTangerineBrush;
    private readonly SolidColorBrush _normalStrokeBrush;
    private readonly SolidColorBrush _activeStrokeBrush;
    private readonly SolidColorBrush _normalTextBrush;
    private readonly SolidColorBrush _activeTextBrush;

    public RadialMenuWindow(
        IReadOnlyList<string> sourceFilePaths,
        IReadOnlyList<ConversionOption> options,
        double screenX,
        double screenY)
    {
        InitializeComponent();

        _sourceFilePaths = sourceFilePaths;
        _options = options;
        _originScreenX = screenX;
        _originScreenY = screenY;

        // Pre-create high performance frozen brushes
        _normalSliceBrush = new LinearGradientBrush(
            Color.FromArgb(0xCD, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0xC8, 0xB7, 0xCE, 0xDC),
            new Point(0.1, 0.05),
            new Point(0.9, 0.95));
        _normalSliceBrush.GradientStops.Insert(1, new GradientStop(Color.FromArgb(0xB0, 0xE0, 0xED, 0xF3), 0.5));
        _normalSliceBrush.Freeze();

        _activeTangerineBrush = new LinearGradientBrush(
            Color.FromArgb(0xF8, 0xFF, 0xB3, 0x6B), // Peach Orange
            Color.FromArgb(0xF8, 0xEB, 0x4F, 0x0C), // Deep Red-Citrus
            new Point(0.1, 0.05),
            new Point(0.9, 0.95));
        _activeTangerineBrush.GradientStops.Insert(1, new GradientStop(Color.FromArgb(0xF0, 0xFF, 0x66, 0x19), 0.5)); // Electric Tangerine
        _activeTangerineBrush.Freeze();

        _normalStrokeBrush = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
        _normalStrokeBrush.Freeze();

        _activeStrokeBrush = new SolidColorBrush(Colors.White);
        _activeStrokeBrush.Freeze();

        _normalTextBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x33, 0x40));
        _normalTextBrush.Freeze();

        _activeTextBrush = new SolidColorBrush(Colors.White);
        _activeTextBrush.Freeze();

        UpdateTopBadge();
        PositionWindow(screenX, screenY);
        BuildSlices();
    }

    private void UpdateTopBadge()
    {
        if (_sourceFilePaths.Count == 1)
        {
            FileNameText.Text = System.IO.Path.GetFileName(_sourceFilePaths[0]);
        }
        else
        {
            FileNameText.Text = $"{_sourceFilePaths.Count} files selected";
        }
    }

    public void PositionWindow(double screenX, double screenY)
    {
        _originScreenX = screenX;
        _originScreenY = screenY;

        Left = screenX - 190.0;
        Top = screenY - 190.0;
    }

    private void BuildSlices()
    {
        SlicesContainer.Children.Clear();
        _slicePaths.Clear();
        _sliceLabels.Clear();

        if (_options == null || _options.Count == 0) return;

        int count = _options.Count;

        for (int i = 0; i < count; i++)
        {
            var opt = _options[i];

            // 1. Slice geometry (Tangerine rounded glass sector)
            var geom = RadialGeometryHelper.CreateRoundedGlassSegmentGeometry(_centerPoint, i, count);

            var path = new WpfPath
            {
                Data = geom,
                Fill = _normalSliceBrush,
                Stroke = _normalStrokeBrush,
                StrokeThickness = 1.1,
                Tag = i
            };

            SlicesContainer.Children.Add(path);
            _slicePaths.Add(path);

            // 2. Slice text label
            Point labelPos = RadialGeometryHelper.GetLabelPosition(_centerPoint, i, count, radius: 123.0);

            var label = new TextBlock
            {
                Text = opt.DisplayName,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = _normalTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            // Measure label to center it on labelPos
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Max(40, label.DesiredSize.Width);
            double h = Math.Max(20, label.DesiredSize.Height);

            Canvas.SetLeft(label, labelPos.X - (w / 2.0));
            Canvas.SetTop(label, labelPos.Y - (h / 2.0));

            SlicesContainer.Children.Add(label);
            _sliceLabels.Add(label);
        }
    }

    public void UpdateCursor(double screenX, double screenY)
    {
        double localX = screenX - Left;
        double localY = screenY - Top;
        var localPoint = new Point(localX, localY);

        int newHoveredIndex = RadialGeometryHelper.GetSectorIndex(localPoint, _centerPoint, _options.Count);

        if (newHoveredIndex != _hoveredIndex)
        {
            _hoveredIndex = newHoveredIndex;
            HighlightHoveredSlice(_hoveredIndex);
        }
    }

    private void HighlightHoveredSlice(int index)
    {
        string countText = _sourceFilePaths.Count > 1 ? $"{_sourceFilePaths.Count} files" : "file";

        for (int i = 0; i < _slicePaths.Count; i++)
        {
            var path = _slicePaths[i];
            var label = _sliceLabels[i];

            if (i == index)
            {
                // Active Tangerine Styling
                path.Fill = _activeTangerineBrush;
                path.Stroke = _activeStrokeBrush;
                path.StrokeThickness = 1.8;

                label.Foreground = _activeTextBrush;
                label.Effect = (DropShadowEffect)FindResource("TextGlow");
            }
            else
            {
                // Normal Glass Styling
                path.Fill = _normalSliceBrush;
                path.Stroke = _normalStrokeBrush;
                path.StrokeThickness = 1.1;

                label.Foreground = _normalTextBrush;
                label.Effect = null;
            }
        }

        // Update Hub & Caption
        if (index >= 0 && index < _options.Count)
        {
            var opt = _options[index];
            HubText.Text = opt.DisplayName;
            HubText.Foreground = new SolidColorBrush(Color.FromRgb(0xEB, 0x4F, 0x0C));
            CaptionText.Text = $"Convert {countText} to {opt.DisplayName}";
        }
        else
        {
            HubText.Text = "DROP";
            HubText.Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x3D, 0x4A));
            CaptionText.Text = "Release in center to cancel";
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
        string countText = _sourceFilePaths.Count > 1 ? $"{_sourceFilePaths.Count} files" : "file";
        CaptionText.Text = $"Converting {countText} to {targetExt.TrimStart('.').ToUpper()}...";
        HubText.Text = "DONE";
    }
}
