using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure single running instance
        const string mutexName = "Global\\OpenTangerine_SingleInstance_Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Open Tangerine is already active in your system tray.\nLook for the orange citrus icon in your notification area (bottom right).",
                "Open Tangerine Already Active",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _explorerService = new ExplorerService();
        _imageConverter = new ImageConverter();
        _mainWindow = new MainWindow(_imageConverter);

        _trayManager = new TrayIconManager();
        _trayManager.OnRequestExit = () => Shutdown();
        _trayManager.OnRequestOpenWindow = () =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        };

        _inputHook = new GlobalInputHook(
            _explorerService,
            _imageConverter,
            onConversionSuccess: (createdPaths) =>
            {
                _trayManager.NotifyConversions(createdPaths);
            });

        _inputHook.Start();

        // If user passed --window argument, show desktop window immediately
        if (e.Args.Contains("--window", StringComparer.OrdinalIgnoreCase))
        {
            _mainWindow.Show();
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
