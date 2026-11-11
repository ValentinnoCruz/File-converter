using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using RadialConvert.Core.Models;

namespace RadialConvert.Core.Services;

public class GoogleDocsConverter
{
    private static readonly HashSet<string> WordExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".doc", ".rtf", ".odt"
    };

    private static readonly HashSet<string> ExcelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx", ".xls", ".csv", ".tsv", ".ods"
    };

    private static readonly string[] Scopes = { DriveService.Scope.DriveFile };
    private const string ApplicationName = "O-Tangerine";

    public bool CanHandle(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string ext = Path.GetExtension(filePath);
        return WordExtensions.Contains(ext) || ExcelExtensions.Contains(ext);
    }

    public IReadOnlyList<ConversionOption> GetOptions(IReadOnlyList<string> filePaths)
    {
        var options = new List<ConversionOption>();
        if (filePaths == null || filePaths.Count == 0) return options;

        var suffixes = filePaths.Select(p => Path.GetExtension(p).ToLowerInvariant()).ToHashSet();

        if (suffixes.All(s => WordExtensions.Contains(s)))
        {
            options.Add(new ConversionOption
            {
                Id = "GDOC",
                DisplayName = "GDOC",
                Description = "Google Doc (Zero Formatting Loss)",
                TargetExtension = ".gdoc"
            });
            options.Add(new ConversionOption
            {
                Id = "PDF",
                DisplayName = "PDF",
                Description = "Word → PDF Document",
                TargetExtension = ".pdf"
            });
            options.Add(new ConversionOption
            {
                Id = "TXT",
                DisplayName = "TXT",
                Description = "Plain Text",
                TargetExtension = ".txt"
            });
        }
        else if (suffixes.All(s => ExcelExtensions.Contains(s)))
        {
            options.Add(new ConversionOption
            {
                Id = "GSHEET",
                DisplayName = "GSHEET",
                Description = "Google Sheet (Zero Formatting Loss)",
                TargetExtension = ".gsheet"
            });

            if (suffixes.All(s => s is ".csv" or ".tsv"))
            {
                options.Add(new ConversionOption
                {
                    Id = "XLSX",
                    DisplayName = "XLSX",
                    Description = "Excel Workbook",
                    TargetExtension = ".xlsx"
                });
            }
            else
            {
                options.Add(new ConversionOption
                {
                    Id = "CSV",
                    DisplayName = "CSV",
                    Description = "Comma-Separated Values",
                    TargetExtension = ".csv"
                });
            }

            options.Add(new ConversionOption
            {
                Id = "PDF",
                DisplayName = "PDF",
                Description = "Spreadsheet → PDF Document",
                TargetExtension = ".pdf"
            });
        }

        return options;
    }

    public async Task<string> ConvertAsync(string sourcePath, string targetOptionId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Source file not found: {sourcePath}");
        }

        string normalizedTarget = targetOptionId.TrimStart('.').ToUpperInvariant();
        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();

        if (normalizedTarget == "GDOC")
        {
            return await ConvertDocxToGoogleDocAsync(sourcePath, cancellationToken);
        }

        if (normalizedTarget == "GSHEET")
        {
            return await ConvertExcelToGoogleSheetAsync(sourcePath, cancellationToken);
        }

        if (WordExtensions.Contains(ext))
        {
            if (normalizedTarget == "PDF")
            {
                return await Task.Run(() => ConvertWordToPdfOffline(sourcePath), cancellationToken);
            }
            if (normalizedTarget == "TXT")
            {
                return await Task.Run(() => ConvertWordToTxtOffline(sourcePath), cancellationToken);
            }
        }

        if (ExcelExtensions.Contains(ext))
        {
            if (normalizedTarget == "CSV")
            {
                return await Task.Run(() => ConvertExcelToCsvOffline(sourcePath), cancellationToken);
            }
            if (normalizedTarget == "XLSX")
            {
                return await Task.Run(() => ConvertCsvToXlsxOffline(sourcePath), cancellationToken);
            }
            if (normalizedTarget == "PDF")
            {
                return await Task.Run(() => ConvertExcelToPdfOffline(sourcePath), cancellationToken);
            }
        }

        throw new NotSupportedException($"Conversion from {ext} to {targetOptionId} is not supported.");
    }

    public async Task<string> ConvertDocxToGoogleDocAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        App.Log($"[GoogleDocsConverter] Starting Word -> Google Doc conversion for {sourcePath}...");
        var service = await GetDriveServiceAsync(cancellationToken);

        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        string directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();

        string sourceMime = ext switch
        {
            ".doc" => "application/msword",
            ".rtf" => "application/rtf",
            ".odt" => "application/vnd.oasis.opendocument.text",
            _ => "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileNameWithoutExt,
            MimeType = "application/vnd.google-apps.document"
        };

        Google.Apis.Drive.v3.Data.File uploadedFile;
        using (var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var request = service.Files.Create(fileMetadata, fs, sourceMime);
            request.Fields = "id, name, webViewLink";
            var progress = await request.UploadAsync(cancellationToken);
            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                throw new InvalidOperationException($"Google Drive upload failed: {progress.Exception?.Message ?? progress.Status.ToString()}");
            }
            uploadedFile = request.ResponseBody;
        }

        string docId = uploadedFile.Id;
        string webViewLink = uploadedFile.WebViewLink ?? $"https://docs.google.com/document/d/{docId}/edit";

        string gdocPath = GetUniquePath(directory, fileNameWithoutExt, ".gdoc");
        var gdocContent = new
        {
            url = webViewLink,
            doc_id = docId,
            resource_id = $"document:{docId}"
        };
        await File.WriteAllTextAsync(gdocPath, JsonSerializer.Serialize(gdocContent, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        App.Log($"[GoogleDocsConverter] Successfully created Google Doc: {gdocPath} ({webViewLink})");

        try
        {
            Process.Start(new ProcessStartInfo(webViewLink) { UseShellExecute = true });
        }
        catch { }

        return gdocPath;
    }

    public async Task<string> ConvertExcelToGoogleSheetAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        App.Log($"[GoogleDocsConverter] Starting Excel -> Google Sheet conversion for {sourcePath}...");
        var service = await GetDriveServiceAsync(cancellationToken);

        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        string directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();

        string sourceMime = ext switch
        {
            ".xls" => "application/vnd.ms-excel",
            ".csv" => "text/csv",
            ".tsv" => "text/tab-separated-values",
            ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
            _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileNameWithoutExt,
            MimeType = "application/vnd.google-apps.spreadsheet"
        };

        Google.Apis.Drive.v3.Data.File uploadedFile;
        using (var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var request = service.Files.Create(fileMetadata, fs, sourceMime);
            request.Fields = "id, name, webViewLink";
            var progress = await request.UploadAsync(cancellationToken);
            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                throw new InvalidOperationException($"Google Drive upload failed: {progress.Exception?.Message ?? progress.Status.ToString()}");
            }
            uploadedFile = request.ResponseBody;
        }

        string sheetId = uploadedFile.Id;
        string webViewLink = uploadedFile.WebViewLink ?? $"https://docs.google.com/spreadsheets/d/{sheetId}/edit";

        string gsheetPath = GetUniquePath(directory, fileNameWithoutExt, ".gsheet");
        var gsheetContent = new
        {
            url = webViewLink,
            doc_id = sheetId,
            resource_id = $"spreadsheet:{sheetId}"
        };
        await File.WriteAllTextAsync(gsheetPath, JsonSerializer.Serialize(gsheetContent, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        App.Log($"[GoogleDocsConverter] Successfully created Google Sheet: {gsheetPath} ({webViewLink})");

        try
        {
            Process.Start(new ProcessStartInfo(webViewLink) { UseShellExecute = true });
        }
        catch { }

        return gsheetPath;
    }

    public static string GetSecretFilePath()
    {
        string localAppSecret = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OTangerine", "client_secret.json");
        if (File.Exists(localAppSecret)) return localAppSecret;

        string baseDirSecret = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client_secret.json");
        if (File.Exists(baseDirSecret)) return baseDirSecret;

        // Auto-discover in Downloads if user downloaded credentials
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
        {
            try
            {
                var match = Directory.EnumerateFiles(downloads, "client_secret_*.json").FirstOrDefault();
                if (match != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(localAppSecret)!);
                    File.Copy(match, localAppSecret, true);
                    return localAppSecret;
                }
            }
            catch { }
        }

        return localAppSecret;
    }

    private static async Task<DriveService> GetDriveServiceAsync(CancellationToken cancellationToken)
    {
        string secretPath = GetSecretFilePath();
        if (!File.Exists(secretPath))
        {
            throw new FileNotFoundException(
                "Google OAuth credentials (client_secret.json) not found.\n\n" +
                $"Please place your Google Cloud OAuth client_secret.json in:\n{secretPath}\n\n" +
                "Or in your Downloads folder as client_secret_*.json.");
        }

        string tokenDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OTangerine", "GoogleToken");
        Directory.CreateDirectory(tokenDir);

        UserCredential credential;
        using (var stream = new FileStream(secretPath, FileMode.Open, FileAccess.Read))
        {
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                cancellationToken,
                new FileDataStore(tokenDir, true));
        }

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        });
    }

    private string ConvertWordToPdfOffline(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        string destPath = GetUniquePath(directory, fileNameWithoutExt, ".pdf");

        Type? wordType = Type.GetTypeFromProgID("Word.Application");
        if (wordType == null)
        {
            throw new InvalidOperationException("Microsoft Word is not installed on this system for offline PDF conversion.");
        }

        dynamic? wordApp = Activator.CreateInstance(wordType);
        if (wordApp == null)
        {
            throw new InvalidOperationException("Failed to launch Microsoft Word COM instance.");
        }

        try
        {
            wordApp.Visible = false;
            dynamic doc = wordApp.Documents.Open(sourcePath, ReadOnly: true);
            try
            {
                // 17 = wdExportFormatPDF
                doc.ExportAsFixedFormat(destPath, 17);
                return destPath;
            }
            finally
            {
                doc.Close(false);
            }
        }
        finally
        {
            wordApp.Quit();
        }
    }

    private string ConvertWordToTxtOffline(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        string destPath = GetUniquePath(directory, fileNameWithoutExt, ".txt");

        Type? wordType = Type.GetTypeFromProgID("Word.Application");
        if (wordType == null)
        {
            throw new InvalidOperationException("Microsoft Word is not installed on this system for offline text conversion.");
        }

        dynamic? wordApp = Activator.CreateInstance(wordType);
        if (wordApp == null)
        {
            throw new InvalidOperationException("Failed to launch Microsoft Word COM instance.");
        }

        try
        {
            wordApp.Visible = false;
            dynamic doc = wordApp.Documents.Open(sourcePath, ReadOnly: true);
            try
            {
                // 2 = wdFormatText
                doc.SaveAs2(destPath, 2);
                return destPath;
            }
            finally
            {
                doc.Close(false);
            }
        }
        finally
        {
            wordApp.Quit();
        }
    }

    private string ConvertExcelToCsvOffline(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        string destPath = GetUniquePath(directory, fileNameWithoutExt, ".csv");

        Type? excelType = Type.GetTypeFromProgID("Excel.Application");
        if (excelType == null)
        {
            throw new InvalidOperationException("Microsoft Excel is not installed on this system for offline CSV conversion.");
        }

        dynamic? excelApp = Activator.CreateInstance(excelType);
        if (excelApp == null)
        {
            throw new InvalidOperationException("Failed to launch Microsoft Excel COM instance.");
        }

        try
        {
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;
            dynamic wb = excelApp.Workbooks.Open(sourcePath, ReadOnly: true);
            try
            {
                // 6 = xlCSV
                wb.SaveAs(destPath, 6);
                return destPath;
            }
            finally
            {
                wb.Close(false);
            }
        }
        finally
        {
            excelApp.Quit();
        }
    }

    private string ConvertExcelToPdfOffline(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        string destPath = GetUniquePath(directory, fileNameWithoutExt, ".pdf");

        Type? excelType = Type.GetTypeFromProgID("Excel.Application");
        if (excelType == null)
        {
            throw new InvalidOperationException("Microsoft Excel is not installed on this system for offline PDF conversion.");
        }

        dynamic? excelApp = Activator.CreateInstance(excelType);
        if (excelApp == null)
        {
            throw new InvalidOperationException("Failed to launch Microsoft Excel COM instance.");
        }

        try
        {
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;
            dynamic wb = excelApp.Workbooks.Open(sourcePath, ReadOnly: true);
            try
            {
                // 0 = xlTypePDF
                wb.ExportAsFixedFormat(0, destPath);
                return destPath;
            }
            finally
            {
                wb.Close(false);
            }
        }
        finally
        {
            excelApp.Quit();
        }
    }

    private string ConvertCsvToXlsxOffline(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        string destPath = GetUniquePath(directory, fileNameWithoutExt, ".xlsx");

        Type? excelType = Type.GetTypeFromProgID("Excel.Application");
        if (excelType == null)
        {
            throw new InvalidOperationException("Microsoft Excel is not installed on this system for offline XLSX conversion.");
        }

        dynamic? excelApp = Activator.CreateInstance(excelType);
        if (excelApp == null)
        {
            throw new InvalidOperationException("Failed to launch Microsoft Excel COM instance.");
        }

        try
        {
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;
            dynamic wb = excelApp.Workbooks.Open(sourcePath, ReadOnly: true);
            try
            {
                // 51 = xlOpenXMLWorkbook (.xlsx)
                wb.SaveAs(destPath, 51);
                return destPath;
            }
            finally
            {
                wb.Close(false);
            }
        }
        finally
        {
            excelApp.Quit();
        }
    }

    private static string GetUniquePath(string directory, string baseName, string ext)
    {
        string path = Path.Combine(directory, baseName + ext);
        int counter = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{baseName} ({counter}){ext}");
            counter++;
        }
        return path;
    }
}
