using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RadialConvert.Core.Models;
using SkiaSharp;

namespace RadialConvert.Core.Services;

public class ImageConverter : IFileConverter
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webp", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tiff", ".tif", ".jfif", ".avif"
    };

    private static readonly HashSet<string> SupportedMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac"
    };

    private string? _ffmpegPath;

    public ImageConverter()
    {
        DetectFfmpeg();
    }

    private void DetectFfmpeg()
    {
        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    _ffmpegPath = candidate;
                    return;
                }
            }
            catch { }
        }

        // Check common locations
        string[] common =
        {
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Links\ffmpeg.exe")
        };

        foreach (var c in common)
        {
            if (File.Exists(c))
            {
                _ffmpegPath = c;
                return;
            }
        }
    }

    public bool IsFfmpegAvailable => !string.IsNullOrEmpty(_ffmpegPath) && File.Exists(_ffmpegPath);

    public void SetFfmpegPath(string path)
    {
        if (File.Exists(path))
        {
            _ffmpegPath = path;
        }
    }

    public bool CanHandle(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var ext = Path.GetExtension(filePath);
        if (SupportedImageExtensions.Contains(ext)) return true;
        if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(ext, ".srt", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".vtt", StringComparison.OrdinalIgnoreCase)) return true;
        if (SupportedMediaExtensions.Contains(ext) && IsFfmpegAvailable) return true;
        return false;
    }

    public bool CanHandleAll(IReadOnlyList<string> filePaths)
    {
        if (filePaths == null || filePaths.Count == 0) return false;
        return filePaths.All(CanHandle);
    }

    public IReadOnlyList<ConversionOption> GetOptions(string filePath)
    {
        return GetOptions(new[] { filePath });
    }

    public IReadOnlyList<ConversionOption> GetOptions(IReadOnlyList<string> filePaths)
    {
        var options = new List<ConversionOption>();
        if (filePaths == null || filePaths.Count == 0) return options;

        var suffixes = filePaths.Select(p => Path.GetExtension(p).ToLowerInvariant()).ToHashSet();

        // Case 1: Image files
        if (suffixes.All(s => SupportedImageExtensions.Contains(s)))
        {
            // Open Tangerine image wheel items: PNG, JPEG, WEBP, TIFF, BMP, PDF
            options.Add(new ConversionOption
            {
                Id = "PNG",
                DisplayName = "PNG",
                Description = "Lossless with Alpha",
                TargetExtension = ".png"
            });
            options.Add(new ConversionOption
            {
                Id = "JPEG",
                DisplayName = "JPEG",
                Description = "Compressed Photo",
                TargetExtension = ".jpg"
            });
            options.Add(new ConversionOption
            {
                Id = "WEBP",
                DisplayName = "WEBP",
                Description = "Modern Web Format",
                TargetExtension = ".webp"
            });
            options.Add(new ConversionOption
            {
                Id = "BMP",
                DisplayName = "BMP",
                Description = "Uncompressed Bitmap",
                TargetExtension = ".bmp"
            });
            options.Add(new ConversionOption
            {
                Id = "ICO",
                DisplayName = "ICO",
                Description = "Windows Icon",
                TargetExtension = ".ico"
            });
            options.Add(new ConversionOption
            {
                Id = "PDF",
                DisplayName = "PDF",
                Description = "Image → PDF Document",
                TargetExtension = ".pdf"
            });
        }
        // Case 2: Subtitles (.srt or .vtt)
        else if (suffixes.Contains(".srt") && suffixes.Count == 1)
        {
            options.Add(new ConversionOption
            {
                Id = "VTT",
                DisplayName = "VTT",
                Description = "SRT → WebVTT",
                TargetExtension = ".vtt"
            });
        }
        else if (suffixes.Contains(".vtt") && suffixes.Count == 1)
        {
            options.Add(new ConversionOption
            {
                Id = "SRT",
                DisplayName = "SRT",
                Description = "VTT → SubRip SRT",
                TargetExtension = ".srt"
            });
        }
        // Case 3: Media (Audio / Video)
        else if (suffixes.All(s => SupportedMediaExtensions.Contains(s)) && IsFfmpegAvailable)
        {
            bool isAudioOnly = suffixes.All(s => s is ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac");
            if (!isAudioOnly)
            {
                options.Add(new ConversionOption { Id = "MP4", DisplayName = "MP4", Description = "H.264 Video", TargetExtension = ".mp4" });
                options.Add(new ConversionOption { Id = "WEBM", DisplayName = "WEBM", Description = "VP9 Video", TargetExtension = ".webm" });
            }
            options.Add(new ConversionOption { Id = "MP3", DisplayName = "MP3", Description = "192k Audio", TargetExtension = ".mp3" });
            options.Add(new ConversionOption { Id = "WAV", DisplayName = "WAV", Description = "Uncompressed Audio", TargetExtension = ".wav" });
            options.Add(new ConversionOption { Id = "FLAC", DisplayName = "FLAC", Description = "Lossless Audio", TargetExtension = ".flac" });
            options.Add(new ConversionOption { Id = "OGG", DisplayName = "OGG", Description = "Vorbis Audio", TargetExtension = ".ogg" });
        }

        return options;
    }

    public async Task<IReadOnlyList<string>> ConvertBatchAsync(
        IReadOnlyList<string> sourcePaths,
        string targetOptionId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<string>();

        // Special case: Multiple images to a single combined PDF
        if (targetOptionId.Equals("PDF", StringComparison.OrdinalIgnoreCase) && sourcePaths.Count > 1)
        {
            string combinedPdf = await ConvertImagesToCombinedPdfAsync(sourcePaths, cancellationToken);
            results.Add(combinedPdf);
            return results;
        }

        foreach (var path in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string converted = await ConvertAsync(path, targetOptionId, cancellationToken);
            results.Add(converted);
        }

        return results;
    }

    public Task<string> ConvertAsync(string sourcePath, string targetOptionOrExt, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Source file not found: {sourcePath}");
            }

            var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
            var srcExt = Path.GetExtension(sourcePath).ToLowerInvariant();

            string normalizedTarget = targetOptionOrExt.TrimStart('.').ToLowerInvariant();

            // Subtitle conversion
            if (srcExt == ".srt" && normalizedTarget == "vtt")
            {
                return ConvertSrtToVtt(sourcePath, directory, fileNameWithoutExt);
            }
            if (srcExt == ".vtt" && normalizedTarget == "srt")
            {
                return ConvertVttToSrt(sourcePath, directory, fileNameWithoutExt);
            }

            // Media conversion via FFmpeg
            if (SupportedMediaExtensions.Contains(srcExt) && IsFfmpegAvailable)
            {
                return ConvertMediaWithFfmpeg(sourcePath, directory, fileNameWithoutExt, normalizedTarget);
            }

            // Image to PDF conversion
            if (normalizedTarget == "pdf" && SupportedImageExtensions.Contains(srcExt))
            {
                return ConvertImageToSinglePdf(sourcePath, directory, fileNameWithoutExt);
            }

            // Standard Image Conversion
            string targetExt = "." + normalizedTarget;
            var destPath = GetUniqueDestinationPath(directory, fileNameWithoutExt, targetExt);

            using var inputStream = File.OpenRead(sourcePath);
            using var sourceBitmap = SKBitmap.Decode(inputStream);

            if (sourceBitmap == null)
            {
                throw new InvalidOperationException($"Failed to decode image from: {sourcePath}");
            }

            SKEncodedImageFormat format;
            int quality = 90;

            switch (normalizedTarget)
            {
                case "png":
                    format = SKEncodedImageFormat.Png;
                    quality = 100;
                    break;
                case "jpg":
                case "jpeg":
                    format = SKEncodedImageFormat.Jpeg;
                    quality = 92;
                    break;
                case "webp":
                    format = SKEncodedImageFormat.Webp;
                    quality = 90;
                    break;
                case "bmp":
                    format = SKEncodedImageFormat.Bmp;
                    quality = 100;
                    break;
                case "ico":
                    format = SKEncodedImageFormat.Ico;
                    quality = 100;
                    break;
                default:
                    throw new NotSupportedException($"Target format '{normalizedTarget}' is not supported.");
            }

            // Flatten transparency onto white background for JPEG
            SKBitmap bitmapToEncode = sourceBitmap;
            bool disposeTargetBitmap = false;

            if (format == SKEncodedImageFormat.Jpeg && sourceBitmap.AlphaType != SKAlphaType.Opaque)
            {
                var compositeBmp = new SKBitmap(sourceBitmap.Width, sourceBitmap.Height, SKColorType.Rgb888x, SKAlphaType.Opaque);
                using (var canvas = new SKCanvas(compositeBmp))
                {
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(sourceBitmap, 0, 0, SKSamplingOptions.Default);
                }
                bitmapToEncode = compositeBmp;
                disposeTargetBitmap = true;
            }

            try
            {
                // Resize for ICO format if larger than 256x256
                if (format == SKEncodedImageFormat.Ico && (bitmapToEncode.Width > 256 || bitmapToEncode.Height > 256))
                {
                    var scaledBmp = bitmapToEncode.Resize(new SKImageInfo(256, 256), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    if (scaledBmp != null)
                    {
                        if (disposeTargetBitmap) bitmapToEncode.Dispose();
                        bitmapToEncode = scaledBmp;
                        disposeTargetBitmap = true;
                    }
                }

                using var image = SKImage.FromBitmap(bitmapToEncode);
                using var data = image.Encode(format, quality);

                if (data == null)
                {
                    throw new InvalidOperationException($"Failed to encode image to {format}");
                }

                using var outputStream = File.Create(destPath);
                data.SaveTo(outputStream);
            }
            finally
            {
                if (disposeTargetBitmap)
                {
                    bitmapToEncode.Dispose();
                }
            }

            return destPath;
        }, cancellationToken);
    }

    private string ConvertImageToSinglePdf(string sourceImagePath, string directory, string baseName)
    {
        var destPath = GetUniqueDestinationPath(directory, baseName, ".pdf");

        using var inputStream = File.OpenRead(sourceImagePath);
        using var bitmap = SKBitmap.Decode(inputStream);
        if (bitmap == null) throw new InvalidOperationException($"Failed to decode {sourceImagePath}");

        using var outputStream = File.Create(destPath);
        using var doc = SKDocument.CreatePdf(outputStream);

        using (var canvas = doc.BeginPage(bitmap.Width, bitmap.Height))
        {
            canvas.DrawBitmap(bitmap, 0, 0, SKSamplingOptions.Default);
            doc.EndPage();
        }
        doc.Close();

        return destPath;
    }

    private Task<string> ConvertImagesToCombinedPdfAsync(IReadOnlyList<string> imagePaths, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var firstDir = Path.GetDirectoryName(imagePaths[0]) ?? Environment.CurrentDirectory;
            var baseName = Path.GetFileNameWithoutExtension(imagePaths[0]) + "_combined";
            var destPath = GetUniqueDestinationPath(firstDir, baseName, ".pdf");

            using var outputStream = File.Create(destPath);
            using var doc = SKDocument.CreatePdf(outputStream);

            foreach (var imgPath in imagePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(imgPath)) continue;

                using var inputStream = File.OpenRead(imgPath);
                using var bitmap = SKBitmap.Decode(inputStream);
                if (bitmap == null) continue;

                using (var canvas = doc.BeginPage(bitmap.Width, bitmap.Height))
                {
                    canvas.DrawBitmap(bitmap, 0, 0, SKSamplingOptions.Default);
                    doc.EndPage();
                }
            }
            doc.Close();

            return destPath;
        }, cancellationToken);
    }

    private string ConvertSrtToVtt(string srtPath, string directory, string baseName)
    {
        var destPath = GetUniqueDestinationPath(directory, baseName, ".vtt");
        var lines = File.ReadAllLines(srtPath);
        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();

        foreach (var line in lines)
        {
            // Replace timecode comma with dot: 00:01:20,000 --> 00:01:20.000
            if (line.Contains("-->"))
            {
                sb.AppendLine(line.Replace(',', '.'));
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        File.WriteAllText(destPath, sb.ToString(), Encoding.UTF8);
        return destPath;
    }

    private string ConvertVttToSrt(string vttPath, string directory, string baseName)
    {
        var destPath = GetUniqueDestinationPath(directory, baseName, ".srt");
        var lines = File.ReadAllLines(vttPath);
        var sb = new StringBuilder();
        int counter = 1;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.Equals(line, "WEBVTT", StringComparison.OrdinalIgnoreCase)) continue;

            if (line.Contains("-->"))
            {
                sb.AppendLine(counter.ToString());
                counter++;
                // Convert dot back to comma: 00:01:20.000 --> 00:01:20,000
                var fixedTime = Regex.Replace(line, @"(\d{2}:\d{2}:\d{2})\.(\d{3})", "$1,$2");
                sb.AppendLine(fixedTime);
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        File.WriteAllText(destPath, sb.ToString(), Encoding.UTF8);
        return destPath;
    }

    private string ConvertMediaWithFfmpeg(string srcPath, string directory, string baseName, string targetExt)
    {
        if (string.IsNullOrEmpty(_ffmpegPath) || !File.Exists(_ffmpegPath))
        {
            throw new InvalidOperationException("FFmpeg is not installed or configured.");
        }

        var destPath = GetUniqueDestinationPath(directory, baseName, "." + targetExt);

        var args = targetExt.ToLowerInvariant() switch
        {
            "mp4" => $"-y -i \"{srcPath}\" -c:v libx264 -preset fast -crf 23 -c:a aac -movflags +faststart \"{destPath}\"",
            "webm" => $"-y -i \"{srcPath}\" -c:v libvpx-vp9 -crf 30 -b:v 0 -c:a libopus \"{destPath}\"",
            "mp3" => $"-y -i \"{srcPath}\" -vn -c:a libmp3lame -b:a 192k \"{destPath}\"",
            "wav" => $"-y -i \"{srcPath}\" -vn -c:a pcm_s16le \"{destPath}\"",
            "flac" => $"-y -i \"{srcPath}\" -vn -c:a flac \"{destPath}\"",
            "ogg" => $"-y -i \"{srcPath}\" -vn -c:a libvorbis \"{destPath}\"",
            _ => $"-y -i \"{srcPath}\" \"{destPath}\""
        };

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        if (proc != null)
        {
            proc.WaitForExit(120_000); // 2 minute timeout
            if (proc.ExitCode != 0)
            {
                string err = proc.StandardError.ReadToEnd();
                throw new InvalidOperationException($"FFmpeg conversion failed: {err}");
            }
        }

        return destPath;
    }

    public static string GetUniqueDestinationPath(string directory, string baseFileName, string extension)
    {
        var candidate = Path.Combine(directory, $"{baseFileName}{extension}");
        if (!File.Exists(candidate)) return candidate;

        int counter = 1;
        while (true)
        {
            candidate = Path.Combine(directory, $"{baseFileName} ({counter}){extension}");
            if (!File.Exists(candidate)) return candidate;
            counter++;
        }
    }
}
