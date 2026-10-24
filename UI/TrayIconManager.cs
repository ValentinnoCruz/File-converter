using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace RadialConvert.UI;

public class TrayIconManager : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1024;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_INFO = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(
        byte[] pbIconBits,
        uint cbIconBits,
        bool fIcon,
        uint dwVersion,
        int cxDesired,
        int cyDesired,
        uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string AppName = "QuickConvert";
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private HwndSource? _hwndSource;
    private NOTIFYICONDATA _nid;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isAdded = false;

    public Action? OnRequestTestConversion { get; set; }
    public Action? OnRequestExit { get; set; }

    public TrayIconManager()
    {
        CreateMessageWindow();
        CreateAndRegisterIcon();
    }

    private void CreateMessageWindow()
    {
        var parameters = new HwndSourceParameters("QuickConvert_TrayListener")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = IntPtr.Zero,
            Width = 0,
            Height = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
    }

    private void CreateAndRegisterIcon()
    {
        if (_hwndSource == null) return;

        _hIcon = GenerateAppIcon();

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
            hWnd = _hwndSource.Handle,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "QuickConvert (Shift+Right-Click to convert)"
        };

        _isAdded = Shell_NotifyIcon(NIM_ADD, ref _nid);
    }

    private IntPtr GenerateAppIcon()
    {
        // Generate an icon programmatically using WPF DrawingVisual
        int size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Background blue circle
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                null,
                new System.Windows.Point(size / 2.0, size / 2.0),
                size / 2.0 - 1,
                size / 2.0 - 1);

            // Lightning / arrows symbol in white
            var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 2.5);
            dc.DrawLine(pen, new System.Windows.Point(8, 12), new System.Windows.Point(22, 12));
            dc.DrawLine(pen, new System.Windows.Point(18, 8), new System.Windows.Point(22, 12));
            dc.DrawLine(pen, new System.Windows.Point(18, 16), new System.Windows.Point(22, 12));

            dc.DrawLine(pen, new System.Windows.Point(24, 20), new System.Windows.Point(10, 20));
            dc.DrawLine(pen, new System.Windows.Point(14, 16), new System.Windows.Point(10, 20));
            dc.DrawLine(pen, new System.Windows.Point(14, 24), new System.Windows.Point(10, 20));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        byte[] pngBytes = ms.ToArray();

        return CreateIconFromResourceEx(pngBytes, (uint)pngBytes.Length, true, 0x00030000, size, size, 0);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int eventId = lParam.ToInt32();
            if (eventId == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
            else if (eventId == WM_LBUTTONDBLCLK)
            {
                ShowQuickGuide();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        if (_hwndSource == null) return;
        SetForegroundWindow(_hwndSource.Handle);

        var menu = new ContextMenu();

        var header = new MenuItem
        {
            Header = "⚡ QuickConvert (Active)",
            FontWeight = FontWeights.Bold,
            IsEnabled = false
        };
        menu.Items.Add(header);

        var tip = new MenuItem
        {
            Header = "Shift + Right-Click a file in Explorer",
            IsEnabled = false
        };
        menu.Items.Add(tip);

        menu.Items.Add(new Separator());

        var startupItem = new MenuItem
        {
            Header = "Start with Windows",
            IsCheckable = true,
            IsChecked = IsStartupEnabled()
        };
        startupItem.Click += (s, e) =>
        {
            bool newState = !startupItem.IsChecked;
            SetStartup(newState);
            startupItem.IsChecked = newState;
        };
        menu.Items.Add(startupItem);

        var testItem = new MenuItem
        {
            Header = "Test Conversion with File..."
        };
        testItem.Click += (s, e) => OnRequestTestConversion?.Invoke();
        menu.Items.Add(testItem);

        var guideItem = new MenuItem
        {
            Header = "How to Use / Help"
        };
        guideItem.Click += (s, e) => ShowQuickGuide();
        menu.Items.Add(guideItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem
        {
            Header = "Exit QuickConvert"
        };
        exitItem.Click += (s, e) => OnRequestExit?.Invoke();
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
    }

    public void NotifyConversion(string createdFilePath)
    {
        try
        {
            SystemSounds.Asterisk.Play();

            if (_isAdded)
            {
                var fileName = Path.GetFileName(createdFilePath);
                var dir = Path.GetDirectoryName(createdFilePath);

                _nid.uFlags = NIF_INFO;
                _nid.szInfoTitle = "File Converted Successfully";
                _nid.szInfo = $"Created: {fileName}\nIn: {dir}";
                _nid.dwInfoFlags = NIIF_INFO;

                Shell_NotifyIcon(NIM_MODIFY, ref _nid);
            }
        }
        catch { }
    }

    private void ShowQuickGuide()
    {
        MessageBox.Show(
            "QuickConvert is running in the background!\n\n" +
            "How to use:\n" +
            "1. Hold SHIFT and RIGHT-CLICK on any image (e.g. WEBP, PNG, JPG) in File Explorer or Desktop.\n" +
            "2. Keep holding right-click as the radial wheel appears at your cursor.\n" +
            "3. Move your cursor to hover over the desired format (e.g. PNG or JPEG).\n" +
            "4. RELEASE right-click to instantly create a new converted copy in the same directory!\n\n" +
            "To cancel: simply release right-click inside the center circle.",
            "QuickConvert - Quick Guide",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key != null)
            {
                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isAdded)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _isAdded = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        _hwndSource?.Dispose();
        _hwndSource = null;
    }
}
