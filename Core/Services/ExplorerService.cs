using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace RadialConvert.Core.Services;

public class ExplorerService
{
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public bool IsExplorerOrDesktopWindow(POINT pt, out IntPtr rootHwnd)
    {
        rootHwnd = IntPtr.Zero;
        IntPtr hwndUnderPoint = WindowFromPoint(pt);
        if (hwndUnderPoint == IntPtr.Zero) return false;

        rootHwnd = GetAncestor(hwndUnderPoint, GA_ROOT);
        if (rootHwnd == IntPtr.Zero) rootHwnd = hwndUnderPoint;

        string className = GetWindowClassName(rootHwnd);
        string childClass = GetWindowClassName(hwndUnderPoint);

        // Standard Explorer, legacy Explorer, or Desktop
        if (className is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW")
        {
            return true;
        }

        if (childClass is "SHELLDLL_DefView" or "DirectUIHWND" or "SysListView32")
        {
            return true;
        }

        return false;
    }

    public string? GetTargetFilePath(POINT pt)
    {
        try
        {
            if (!IsExplorerOrDesktopWindow(pt, out IntPtr rootHwnd))
            {
                return null;
            }

            string rootClass = GetWindowClassName(rootHwnd);
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return null;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            // Case 1: Desktop
            if (rootClass is "Progman" or "WorkerW")
            {
                var desktopPath = GetDesktopSelectedFilePath(shell);
                if (!string.IsNullOrEmpty(desktopPath) && File.Exists(desktopPath))
                {
                    return desktopPath;
                }
            }

            // Case 2: Open Explorer Window
            dynamic windows = shell.Windows();
            for (int i = 0; i < windows.Count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    IntPtr hwnd = (IntPtr)(long)window.HWND;

                    // Match root HWND or any active window
                    if (hwnd == rootHwnd || windows.Count == 1)
                    {
                        var path = GetPathFromWindowDocument(window);
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            return path;
                        }
                    }
                }
                catch
                {
                    // Continue checking next window
                }
            }

            // Fallback: Check all shell windows if foreground window matches
            IntPtr foreground = GetForegroundWindow();
            for (int i = 0; i < windows.Count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    IntPtr hwnd = (IntPtr)(long)window.HWND;
                    if (hwnd == foreground)
                    {
                        var path = GetPathFromWindowDocument(window);
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            return path;
                        }
                    }
                }
                catch { }
            }

            // Fallback: UI Automation hit test
            var uiaPath = GetPathFromUIAutomation(pt);
            if (!string.IsNullOrEmpty(uiaPath) && File.Exists(uiaPath))
            {
                return uiaPath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetTargetFilePath error: {ex.Message}");
        }

        return null;
    }

    private string? GetDesktopSelectedFilePath(dynamic shell)
    {
        try
        {
            object pvarLoc = 0;
            object pvarLocRoot = 0;
            dynamic desktop = shell.Windows().FindWindowSW(ref pvarLoc, ref pvarLocRoot, 8, out int pHWND, 1);
            if (desktop?.Document != null)
            {
                return GetPathFromWindowDocument(desktop);
            }
        }
        catch { }
        return null;
    }

    private string? GetPathFromWindowDocument(dynamic window)
    {
        try
        {
            dynamic doc = window.Document;
            if (doc == null) return null;

            dynamic? selected = doc.SelectedItems();
            if (selected != null && ((int?)selected?.Count ?? 0) > 0)
            {
                dynamic? first = selected?.Item(0);
                if (first != null)
                {
                    string path = (string)first.Path;
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }

            dynamic focused = doc.FocusedItem;
            if (focused != null)
            {
                string path = (string)focused.Path;
                if (!string.IsNullOrEmpty(path)) return path;
            }
        }
        catch { }
        return null;
    }

    private string? GetPathFromUIAutomation(POINT pt)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
            if (element == null) return null;

            // In Explorer, item name might be the file name
            string name = element.Current.Name;
            if (string.IsNullOrWhiteSpace(name)) return null;

            // Check if name itself is a full path
            if (File.Exists(name)) return name;

            // Try Desktop folder
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var candidateDesktop = Path.Combine(desktopDir, name);
            if (File.Exists(candidateDesktop)) return candidateDesktop;
        }
        catch { }
        return null;
    }

    private static string GetWindowClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
