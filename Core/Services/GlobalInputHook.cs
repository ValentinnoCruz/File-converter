using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RadialConvert.Core.Models;
using RadialConvert.UI;

namespace RadialConvert.Core.Services;

public class GlobalInputHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
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

    private const uint EXTRA_INFO_SYNTHETIC = 0x54414E47; // "TANG"
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    private readonly LowLevelMouseProc _mouseProc;
    private IntPtr _hookId = IntPtr.Zero;

    private readonly ExplorerService _explorerService;
    private readonly ImageConverter _imageConverter;
    private readonly Action<IReadOnlyList<string>> _onConversionSuccess;

    private volatile bool _isRadialMenuOpen = false;
    private volatile bool _isShiftRightPending = false;
    private volatile bool _isLeftDown = false;
    private volatile bool _dragTriggered = false;
    private POINT _leftDownPt;

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

    public bool IsRunning => _hookId != IntPtr.Zero;

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        string modName = curModule?.ModuleName ?? string.Empty;
        IntPtr hMod = GetModuleHandle(modName);
        if (hMod == IntPtr.Zero)
        {
            hMod = GetModuleHandle(null!);
        }

        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        if (_hookId == IntPtr.Zero)
        {
            // Fallback: try with IntPtr.Zero
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
        }
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
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (hookStruct.dwExtraInfo == (IntPtr)EXTRA_INFO_SYNTHETIC)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            int msg = wParam.ToInt32();
            var pt = hookStruct.pt;

            if (msg == WM_LBUTTONDOWN)
            {
                _isLeftDown = true;
                _leftDownPt = pt;
                _dragTriggered = false;
            }
            else if (msg == WM_LBUTTONUP)
            {
                _isLeftDown = false;
                _dragTriggered = false;

                // On Drag Release (Drop on slice)
                if (_isRadialMenuOpen)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_currentRadialWindow != null && _currentFilePaths != null && _currentFilePaths.Count > 0)
                        {
                            var selectedOption = _currentRadialWindow.GetSelectedOption();
                            if (selectedOption != null)
                            {
                                ExecuteConversion(_currentFilePaths, selectedOption);
                            }
                            else
                            {
                                // Dropped in center hub or outside wheel -> cancel
                                CloseRadialMenu();
                            }
                        }
                        else
                        {
                            CloseRadialMenu();
                        }
                    });
                }
            }
            else if (msg == WM_RBUTTONDOWN)
            {
                bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
                                   (GetAsyncKeyState(0xA0) & 0x8000) != 0 ||
                                   (GetAsyncKeyState(0xA1) & 0x8000) != 0;

                if (isShiftDown)
                {
                    App.Log($"[InputHook] Shift+Right-Click detected at ({pt.x}, {pt.y})");
                    _isShiftRightPending = true;

                    // Immediately return to OS without blocking the low-level hook!
                    ThreadPool.QueueUserWorkItem(_ => ProcessShiftRightClickAsync(pt));

                    return (IntPtr)1; // Swallow WM_RBUTTONDOWN
                }
            }
            else if (msg == WM_MOUSEMOVE)
            {
                // Drag + Shift detection (Original Tangerine style)
                if (_isLeftDown && !_dragTriggered && !_isRadialMenuOpen)
                {
                    bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
                                       (GetAsyncKeyState(0xA0) & 0x8000) != 0 ||
                                       (GetAsyncKeyState(0xA1) & 0x8000) != 0;

                    int dx = pt.x - _leftDownPt.x;
                    int dy = pt.y - _leftDownPt.y;
                    int distSq = dx * dx + dy * dy;

                    if (isShiftDown && distSq >= 36)
                    {
                        App.Log($"[InputHook] Drag + Shift detected at ({pt.x}, {pt.y}), distSq={distSq}");
                        _dragTriggered = true;
                        ThreadPool.QueueUserWorkItem(_ => ProcessDragStartAsync(pt));
                    }
                }

                if (_isRadialMenuOpen && _currentRadialWindow != null)
                {
                    _currentRadialWindow.Dispatcher.BeginInvoke(() =>
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
                        if (_currentRadialWindow != null && _currentFilePaths != null && _currentFilePaths.Count > 0)
                        {
                            var selectedOption = _currentRadialWindow.GetSelectedOption();
                            if (selectedOption != null)
                            {
                                ExecuteConversion(_currentFilePaths, selectedOption);
                            }
                            // If selectedOption == null (released in center hub or during quick tap),
                            // keep the radial menu open so the user can click their choice!
                        }
                    });

                    return (IntPtr)1; // Swallow
                }

                if (_isShiftRightPending)
                {
                    return (IntPtr)1; // Swallow while pending
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void ProcessDragStartAsync(POINT pt)
    {
        try
        {
            App.Log($"[InputHook] ProcessDragStartAsync at ({pt.x}, {pt.y})");
            var expPt = new ExplorerService.POINT { X = pt.x, Y = pt.y };
            var filePaths = _explorerService.GetTargetFilePaths(expPt);

            if (filePaths == null || filePaths.Count == 0 || !_imageConverter.CanHandleAll(filePaths))
            {
                App.Log($"[InputHook] Drag rejected: count={filePaths?.Count ?? 0}, canHandle={_imageConverter.CanHandleAll(filePaths ?? Array.Empty<string>())}");
                _dragTriggered = false;
                return;
            }

            var options = _imageConverter.GetOptions(filePaths);
            if (options == null || options.Count == 0)
            {
                App.Log("[InputHook] Drag rejected: no options available");
                _dragTriggered = false;
                return;
            }

            App.Log($"[InputHook] Opening drag radial wheel for {filePaths.Count} files with {options.Count} options");
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                CloseRadialMenu();

                _currentFilePaths = filePaths;
                _isRadialMenuOpen = true;

                var window = new RadialMenuWindow(filePaths, options, pt.x, pt.y);
                _currentRadialWindow = window;

                window.OnOptionSelected += (selectedOption) =>
                {
                    if (_currentFilePaths != null && _currentFilePaths.Count > 0)
                    {
                        ExecuteConversion(_currentFilePaths, selectedOption);
                    }
                };

                window.OnFilesDropped += (droppedFiles, selectedOption) =>
                {
                    ExecuteConversion(droppedFiles, selectedOption);
                };

                window.Closed += (s, e) =>
                {
                    _isRadialMenuOpen = false;
                    _currentRadialWindow = null;
                    _currentFilePaths = null;
                };

                window.Show();
                window.Activate();
            });
        }
        catch (Exception ex)
        {
            _dragTriggered = false;
            App.Log($"[InputHook] Error starting drag radial menu: {ex}");
        }
    }

    private void ProcessShiftRightClickAsync(POINT pt)
    {
        try
        {
            App.Log($"[InputHook] ProcessShiftRightClickAsync at ({pt.x}, {pt.y})");
            var expPt = new ExplorerService.POINT { X = pt.x, Y = pt.y };
            var filePaths = _explorerService.GetTargetFilePaths(expPt);

            if (filePaths != null && filePaths.Count > 0 && _imageConverter.CanHandleAll(filePaths))
            {
                var options = _imageConverter.GetOptions(filePaths);
                if (options != null && options.Count > 0)
                {
                    App.Log($"[InputHook] Opening Shift+Right-Click radial wheel for {filePaths.Count} files with {options.Count} options");
                    _currentFilePaths = filePaths;
                    _isRadialMenuOpen = true;
                    _isShiftRightPending = false;

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        CloseRadialMenu();
                        var window = new RadialMenuWindow(filePaths, options, pt.x, pt.y);
                        _currentRadialWindow = window;

                        window.OnOptionSelected += (selectedOption) =>
                        {
                            ExecuteConversion(filePaths, selectedOption);
                        };

                        window.OnFilesDropped += (droppedFiles, selectedOption) =>
                        {
                            ExecuteConversion(droppedFiles, selectedOption);
                        };

                        window.Closed += (s, e) =>
                        {
                            _isRadialMenuOpen = false;
                            _currentRadialWindow = null;
                            _currentFilePaths = null;
                        };

                        window.Show();
                        window.Activate();
                    });

                    return;
                }
            }

            App.Log($"[InputHook] Shift+Right-Click pass through (unhandled): count={filePaths?.Count ?? 0}");
            // Not handled by O-Tangerine -> pass through right click so Windows shows default menu
            _isShiftRightPending = false;
            PassThroughRightClick();
        }
        catch (Exception ex)
        {
            App.Log($"[InputHook] Error processing shift right click: {ex}");
            _isShiftRightPending = false;
            PassThroughRightClick();
        }
    }

    private static void PassThroughRightClick()
    {
        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, (UIntPtr)EXTRA_INFO_SYNTHETIC);
        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, (UIntPtr)EXTRA_INFO_SYNTHETIC);
    }

    private void ExecuteConversion(IReadOnlyList<string> sourceFiles, ConversionOption selectedOption)
    {
        App.Log($"[InputHook] ExecuteConversion called for {sourceFiles.Count} file(s) -> {selectedOption.Id} ({selectedOption.TargetExtension})");
        if (_currentRadialWindow == null) return;

        var windowToClose = _currentRadialWindow;
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
                App.Log($"[InputHook] Successfully converted {createdPaths.Count} file(s): {string.Join(", ", createdPaths)}");
                _onConversionSuccess?.Invoke(createdPaths);
            }
            catch (Exception ex)
            {
                App.Log($"[InputHook] Conversion failed: {ex}");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to convert file(s):\n{ex.Message}",
                        "O-Tangerine Error",
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
