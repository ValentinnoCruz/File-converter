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

    public IReadOnlyList<string> GetTargetFilePaths(POINT pt)
    {
        var results = new List<string>();

        try
        {
            if (!IsExplorerOrDesktopWindow(pt, out IntPtr rootHwnd))
            {
                return results;
            }

            string rootClass = GetWindowClassName(rootHwnd);
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return results;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return results;

            // Case 1: Desktop
            if (rootClass is "Progman" or "WorkerW")
            {
                var desktopPaths = GetDesktopSelectedFilePaths(shell);
                if (desktopPaths.Count > 0)
                {
                    return desktopPaths;
                }
            }

            // Case 2: Open Explorer Window matching rootHwnd or foreground
            dynamic windows = shell.Windows();
            for (int i = 0; i < windows.Count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    IntPtr hwnd = (IntPtr)(long)window.HWND;

                    if (hwnd == rootHwnd || windows.Count == 1)
                    {
                        var paths = GetPathsFromWindowDocument(window);
                        if (paths.Count > 0)
                        {
                            return paths;
                        }
                    }
                }
                catch { }
            }

            // Fallback: Check foreground window
            IntPtr foreground = GetForegroundWindow();
            for (int i = 0; i < windows.Count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    IntPtr hwnd = (IntPtr)(long)window.HWND;
                    if (hwnd == foreground)
                    {
                        var paths = GetPathsFromWindowDocument(window);
                        if (paths.Count > 0)
                        {
                            return paths;
                        }
                    }
                }
                catch { }
            }

            // Fallback: UI Automation single file lookup
            var uiaPath = GetPathFromUIAutomation(pt);
            if (!string.IsNullOrEmpty(uiaPath) && File.Exists(uiaPath))
            {
                results.Add(uiaPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetTargetFilePaths error: {ex.Message}");
        }

        return results;
    }

    public string? GetTargetFilePath(POINT pt)
    {
        var paths = GetTargetFilePaths(pt);
        return paths.Count > 0 ? paths[0] : null;
    }

    private List<string> GetDesktopSelectedFilePaths(dynamic shell)
    {
        var list = new List<string>();
        try
        {
            object pvarLoc = 0;
            object pvarLocRoot = 0;
            dynamic desktop = shell.Windows().FindWindowSW(ref pvarLoc, ref pvarLocRoot, 8, out int pHWND, 1);
            if (desktop?.Document != null)
            {
                return GetPathsFromWindowDocument(desktop);
            }
        }
        catch { }
        return list;
    }

    private List<string> GetPathsFromWindowDocument(dynamic window)
    {
        var list = new List<string>();
        try
        {
            dynamic doc = window.Document;
            if (doc == null) return list;

            dynamic? selected = doc.SelectedItems();
            int count = (int?)selected?.Count ?? 0;
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    dynamic? item = selected?.Item(i);
                    if (item != null)
                    {
                        string path = (string)item.Path;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            list.Add(path);
                        }
                    }
                }
                if (list.Count > 0) return list;
            }

            // If no multi-selection, try focused item
            dynamic focused = doc.FocusedItem;
            if (focused != null)
            {
                string path = (string)focused.Path;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    list.Add(path);
                }
            }
        }
        catch { }
        return list;
    }

    private string? GetPathFromUIAutomation(POINT pt)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
            if (element == null) return null;

            string name = element.Current.Name;
            if (string.IsNullOrWhiteSpace(name)) return null;

            if (File.Exists(name)) return name;

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
