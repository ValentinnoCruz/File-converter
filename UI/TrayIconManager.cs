using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace RadialConvert.UI;

public class TrayIconManager : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 100;
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9001;

    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_X = 0x58;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

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
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(byte[] pbIconBits, uint cbIconBits, bool fIcon, uint dwVersion, int cxDesired, int cyDesired, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const string AppName = "OTangerine";
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private HwndSource? _hwndSource;
    private NOTIFYICONDATA _nid;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isAdded = false;

    public Action? OnRequestOpenWindow { get; set; }
    public Action? OnRequestExit { get; set; }
    public Action? OnRequestHotkeyTrigger { get; set; }

    public TrayIconManager()
    {
        CreateMessageWindow();
        CreateAndRegisterIcon();
    }

    private void CreateMessageWindow()
    {
        var parameters = new HwndSourceParameters("OTangerine_TrayListener")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = IntPtr.Zero,
            Width = 0,
            Height = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        // Register Global Hotkey: Ctrl + Shift + X
        RegisterHotKey(_hwndSource.Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_X);
    }

    private void CreateAndRegisterIcon()
    {
        if (_hwndSource == null) return;

        _hIcon = GenerateTangerineIcon();

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
            hWnd = _hwndSource.Handle,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "O-Tangerine (Hold Shift & Drag, Shift+Right-Click, or Ctrl+Shift+X)"
        };

        _isAdded = Shell_NotifyIcon(NIM_ADD, ref _nid);
    }

    public void NotifyStartup()
    {
        try
        {
            SystemSounds.Asterisk.Play();
            if (_isAdded)
            {
                _nid.uFlags = NIF_INFO | NIF_TIP;
                _nid.szInfoTitle = "🍊 O-Tangerine is Running!";
                _nid.szInfo = "Drag files + hold Shift, or Shift+Right-Click, or press Ctrl+Shift+X to convert.";
                _nid.dwInfoFlags = (int)NIIF_INFO;
                Shell_NotifyIcon(NIM_MODIFY, ref _nid);
            }
        }
        catch { }
    }

    private IntPtr GenerateTangerineIcon()
    {
        int size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Citrus Tangerine Orange base circle
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(255, 102, 25)),
                null,
                new Point(size / 2.0, size / 2.0),
                size / 2.0 - 1.5,
                size / 2.0 - 1.5);

            // Leaf / stem accent in green
            var leafBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            dc.DrawEllipse(leafBrush, null, new Point(22, 9), 4, 2.5);

            // White wheel / conversion symbol
            var pen = new Pen(Brushes.White, 2.4);
            dc.DrawLine(pen, new Point(9, 13), new Point(23, 13));
            dc.DrawLine(pen, new Point(19, 9), new Point(23, 13));
            dc.DrawLine(pen, new Point(19, 17), new Point(23, 13));

            dc.DrawLine(pen, new Point(23, 21), new Point(9, 21));
            dc.DrawLine(pen, new Point(13, 17), new Point(9, 21));
            dc.DrawLine(pen, new Point(13, 25), new Point(9, 21));
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
            else if (eventId == WM_LBUTTONUP || eventId == WM_LBUTTONDBLCLK)
            {
                OnRequestOpenWindow?.Invoke();
                handled = true;
            }
        }
        else if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            OnRequestHotkeyTrigger?.Invoke();
            handled = true;
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
            Header = "🍊 O-Tangerine (Active)",
            FontWeight = FontWeights.Bold,
            IsEnabled = false
        };
        menu.Items.Add(header);

        var openWindowItem = new MenuItem
        {
            Header = "Open O-Tangerine Window...",
            FontWeight = FontWeights.SemiBold
        };
        openWindowItem.Click += (s, e) => OnRequestOpenWindow?.Invoke();
        menu.Items.Add(openWindowItem);

        var tip = new MenuItem
        {
            Header = "Tip: Hold Shift while dragging, or Ctrl+Shift+X",
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

        var guideItem = new MenuItem
        {
            Header = "How to Use / Help"
        };
        guideItem.Click += (s, e) => ShowQuickGuide();
        menu.Items.Add(guideItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem
        {
            Header = "Exit O-Tangerine"
        };
        exitItem.Click += (s, e) => OnRequestExit?.Invoke();
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
    }

    public void NotifyConversions(IReadOnlyList<string> createdFilePaths)
    {
        try
        {
            SystemSounds.Asterisk.Play();

            if (_isAdded && createdFilePaths.Count > 0)
            {
                string title = createdFilePaths.Count == 1 ? "File Converted" : $"{createdFilePaths.Count} Files Converted";
                string details = createdFilePaths.Count == 1
                    ? $"Created: {Path.GetFileName(createdFilePaths[0])}"
                    : $"Created {createdFilePaths.Count} files in {Path.GetDirectoryName(createdFilePaths[0])}";

                _nid.uFlags = NIF_INFO;
                _nid.szInfoTitle = title;
                _nid.szInfo = details;
                _nid.dwInfoFlags = (int)NIIF_INFO;

                Shell_NotifyIcon(NIM_MODIFY, ref _nid);
            }
        }
        catch { }
    }

    private void ShowQuickGuide()
    {
        MessageBox.Show(
            "O-Tangerine is active in the background!\n\n" +
            "How to convert:\n" +
            "Method 1 (Drag & Drop - Recommended):\n" +
            "1. Drag any file(s) in File Explorer or Desktop while holding SHIFT.\n" +
            "2. The compact frosted glass wheel pops up around your cursor.\n" +
            "3. Drop the file(s) onto your target format slice (PNG, JPEG, WEBP, PDF, DOCX, etc.).\n" +
            "4. Your converted copy is immediately created right next to the original!\n\n" +
            "Method 2 (Shift + Right-Click):\n" +
            "1. Hold SHIFT and RIGHT-CLICK any file(s) in File Explorer.\n" +
            "2. Keep right-click held and release on your chosen format, or click any slice.\n\n" +
            "Method 3 (Global Hotkey):\n" +
            "1. Select any file in Explorer and press CTRL + SHIFT + X.\n\n" +
            "To cancel: Drop/release inside the center DROP hub or press Escape.\n" +
            "Click this tray icon anytime to open the O-Tangerine desktop window.",
            "O-Tangerine - Guide",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public static bool IsStartupEnabled()
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

    public static void SetStartup(bool enable)
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
        if (_hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
        }

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
