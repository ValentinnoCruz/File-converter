using System;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using RadialConvert.Core.Services;
using RadialConvert.UI;
using ImageConverter = RadialConvert.Core.Services.ImageConverter;

namespace RadialConvert;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;
    private TrayIconManager? _trayManager;
    private GlobalInputHook? _inputHook;
    private ExplorerService? _explorerService;
    private ImageConverter? _imageConverter;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure single running instance
        const string mutexName = "Global\\QuickConvert_SingleInstance_Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "QuickConvert is already running in your system tray.\nLook for the QuickConvert icon in the bottom right notification area.",
                "QuickConvert Already Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Keep app alive even without active windows
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _explorerService = new ExplorerService();
        _imageConverter = new ImageConverter();

        _trayManager = new TrayIconManager();
        _trayManager.OnRequestExit = () => Shutdown();
        _trayManager.OnRequestTestConversion = HandleManualTest;

        _inputHook = new GlobalInputHook(
            _explorerService,
            _imageConverter,
            onConversionSuccess: (createdPath) =>
            {
                _trayManager.NotifyConversion(createdPath);
            });

        _inputHook.Start();
    }

    private void HandleManualTest()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an Image to Test QuickConvert Wheel",
            Filter = "Image Files (*.webp;*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.webp;*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true && _imageConverter != null)
        {
            string file = dialog.FileName;
            var options = _imageConverter.GetOptions(file);
            if (options.Count > 0)
            {
                // Position near center of screen
                double screenW = SystemParameters.PrimaryScreenWidth;
                double screenH = SystemParameters.PrimaryScreenHeight;
                var testWheel = new RadialMenuWindow(file, options, screenW / 2.0, screenH / 2.0);
                testWheel.Show();
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _inputHook?.Dispose();
        _trayManager?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
