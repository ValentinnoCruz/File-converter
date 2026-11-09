using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RadialConvert.Core.Services;
using RadialConvert.UI;

namespace RadialConvert;

public partial class App : Application
{
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CURSORPOINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORPOINT
    {
        public int X;
        public int Y;
    }

    private Mutex? _singleInstanceMutex;
    private GlobalInputHook? _inputHook;
    private TrayIconManager? _trayManager;
    private ExplorerService? _explorerService;
    private ImageConverter? _imageConverter;
    private MainWindow? _mainWindow;

    private static void Log(string message)
    {
        try
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OTangerine");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "app.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}\r\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"DISPATCHER UNHANDLED EXCEPTION: {args.Exception}");
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"APPDOMAIN UNHANDLED EXCEPTION: {args.ExceptionObject}");
        };

        Log("App.OnStartup began");

        // Attach to calling PowerShell/CMD console if available
        AttachConsole(ATTACH_PARENT_PROCESS);

        try
        {
            // Ensure single running instance
            const string mutexName = "Global\\OTangerine_SingleInstance_Mutex";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            Log($"Mutex created, createdNew={createdNew}");

            if (!createdNew)
            {
                Log("Another instance is already running (createdNew=false). Shutting down.");
                Console.WriteLine("\n[O-Tangerine] App is already running in your system tray.");
                System.Windows.MessageBox.Show(
                    "O-Tangerine is already active in your system tray.\nLook for the orange citrus icon in your notification area (bottom right).",
                    "O-Tangerine Already Active",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Log("Initializing services...");
            _explorerService = new ExplorerService();
            _imageConverter = new ImageConverter();
            _mainWindow = new MainWindow(_imageConverter);

            Log("Initializing TrayManager...");
            _trayManager = new TrayIconManager();
            _trayManager.OnRequestExit = () => Shutdown();
            _trayManager.OnRequestOpenWindow = () =>
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            };

            // Hotkey Ctrl + Shift + X trigger
            _trayManager.OnRequestHotkeyTrigger = () =>
            {
                Log("Hotkey Ctrl+Shift+X triggered");
                GetCursorPos(out var cPt);
                var expPt = new ExplorerService.POINT { X = cPt.X, Y = cPt.Y };
                var files = _explorerService.GetTargetFilePaths(expPt);
                if (files != null && files.Count > 0 && _imageConverter.CanHandleAll(files))
                {
                    var options = _imageConverter.GetOptions(files);
                    if (options != null && options.Count > 0)
                    {
                        var win = new RadialMenuWindow(files, options, cPt.X, cPt.Y);
                        win.OnOptionSelected += (opt) =>
                        {
                            Task.Run(async () =>
                            {
                                var created = await _imageConverter.ConvertBatchAsync(files, opt.Id);
                                _trayManager.NotifyConversions(created);
                            });
                            win.Close();
                        };
                        win.Show();
                        win.Activate();
                        return;
                    }
                }

                // If no file selected, open main window
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            };

            Log("Initializing InputHook...");
            _inputHook = new GlobalInputHook(
                _explorerService,
                _imageConverter,
                onConversionSuccess: (createdPaths) =>
                {
                    _trayManager.NotifyConversions(createdPaths);
                });

            _inputHook.Start();
            Log($"InputHook started successfully, IsRunning={_inputHook.IsRunning}.");

            // Ensure startup shortcut / registry is active
            TrayIconManager.SetStartup(true);

            // Pop balloon tip & chime
            _trayManager.NotifyStartup();

            Console.WriteLine("\n=======================================================");
            Console.WriteLine("  O-Tangerine (Windows Native) is running!");
            Console.WriteLine("  - Desktop batch window opened.");
            Console.WriteLine("  - Drag any file(s) while holding SHIFT, or Shift+Right-Click,");
            Console.WriteLine("    or press Ctrl+Shift+X to open the conversion wheel.");
            Console.WriteLine("=======================================================\n");

            // Show window by default unless user launched explicitly with --background
            bool startInBackground = e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase);
            if (!startInBackground)
            {
                Log("Showing MainWindow...");
                _mainWindow.Show();
                _mainWindow.Activate();
                Log("MainWindow shown.");
            }
        }
        catch (Exception ex)
        {
            Log($"CRITICAL EXCEPTION IN ONSTARTUP: {ex}");
            MessageBox.Show($"O-Tangerine startup error:\n{ex}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"App.OnExit called with ExitCode={e.ApplicationExitCode}. CallStack:\n{Environment.StackTrace}");
        _inputHook?.Dispose();
        _trayManager?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
