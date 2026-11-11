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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    private static int _cachedExplorerPid = -1;
    private static DateTime _lastPidCheck = DateTime.MinValue;

    public static bool IsExplorerProcess(uint pid)
    {
        if (pid == 0) return false;
        if (_cachedExplorerPid > 0 && pid == (uint)_cachedExplorerPid && (DateTime.UtcNow - _lastPidCheck).TotalSeconds < 30)
        {
            return true;
        }

        try
        {
            var proc = Process.GetProcessById((int)pid);
            bool isExp = string.Equals(proc.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
            if (isExp)
            {
                _cachedExplorerPid = (int)pid;
                _lastPidCheck = DateTime.UtcNow;
            }
            return isExp;
        }
        catch { return false; }
    }

    public bool IsExplorerOrDesktopWindow(POINT pt, out IntPtr rootHwnd)
    {
        rootHwnd = IntPtr.Zero;
        try
        {
            IntPtr hwndUnderPoint = WindowFromPoint(pt);
            if (hwndUnderPoint != IntPtr.Zero)
            {
                rootHwnd = GetAncestor(hwndUnderPoint, GA_ROOT);
                if (rootHwnd == IntPtr.Zero) rootHwnd = hwndUnderPoint;

                string className = GetWindowClassName(rootHwnd);
                string childClass = GetWindowClassName(hwndUnderPoint);

                // Standard Explorer, Windows 11 Tabs, legacy Explorer, or Desktop
                if (className is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW")
                {
                    return true;
                }

                if (childClass is "SHELLDLL_DefView" or "DirectUIHWND" or "SysListView32" or "XamlWidgetHostInputSinkWindow" or "XamlExplorerHostIslandWindow")
                {
                    return true;
                }

                // Process check: Is the window owned by explorer.exe?
                GetWindowThreadProcessId(hwndUnderPoint, out uint pid);
                if (pid > 0 && IsExplorerProcess(pid))
                {
                    return true;
                }
            }

            // Fallback: check foreground window
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                string fgClass = GetWindowClassName(fg);
                if (fgClass is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW")
                {
                    rootHwnd = fg;
                    return true;
                }

                GetWindowThreadProcessId(fg, out uint fgPid);
                if (fgPid > 0 && IsExplorerProcess(fgPid))
                {
                    rootHwnd = fg;
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    public IReadOnlyList<string> GetTargetFilePaths(POINT pt)
    {
        var results = new List<string>();

        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return results;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return results;

            IntPtr hwndUnderPoint = WindowFromPoint(pt);
            IntPtr rootHwnd = hwndUnderPoint != IntPtr.Zero ? GetAncestor(hwndUnderPoint, GA_ROOT) : IntPtr.Zero;
            if (rootHwnd == IntPtr.Zero) rootHwnd = hwndUnderPoint;
            IntPtr fgHwnd = GetForegroundWindow();

            dynamic windows = shell.Windows();
            int winCount = (int)windows.Count;

            // 1. First prioritize the active window under cursor or foreground Explorer window
            for (int i = 0; i < winCount; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    IntPtr wHwnd = new IntPtr((long)window.HWND);
                    if (wHwnd == rootHwnd || (fgHwnd != IntPtr.Zero && wHwnd == fgHwnd))
                    {
                        var paths = GetPathsFromWindowDocument(window);
                        if (paths.Count > 0)
                        {
                            App.Log($"[Explorer] Matched active window HWND {wHwnd}, found {paths.Count} selected item(s): {string.Join(", ", paths)}");
                            return paths.Distinct().ToList();
                        }
                    }
                }
                catch { }
            }

            // 2. Check Desktop for selected items if cursor or foreground is desktop
            string rootClass = rootHwnd != IntPtr.Zero ? GetWindowClassName(rootHwnd) : "";
            string fgClass = fgHwnd != IntPtr.Zero ? GetWindowClassName(fgHwnd) : "";
            if (rootClass is "Progman" or "WorkerW" || fgClass is "Progman" or "WorkerW")
            {
                var dPaths = GetDesktopSelectedFilePaths(shell);
                if (dPaths.Count > 0)
                {
                    App.Log($"[Explorer] Matched Desktop, found {dPaths.Count} selected item(s): {string.Join(", ", dPaths)}");
                    return dPaths.Distinct().ToList();
                }
            }

            // 3. Fallback to any open Explorer window with active selection (first non-empty)
            for (int i = 0; i < winCount; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    var paths = GetPathsFromWindowDocument(window);
                    if (paths.Count > 0)
                    {
                        App.Log($"[Explorer] Matched Explorer window {i}, found {paths.Count} selected item(s): {string.Join(", ", paths)}");
                        return paths.Distinct().ToList();
                    }
                }
                catch { }
            }

            // 4. Fallback Desktop
            var fallbackDesk = GetDesktopSelectedFilePaths(shell);
            if (fallbackDesk.Count > 0)
            {
                App.Log($"[Explorer] Fallback Desktop, found {fallbackDesk.Count} item(s)");
                return fallbackDesk.Distinct().ToList();
            }

            // 5. Fallback: UI Automation directly at cursor coordinates
            var openFolderPaths = new List<string>();
            for (int i = 0; i < winCount; i++)
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

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] standardDirs =
            {
                Path.Combine(userProfile, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            foreach (var dir in standardDirs)
            {
                if (Directory.Exists(dir) && !openFolderPaths.Contains(dir))
                {
                    openFolderPaths.Add(dir);
                }
            }

            var uiaPath = GetPathFromUIAutomation(pt, openFolderPaths);
            if (!string.IsNullOrEmpty(uiaPath) && File.Exists(uiaPath))
            {
                App.Log($"[Explorer] UIAutomation found item: {uiaPath}");
                results.Add(uiaPath);
                return results;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Explorer] GetTargetFilePaths error: {ex.Message}");
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

            // Check element itself and up to 8 parent levels
            var current = element;
            for (int depth = 0; depth < 8 && current != null; depth++)
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
                                var match = Directory.EnumerateFiles(folder).FirstOrDefault(f =>
                                    string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
                                if (match != null) return match;
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
