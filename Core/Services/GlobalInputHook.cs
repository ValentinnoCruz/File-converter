using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    private readonly Action<IReadOnlyList<string>> _onConversionSuccess;

    private volatile bool _isRadialMenuOpen = false;
    private RadialMenuWindow? _currentRadialWindow = null;
    private IReadOnlyList<string>? _currentFilePaths = null;

    public GlobalInputHook(
        ExplorerService explorerService,
        ImageConverter imageConverter,
        Action<IReadOnlyList<string>> onConversionSuccess)
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
                bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
                                   (GetAsyncKeyState(0xA0) & 0x8000) != 0 ||
                                   (GetAsyncKeyState(0xA1) & 0x8000) != 0;

                if (isShiftDown)
                {
                    var expPt = new ExplorerService.POINT { X = pt.x, Y = pt.y };
                    if (_explorerService.IsExplorerOrDesktopWindow(expPt, out _))
                    {
                        var filePaths = _explorerService.GetTargetFilePaths(expPt);

                        if (filePaths != null && filePaths.Count > 0 && _imageConverter.CanHandleAll(filePaths))
                        {
                            var options = _imageConverter.GetOptions(filePaths);
                            if (options.Count > 0)
                            {
                                _currentFilePaths = filePaths;
                                _isRadialMenuOpen = true;

                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    CloseRadialMenu();
                                    _currentRadialWindow = new RadialMenuWindow(filePaths, options, pt.x, pt.y);
                                    _currentRadialWindow.OnOptionSelected += (selectedOption) =>
                                    {
                                        ExecuteConversion(selectedOption);
                                    };
                                    _currentRadialWindow.Closed += (s, e) =>
                                    {
                                        _isRadialMenuOpen = false;
                                        _currentRadialWindow = null;
                                        _currentFilePaths = null;
                                    };
                                    _currentRadialWindow.Show();
                                    _currentRadialWindow.Activate();
                                });

                                // Swallow WM_RBUTTONDOWN so Windows Explorer context menu doesn't show
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }
            else if (msg == WM_MOUSEMOVE)
            {
                if (_isRadialMenuOpen && _currentRadialWindow != null)
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
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
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_currentRadialWindow != null && _currentFilePaths != null)
                        {
                            var selectedOption = _currentRadialWindow.GetSelectedOption();
                            if (selectedOption != null)
                            {
                                ExecuteConversion(selectedOption);
                            }
                            // If selectedOption == null (e.g. mouse released in center hub),
                            // keep the radial menu open so the user can click their choice!
                        }
                    });

                    // Swallow the WM_RBUTTONUP so Windows Explorer context menu doesn't show
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void ExecuteConversion(ConversionOption selectedOption)
    {
        if (_currentRadialWindow == null || _currentFilePaths == null) return;

        var windowToClose = _currentRadialWindow;
        var sourceFiles = _currentFilePaths;
        _currentRadialWindow = null;
        _currentFilePaths = null;
        _isRadialMenuOpen = false;

        windowToClose.ShowConvertingFeedback(selectedOption.TargetExtension);

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try { windowToClose.Close(); } catch { }
                });

                var createdPaths = await _imageConverter.ConvertBatchAsync(sourceFiles, selectedOption.Id);
                _onConversionSuccess?.Invoke(createdPaths);
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to convert file(s):\n{ex.Message}",
                        "Open Tangerine Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        });
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
        _currentFilePaths = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
