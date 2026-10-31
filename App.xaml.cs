using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using RadialConvert.Core.Services;
using RadialConvert.UI;
using ImageConverter = RadialConvert.Core.Services.ImageConverter;

namespace RadialConvert;

public partial class App : System.Windows.Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int ATTACH_PARENT_PROCESS = -1;

    private static Mutex? _singleInstanceMutex;
    private TrayIconManager? _trayManager;
    private GlobalInputHook? _inputHook;
    private ExplorerService? _explorerService;
    private ImageConverter? _imageConverter;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Attach to calling PowerShell/CMD console if available
        AttachConsole(ATTACH_PARENT_PROCESS);

        // Ensure single running instance
        const string mutexName = "Global\\OpenTangerine_SingleInstance_Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            Console.WriteLine("\n[Open Tangerine] App is already running in your system tray.");
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

        Console.WriteLine("\n=======================================================");
        Console.WriteLine("  Open Tangerine (Windows Native) is running!");
        Console.WriteLine("  - Desktop batch window opened.");
        Console.WriteLine("  - Hold SHIFT + RIGHT-CLICK on any file in File Explorer");
        Console.WriteLine("    to open the frosted glass conversion wheel.");
        Console.WriteLine("=======================================================\n");

        // Show window by default unless user launched explicitly with --background
        bool startInBackground = e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        if (!startInBackground)
        {
            _mainWindow.Show();
            _mainWindow.Activate();
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
