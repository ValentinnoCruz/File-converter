using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
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
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private (double ScaleX, double ScaleY) GetDpiScaleForPoint(double screenX, double screenY)
    {
        try
        {
            var pt = new POINT { X = (int)Math.Round(screenX), Y = (int)Math.Round(screenY) };
            IntPtr hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (hMon != IntPtr.Zero)
            {
                if (GetDpiForMonitor(hMon, 0, out uint dpiX, out uint dpiY) == 0 && dpiX > 0)
                {
                    return (dpiX / 96.0, dpiY / 96.0);
                }
            }
        }
        catch { }

        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            if (dpi.DpiScaleX > 0)
            {
                return (dpi.DpiScaleX, dpi.DpiScaleY);
            }
        }
        catch { }

        try
        {
            uint sysDpi = GetDpiForSystem();
            if (sysDpi > 0)
            {
                return (sysDpi / 96.0, sysDpi / 96.0);
            }
        }
        catch { }

        return (1.0, 1.0);
    }
    private IReadOnlyList<string> _sourceFilePaths;
    private IReadOnlyList<ConversionOption> _options;
    private readonly Point _centerPoint = new Point(135.0, 135.0);
    private double _originScreenX;
    private double _originScreenY;
    private double _scaleX = 1.0;
    private double _scaleY = 1.0;
    private int _hoveredIndex = -1;

    private readonly List<WpfPath> _slicePaths = new();
    private readonly List<TextBlock> _sliceLabels = new();

    private readonly LinearGradientBrush _normalSliceBrush;
    private readonly LinearGradientBrush _activeTangerineBrush;
    private readonly SolidColorBrush _normalStrokeBrush;
    private readonly SolidColorBrush _activeStrokeBrush;
    private readonly SolidColorBrush _normalTextBrush;
    private readonly SolidColorBrush _activeTextBrush;

    public event Action<ConversionOption>? OnOptionSelected;
    public event Action<IReadOnlyList<string>, ConversionOption>? OnFilesDropped;
    public event Action<IReadOnlyList<string>>? OnFilesProvided;

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

        // Pre-create high performance frosted glass frozen brushes
        _normalSliceBrush = new LinearGradientBrush(
            Color.FromArgb(0xD2, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0xB5, 0xBD, 0xD4, 0xE4),
            new Point(0.1, 0.05),
            new Point(0.9, 0.95));
        _normalSliceBrush.GradientStops.Insert(1, new GradientStop(Color.FromArgb(0x90, 0xD5, 0xE7, 0xF2), 0.5));
        _normalSliceBrush.Freeze();

        _activeTangerineBrush = new LinearGradientBrush(
            Color.FromArgb(0xFA, 0xFF, 0x98, 0x47), // Peach Tangerine Highlight
            Color.FromArgb(0xF8, 0xDE, 0x38, 0x00), // Deep Citrus Red-Orange
            new Point(0.1, 0.05),
            new Point(0.9, 0.95));
        _activeTangerineBrush.GradientStops.Insert(1, new GradientStop(Color.FromArgb(0xF5, 0xFF, 0x5E, 0x12), 0.5)); // Electric Tangerine
        _activeTangerineBrush.Freeze();

        _normalStrokeBrush = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
        _normalStrokeBrush.Freeze();

        _activeStrokeBrush = new SolidColorBrush(Colors.White);
        _activeStrokeBrush.Freeze();

        _normalTextBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x31, 0x40));
        _normalTextBrush.Freeze();

        _activeTextBrush = new SolidColorBrush(Colors.White);
        _activeTextBrush.Freeze();

        CenterHub.Cursor = System.Windows.Input.Cursors.Hand;
        CenterHub.MouseLeftButtonDown += (s, e) => { e.Handled = true; Close(); };
        HubPill.Cursor = System.Windows.Input.Cursors.Hand;
        HubPill.MouseLeftButtonDown += (s, e) => { e.Handled = true; Close(); };

        RootGrid.MouseLeftButtonDown += (s, e) =>
        {
            if (e.OriginalSource == RootGrid)
            {
                Close();
            }
        };

        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
            }
        };

        MouseMove += (s, e) =>
        {
            var p = e.GetPosition(this);
            UpdateLocalCursor(p);
        };

        // Wire OLE Drag & Drop events
        AllowDrop = true;
        DragEnter += RadialMenuWindow_DragEnter;
        DragOver += RadialMenuWindow_DragOver;
        DragLeave += RadialMenuWindow_DragLeave;
        Drop += RadialMenuWindow_Drop;

        UpdateTopBadge();
        PositionWindow(screenX, screenY);
        BuildSlices();
    }

    public void SetData(IReadOnlyList<string> sourceFilePaths, IReadOnlyList<ConversionOption> options)
    {
        _sourceFilePaths = sourceFilePaths;
        _options = options;
        _hoveredIndex = -1;
        UpdateTopBadge();
        BuildSlices();
    }

    private void UpdateTopBadge()
    {
        if (_sourceFilePaths == null || _sourceFilePaths.Count == 0)
        {
            FileNameText.Text = "Drag files here";
        }
        else if (_sourceFilePaths.Count == 1)
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

        var (scaleX, scaleY) = GetDpiScaleForPoint(screenX, screenY);
        _scaleX = scaleX;
        _scaleY = scaleY;

        // Convert physical screen coordinates to WPF DIPs
        double dipCursorX = screenX / _scaleX;
        double dipCursorY = screenY / _scaleY;

        // Center the 270x270 wheel (center at 135, 135) exactly over the cursor
        Left = dipCursorX - 135.0;
        Top = dipCursorY - 135.0;

        App.Log($"[RadialWindow] Positioned at physical ({screenX}, {screenY}) -> DIPs ({Left:F1}, {Top:F1}), Scale={_scaleX:F3}x{_scaleY:F3}");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                int physX = (int)Math.Round(_originScreenX - (135.0 * _scaleX));
                int physY = (int)Math.Round(_originScreenY - (135.0 * _scaleY));
                int physWidth = (int)Math.Round(Width * _scaleX);
                int physHeight = (int)Math.Round(Height * _scaleY);

                SetWindowPos(helper.Handle, HWND_TOPMOST, physX, physY, physWidth, physHeight, SWP_SHOWWINDOW);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[RadialWindow] OnSourceInitialized SetWindowPos error: {ex.Message}");
        }
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

            // 1. Slice geometry (Rounded frosted glass sector)
            var geom = RadialGeometryHelper.CreateRoundedGlassSegmentGeometry(_centerPoint, i, count);

            var path = new WpfPath
            {
                Data = geom,
                Fill = _normalSliceBrush,
                Stroke = _normalStrokeBrush,
                StrokeThickness = 1.0,
                Tag = i,
                Cursor = System.Windows.Input.Cursors.Hand,
                AllowDrop = true
            };

            var chosenOption = opt;
            path.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                OnOptionSelected?.Invoke(chosenOption);
            };
            path.MouseRightButtonDown += (s, e) =>
            {
                e.Handled = true;
                OnOptionSelected?.Invoke(chosenOption);
            };

            SlicesContainer.Children.Add(path);
            _slicePaths.Add(path);

            // 2. Slice text label
            Point labelPos = RadialGeometryHelper.GetLabelPosition(_centerPoint, i, count, radius: 80.0);

            var label = new TextBlock
            {
                Text = opt.DisplayName,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = _normalTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            // Measure label to center it on labelPos
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Max(34, label.DesiredSize.Width);
            double h = Math.Max(18, label.DesiredSize.Height);

            Canvas.SetLeft(label, labelPos.X - (w / 2.0));
            Canvas.SetTop(label, labelPos.Y - (h / 2.0));

            SlicesContainer.Children.Add(label);
            _sliceLabels.Add(label);
        }
    }

    private void RadialMenuWindow_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                OnFilesProvided?.Invoke(files);
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void RadialMenuWindow_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            var p = e.GetPosition(this);
            UpdateLocalCursor(p);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void RadialMenuWindow_DragLeave(object sender, DragEventArgs e)
    {
        HighlightHoveredSlice(-1);
    }

    private void RadialMenuWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var opt = GetSelectedOption();
                if (opt != null)
                {
                    OnFilesDropped?.Invoke(files, opt);
                    return;
                }
            }
        }

        // Dropped in center hub or outside format slices -> cancel
        Close();
    }

    public void UpdateLocalCursor(Point localPoint)
    {
        if (_options == null || _options.Count == 0) return;
        int newHoveredIndex = RadialGeometryHelper.GetSectorIndex(localPoint, _centerPoint, _options.Count);
        if (newHoveredIndex != _hoveredIndex)
        {
            _hoveredIndex = newHoveredIndex;
            HighlightHoveredSlice(_hoveredIndex);
        }
    }

    public void UpdateCursor(double screenX, double screenY)
    {
        double dipCursorX = screenX / _scaleX;
        double dipCursorY = screenY / _scaleY;
        double localX = dipCursorX - Left;
        double localY = dipCursorY - Top;
        UpdateLocalCursor(new Point(localX, localY));
    }

    private void HighlightHoveredSlice(int index)
    {
        string countText = (_sourceFilePaths != null && _sourceFilePaths.Count > 1) ? $"{_sourceFilePaths.Count} files" : "file";

        for (int i = 0; i < _slicePaths.Count; i++)
        {
            var path = _slicePaths[i];
            var label = _sliceLabels[i];

            if (i == index)
            {
                // Active Electric Tangerine Styling
                path.Fill = _activeTangerineBrush;
                path.Stroke = _activeStrokeBrush;
                path.StrokeThickness = 1.6;

                label.Foreground = _activeTextBrush;
                label.Effect = (DropShadowEffect)FindResource("TextGlow");
            }
            else
            {
                // Normal Frosted Glass Styling
                path.Fill = _normalSliceBrush;
                path.Stroke = _normalStrokeBrush;
                path.StrokeThickness = 1.0;

                label.Foreground = _normalTextBrush;
                label.Effect = null;
            }
        }

        // Update Hub & Caption
        if (_options != null && index >= 0 && index < _options.Count)
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
            CaptionText.Text = "Drop onto format to convert";
        }
    }

    public ConversionOption? GetSelectedOption()
    {
        if (_options != null && _hoveredIndex >= 0 && _hoveredIndex < _options.Count)
        {
            return _options[_hoveredIndex];
        }
        return null;
    }

    public void ShowConvertingFeedback(string targetExt)
    {
        string countText = (_sourceFilePaths != null && _sourceFilePaths.Count > 1) ? $"{_sourceFilePaths.Count} files" : "file";
        CaptionText.Text = $"Converting {countText} to {targetExt.TrimStart('.').ToUpper()}...";
        HubText.Text = "DONE";
    }
}
