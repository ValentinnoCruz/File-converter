using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using RadialConvert.Core.Models;
using RadialConvert.UI;

namespace RadialConvert.Core.Services;

public class GlobalInputHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int VK_SHIFT = 0x10;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly LowLevelMouseProc _mouseProc;
    private IntPtr _hookId = IntPtr.Zero;

    private readonly ExplorerService _explorerService;
    private readonly ImageConverter _imageConverter;
    private readonly Action<string> _onConversionSuccess;

    private volatile bool _isRadialMenuOpen = false;
    private RadialMenuWindow? _currentRadialWindow = null;
    private string? _currentFilePath = null;

    public GlobalInputHook(
        ExplorerService explorerService,
        ImageConverter imageConverter,
        Action<string> onConversionSuccess)
    {
        _explorerService = explorerService;
        _imageConverter = imageConverter;
        _onConversionSuccess = onConversionSuccess;
        _mouseProc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        CloseRadialMenu();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var pt = hookStruct.pt;

            if (msg == WM_RBUTTONDOWN)
            {
                bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                if (isShiftDown)
                {
                    var expPt = new ExplorerService.POINT { X = pt.x, Y = pt.y };
                    if (_explorerService.IsExplorerOrDesktopWindow(expPt, out _))
                    {
                        // Give Explorer ~15ms to select the clicked file upon WM_RBUTTONDOWN
                        Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(20);
                                var filePath = _explorerService.GetTargetFilePath(expPt);

                                if (!string.IsNullOrEmpty(filePath) && _imageConverter.CanHandle(filePath))
                                {
                                    var options = _imageConverter.GetOptions(filePath);
                                    if (options.Count > 0)
                                    {
                                        _currentFilePath = filePath;
                                        _isRadialMenuOpen = true;

                                        // Show radial menu on UI thread
                                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            CloseRadialMenu();
                                            _currentRadialWindow = new RadialMenuWindow(filePath, options, pt.x, pt.y);
                                            _currentRadialWindow.Show();
                                        });
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error preparing radial menu: {ex.Message}");
                            }
                        });
                    }
                }
            }
            else if (msg == WM_MOUSEMOVE)
            {
                if (_isRadialMenuOpen && _currentRadialWindow != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_isRadialMenuOpen && _currentRadialWindow != null)
                        {
                            _currentRadialWindow.UpdateCursor(pt.x, pt.y);
                        }
                    });
                }
            }
            else if (msg == WM_RBUTTONUP)
            {
                if (_isRadialMenuOpen)
                {
                    _isRadialMenuOpen = false;

                    // Execute chosen option on release
                    System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                    {
                        if (_currentRadialWindow != null && _currentFilePath != null)
                        {
                            var selectedOption = _currentRadialWindow.GetSelectedOption();
                            var windowToClose = _currentRadialWindow;
                            var sourceFile = _currentFilePath;
                            _currentRadialWindow = null;
                            _currentFilePath = null;

                            if (selectedOption != null)
                            {
                                windowToClose.ShowConvertingFeedback(selectedOption.TargetExtension);
                                // Brief visual confirmation
                                await Task.Delay(180);
                                windowToClose.Close();

                                try
                                {
                                    var createdPath = await _imageConverter.ConvertAsync(sourceFile, selectedOption.TargetExtension);
                                    _onConversionSuccess?.Invoke(createdPath);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"Failed to convert file:\n{ex.Message}", "QuickConvert Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            }
                            else
                            {
                                windowToClose.Close();
                            }
                        }
                    });

                    // SWALLOW the WM_RBUTTONUP so Windows Explorer never opens the context menu
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void CloseRadialMenu()
    {
        if (_currentRadialWindow != null)
        {
            try
            {
                _currentRadialWindow.Close();
            }
            catch { }
            _currentRadialWindow = null;
        }
        _isRadialMenuOpen = false;
        _currentFilePath = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
