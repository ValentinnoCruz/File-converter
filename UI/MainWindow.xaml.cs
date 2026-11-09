using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using RadialConvert.Core.Models;
using RadialConvert.Core.Services;
using WpfPath = System.Windows.Shapes.Path;
using Point = System.Windows.Point;

namespace RadialConvert.UI;

public partial class MainWindow : Window
{
    private readonly ImageConverter _converter;
    private readonly ObservableCollection<string> _fileList = new();
    private static readonly string[] DefaultFormats = { "PNG", "JPEG", "WEBP", "BMP", "ICO", "PDF" };
    private readonly List<string> _standardFormats = new();
    private string? _customOutputFolder = null;
    private int _selectedWheelIndex = 0;

    private readonly List<WpfPath> _embeddedPaths = new();
    private readonly List<TextBlock> _embeddedLabels = new();

    public MainWindow(ImageConverter converter)
    {
        InitializeComponent();
        _converter = converter;

        FilesListBox.ItemsSource = _fileList;
        RefreshAvailableFormats();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Don't close process; hide window to keep background tray and hook active
        e.Cancel = true;
        Hide();
    }

    private void BuildEmbeddedWheel()
    {
        EmbeddedSlices.Children.Clear();
        _embeddedPaths.Clear();
        _embeddedLabels.Clear();

        if (_standardFormats.Count == 0) return;

        var center = new Point(180, 180);
        int count = _standardFormats.Count;

        for (int i = 0; i < count; i++)
        {
            int index = i;
            string format = _standardFormats[i];

            var geom = RadialGeometryHelper.CreateRoundedGlassSegmentGeometry(center, i, count, outerRadius: 165, innerRadius: 68);

            var path = new WpfPath
            {
                Data = geom,
                Fill = (i == _selectedWheelIndex)
                    ? new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0x66, 0x19))
                    : new SolidColorBrush(Color.FromArgb(0xCD, 0xFF, 0xFF, 0xFF)),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = (i == _selectedWheelIndex) ? 2.0 : 1.0,
                Cursor = Cursors.Hand
            };

            path.MouseLeftButtonDown += (s, e) =>
            {
                _selectedWheelIndex = index;
                OperationsCombo.SelectedIndex = index;
                UpdateEmbeddedWheelSelection();
            };

            EmbeddedSlices.Children.Add(path);
            _embeddedPaths.Add(path);

            Point labelPos = RadialGeometryHelper.GetLabelPosition(center, i, count, radius: 116);
            var label = new TextBlock
            {
                Text = format,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (i == _selectedWheelIndex) ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x21, 0x33, 0x40)),
                IsHitTestVisible = false
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, labelPos.X - (label.DesiredSize.Width / 2.0));
            Canvas.SetTop(label, labelPos.Y - (label.DesiredSize.Height / 2.0));

            EmbeddedSlices.Children.Add(label);
            _embeddedLabels.Add(label);
        }

        EmbeddedHubText.Text = _standardFormats[_selectedWheelIndex];
    }

    private void RefreshAvailableFormats()
    {
        var options = _fileList.Count > 0 ? _converter.GetOptions(_fileList.ToList()) : null;

        _standardFormats.Clear();
        if (options != null && options.Count > 0)
        {
            _standardFormats.AddRange(options.Select(o => o.DisplayName));
        }
        else
        {
            _standardFormats.AddRange(DefaultFormats);
        }

        OperationsCombo.ItemsSource = null;
        OperationsCombo.ItemsSource = _standardFormats;
        _selectedWheelIndex = 0;
        OperationsCombo.SelectedIndex = 0;
        BuildEmbeddedWheel();
    }

    private void UpdateEmbeddedWheelSelection()
    {
        for (int i = 0; i < _embeddedPaths.Count; i++)
        {
            bool isSelected = (i == _selectedWheelIndex);
            _embeddedPaths[i].Fill = isSelected
                ? new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0x66, 0x19))
                : new SolidColorBrush(Color.FromArgb(0xCD, 0xFF, 0xFF, 0xFF));
            _embeddedPaths[i].StrokeThickness = isSelected ? 2.0 : 1.0;
            _embeddedLabels[i].Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x21, 0x33, 0x40));
        }

        EmbeddedHubText.Text = _standardFormats[_selectedWheelIndex];
    }

    private void OperationsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OperationsCombo.SelectedIndex >= 0 && OperationsCombo.SelectedIndex < _standardFormats.Count)
        {
            _selectedWheelIndex = OperationsCombo.SelectedIndex;
            UpdateEmbeddedWheelSelection();
        }
    }

    private void DropContainer_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            DropContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x61, 0x0A));
            e.Effects = DragDropEffects.Copy;
        }
    }

    private void DropContainer_DragLeave(object sender, DragEventArgs e)
    {
        DropContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x4A));
    }

    private void DropContainer_Drop(object sender, DragEventArgs e)
    {
        DropContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x4A));
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            AddFiles(files);
        }
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Select files to convert"
        };
        if (dlg.ShowDialog() == true)
        {
            AddFiles(dlg.FileNames);
        }
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path) && !_fileList.Contains(path))
            {
                _fileList.Add(path);
            }
        }

        EmptyPrompt.Visibility = _fileList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilesListBox.Visibility = _fileList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshAvailableFormats();
    }

    private void ClearFiles_Click(object sender, RoutedEventArgs e)
    {
        _fileList.Clear();
        EmptyPrompt.Visibility = Visibility.Visible;
        FilesListBox.Visibility = Visibility.Collapsed;
        RefreshAvailableFormats();
        StatusMessage.Text = "";
    }

    private void OutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose output directory"
        };
        if (dlg.ShowDialog() == true)
        {
            _customOutputFolder = dlg.FolderName;
            OutputFolderButton.Content = System.IO.Path.GetFileName(_customOutputFolder);
        }
    }

    private void TestWheel_Click(object sender, RoutedEventArgs e)
    {
        var sampleFiles = _fileList.Count > 0 ? _fileList.ToList() : new List<string> { "sample.webp" };
        var options = _converter.GetOptions(sampleFiles);
        if (options.Count == 0)
        {
            options = new List<ConversionOption>
            {
                new() { Id = "PNG", DisplayName = "PNG", TargetExtension = ".png" },
                new() { Id = "JPEG", DisplayName = "JPEG", TargetExtension = ".jpg" },
                new() { Id = "WEBP", DisplayName = "WEBP", TargetExtension = ".webp" },
                new() { Id = "PDF", DisplayName = "PDF", TargetExtension = ".pdf" },
                new() { Id = "DOCX", DisplayName = "DOCX", TargetExtension = ".docx" }
            };
        }

        double x = Left + Width / 2.0;
        double y = Top + Height / 2.0;

        var testWindow = new RadialMenuWindow(sampleFiles, options, x, y);
        testWindow.OnOptionSelected += (opt) =>
        {
            MessageBox.Show(
                $"Option chosen: {opt.DisplayName} ({opt.TargetExtension})\n\nIn Explorer, dropping on this format slice creates the converted file instantly!",
                "O-Tangerine Glass Wheel",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            testWindow.Close();
        };
        testWindow.Show();
        testWindow.Activate();
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (_fileList.Count == 0) return;

        string target = _standardFormats[_selectedWheelIndex];
        ConvertButton.IsEnabled = false;
        JobProgressBar.Visibility = Visibility.Visible;
        JobProgressBar.IsIndeterminate = true;
        StatusMessage.Text = "Converting...";

        try
        {
            var created = await _converter.ConvertBatchAsync(_fileList.ToList(), target);
            StatusMessage.Text = $"Done! Converted {created.Count} file(s).";
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Conversion error: {ex.Message}", "O-Tangerine", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage.Text = "Error during conversion.";
        }
        finally
        {
            JobProgressBar.Visibility = Visibility.Collapsed;
            JobProgressBar.IsIndeterminate = false;
            ConvertButton.IsEnabled = true;
        }
    }
}
