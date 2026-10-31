using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        // Standard Explorer, Windows 11 Tabs, legacy Explorer, or Desktop
        if (className is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW")
        {
            return true;
        }

        if (childClass is "SHELLDLL_DefView" or "DirectUIHWND" or "SysListView32" or "XamlWidgetHostInputSinkWindow")
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

            // Collect all candidate open folder paths
            var openFolderPaths = new List<string>();
            dynamic windows = shell.Windows();
            for (int i = 0; i < windows.Count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    string url = (string)window.LocationURL;
                    if (url.StartsWith("file:///"))
                    {
                        string fp = Uri.UnescapeDataString(url.Substring(8).Replace('/', '\\'));
                        if (Directory.Exists(fp) && !openFolderPaths.Contains(fp))
                        {
                            openFolderPaths.Add(fp);
                        }
                    }
                }
                catch { }
            }

            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!openFolderPaths.Contains(desktopDir)) openFolderPaths.Add(desktopDir);

            // 1. Try currently selected items in all active windows
            for (int i = 0; i < windows.Count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    var paths = GetPathsFromWindowDocument(window);
                    if (paths.Count > 0)
                    {
                        results.AddRange(paths);
                    }
                }
                catch { }
            }

            // 2. Desktop items if clicking desktop
            if (rootClass is "Progman" or "WorkerW")
            {
                var dPaths = GetDesktopSelectedFilePaths(shell);
                if (dPaths.Count > 0)
                {
                    results.AddRange(dPaths);
                }
            }

            if (results.Count > 0)
            {
                return results.Distinct().ToList();
            }

            // 3. Fallback: UI Automation hit testing at cursor coordinates
            var uiaPath = GetPathFromUIAutomation(pt, openFolderPaths);
            if (!string.IsNullOrEmpty(uiaPath) && File.Exists(uiaPath))
            {
                results.Add(uiaPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetTargetFilePaths error: {ex.Message}");
        }

        return results.Distinct().ToList();
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

    private string? GetPathFromUIAutomation(POINT pt, List<string> candidateFolders)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
            if (element == null) return null;

            // Check element itself and up to 3 parent levels (in case text/icon child was hit)
            var current = element;
            for (int depth = 0; depth < 4 && current != null; depth++)
            {
                string name = current.Current.Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (File.Exists(name)) return name;

                    foreach (var folder in candidateFolders)
                    {
                        var full = Path.Combine(folder, name);
                        if (File.Exists(full)) return full;

                        // Check if file extension is hidden in Explorer
                        if (Directory.Exists(folder))
                        {
                            try
                            {
                                var matches = Directory.GetFiles(folder, name + ".*");
                                if (matches.Length > 0 && File.Exists(matches[0]))
                                {
                                    return matches[0];
                                }
                            }
                            catch { }
                        }
                    }
                }

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
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
