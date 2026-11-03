using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using RadialConvert.Core.Models;

namespace RadialConvert.Core.Services;

public class OneNoteConverter
{
    private static bool? _isOneNoteInstalled;
    private static bool? _isPythonDocxAvailable;

    public bool IsOneNoteAvailable()
    {
        if (_isOneNoteInstalled.HasValue) return _isOneNoteInstalled.Value;
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey("OneNote.Application");
            _isOneNoteInstalled = (key != null);
        }
        catch
        {
            _isOneNoteInstalled = false;
        }
        return _isOneNoteInstalled.Value;
    }

    public bool IsDocxAvailable()
    {
        if (_isPythonDocxAvailable.HasValue) return _isPythonDocxAvailable.Value;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "-c \"import pdf2docx\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(3000);
                _isPythonDocxAvailable = (proc.ExitCode == 0);
                return _isPythonDocxAvailable.Value;
            }
        }
        catch { }
        _isPythonDocxAvailable = false;
        return false;
    }

    public bool CanHandle(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".one", StringComparison.OrdinalIgnoreCase) && IsOneNoteAvailable();
    }

    public IReadOnlyList<ConversionOption> GetOptions(string filePath)
    {
        var options = new List<ConversionOption>();
        if (!CanHandle(filePath)) return options;

        options.Add(new ConversionOption
        {
            Id = "PDF",
            DisplayName = "PDF",
            Description = "OneNote → Native Vector PDF",
            TargetExtension = ".pdf",
            AccentColor = "#FF6619"
        });

        if (IsDocxAvailable())
        {
            options.Add(new ConversionOption
            {
                Id = "DOCX",
                DisplayName = "DOCX",
                Description = "OneNote → Formatted Word Doc",
                TargetExtension = ".docx",
                AccentColor = "#2B579A"
            });
        }

        options.Add(new ConversionOption
        {
            Id = "XPS",
            DisplayName = "XPS",
            Description = "OneNote → XML Paper Document",
            TargetExtension = ".xps",
            AccentColor = "#0284C7"
        });

        return options;
    }

    public async Task<string> ConvertAsync(string sourcePath, string targetExtensionOrId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Source OneNote file not found: {sourcePath}");
        }

        string directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string normalized = targetExtensionOrId.TrimStart('.').ToLowerInvariant();

        if (normalized == "pdf")
        {
            string destPdf = ImageConverter.GetUniqueDestinationPath(directory, baseName, ".pdf");
            await ExportToOneNoteFormatAsync(sourcePath, destPdf, 3, cancellationToken);
            return destPdf;
        }
        else if (normalized == "xps")
        {
            string destXps = ImageConverter.GetUniqueDestinationPath(directory, baseName, ".xps");
            await ExportToOneNoteFormatAsync(sourcePath, destXps, 4, cancellationToken);
            return destXps;
        }
        else if (normalized is "docx" or "doc")
        {
            string destDocx = ImageConverter.GetUniqueDestinationPath(directory, baseName, ".docx");
            string tempPdf = Path.Combine(Path.GetTempPath(), $"onenote_temp_{Guid.NewGuid():N}.pdf");

            try
            {
                await ExportToOneNoteFormatAsync(sourcePath, tempPdf, 3, cancellationToken);
                await ConvertPdfToDocxAsync(tempPdf, destDocx, cancellationToken);
                return destDocx;
            }
            finally
            {
                if (File.Exists(tempPdf))
                {
                    try { File.Delete(tempPdf); } catch { }
                }
            }
        }
        else
        {
            throw new NotSupportedException($"Target format '{targetExtensionOrId}' is not supported for OneNote files.");
        }
    }

    private static Task ExportToOneNoteFormatAsync(string sourcePath, string destPath, int publishFormat, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            string escapedSource = sourcePath.Replace("'", "''");
            string escapedDest = destPath.Replace("'", "''");

            string script = $@"
$ErrorActionPreference = 'Stop'
$on = New-Object -ComObject OneNote.Application
try {{
    $sectionId = ''
    $on.OpenHierarchy('{escapedSource}', '', [ref]$sectionId, 0)
    $on.Publish($sectionId, '{escapedDest}', {publishFormat}, '')
}} finally {{
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($on) | Out-Null
}}
";
            byte[] bytes = System.Text.Encoding.Unicode.GetBytes(script);
            string encoded = Convert.ToBase64String(bytes);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("Failed to launch PowerShell for OneNote export.");

            // Wait up to 3 minutes for large OneNote files
            bool exited = proc.WaitForExit(180_000);
            if (!exited)
            {
                try { proc.Kill(); } catch { }
                throw new TimeoutException("OneNote conversion timed out after 3 minutes.");
            }

            if (proc.ExitCode != 0 || !File.Exists(destPath))
            {
                string err = proc.StandardError.ReadToEnd();
                throw new InvalidOperationException($"OneNote export failed (Code {proc.ExitCode}): {err}");
            }
        }, cancellationToken);
    }

    private static Task ConvertPdfToDocxAsync(string pdfPath, string docxPath, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import sys; from pdf2docx import parse; parse(sys.argv[1], sys.argv[2])");
            psi.ArgumentList.Add(pdfPath);
            psi.ArgumentList.Add(docxPath);

            using var proc = Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("Failed to launch python for PDF to DOCX conversion.");

            bool exited = proc.WaitForExit(180_000);
            if (!exited)
            {
                try { proc.Kill(); } catch { }
                throw new TimeoutException("PDF to DOCX conversion timed out.");
            }

            if (proc.ExitCode != 0 || !File.Exists(docxPath))
            {
                string err = proc.StandardError.ReadToEnd();
                throw new InvalidOperationException($"DOCX conversion failed: {err}");
            }
        }, cancellationToken);
    }
}
