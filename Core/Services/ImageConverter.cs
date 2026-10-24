using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RadialConvert.Core.Models;
using SkiaSharp;

namespace RadialConvert.Core.Services;

public class ImageConverter : IFileConverter
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webp", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tiff", ".jfif", ".avif"
    };

    public bool CanHandle(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    public IReadOnlyList<ConversionOption> GetOptions(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var options = new List<ConversionOption>();

        if (ext == ".webp")
        {
            options.Add(new ConversionOption
            {
                Id = "PNG",
                DisplayName = "PNG",
                Description = "Lossless with Alpha",
                TargetExtension = ".png",
                AccentColor = "#3B82F6" // Vibrant Blue
            });
            options.Add(new ConversionOption
            {
                Id = "JPEG",
                DisplayName = "JPEG",
                Description = "Compressed Photo",
                TargetExtension = ".jpg",
                AccentColor = "#10B981" // Vibrant Green
            });
            options.Add(new ConversionOption
            {
                Id = "BMP",
                DisplayName = "BMP",
                Description = "Uncompressed Bitmap",
                TargetExtension = ".bmp",
                AccentColor = "#8B5CF6" // Purple
            });
            options.Add(new ConversionOption
            {
                Id = "ICO",
                DisplayName = "ICO",
                Description = "Windows Icon",
                TargetExtension = ".ico",
                AccentColor = "#F59E0B" // Amber
            });
        }
        else if (ext is ".jpg" or ".jpeg")
        {
            options.Add(new ConversionOption
            {
                Id = "PNG",
                DisplayName = "PNG",
                Description = "Lossless with Alpha",
                TargetExtension = ".png",
                AccentColor = "#3B82F6"
            });
            options.Add(new ConversionOption
            {
                Id = "WEBP",
                DisplayName = "WEBP",
                Description = "Modern Web Format",
                TargetExtension = ".webp",
                AccentColor = "#06B6D4" // Cyan
            });
            options.Add(new ConversionOption
            {
                Id = "BMP",
                DisplayName = "BMP",
                Description = "Uncompressed Bitmap",
                TargetExtension = ".bmp",
                AccentColor = "#8B5CF6"
            });
            options.Add(new ConversionOption
            {
                Id = "ICO",
                DisplayName = "ICO",
                Description = "Windows Icon",
                TargetExtension = ".ico",
                AccentColor = "#F59E0B"
            });
        }
        else if (ext == ".png")
        {
            options.Add(new ConversionOption
            {
                Id = "JPEG",
                DisplayName = "JPEG",
                Description = "Compressed Photo",
                TargetExtension = ".jpg",
                AccentColor = "#10B981"
            });
            options.Add(new ConversionOption
            {
                Id = "WEBP",
                DisplayName = "WEBP",
                Description = "Modern Web Format",
                TargetExtension = ".webp",
                AccentColor = "#06B6D4"
            });
            options.Add(new ConversionOption
            {
                Id = "ICO",
                DisplayName = "ICO",
                Description = "Windows Icon",
                TargetExtension = ".ico",
                AccentColor = "#F59E0B"
            });
            options.Add(new ConversionOption
            {
                Id = "BMP",
                DisplayName = "BMP",
                Description = "Uncompressed Bitmap",
                TargetExtension = ".bmp",
                AccentColor = "#8B5CF6"
            });
        }
        else
        {
            // Fallback for other formats (bmp, gif, tiff, etc.)
            options.Add(new ConversionOption
            {
                Id = "PNG",
                DisplayName = "PNG",
                Description = "Lossless with Alpha",
                TargetExtension = ".png",
                AccentColor = "#3B82F6"
            });
            options.Add(new ConversionOption
            {
                Id = "JPEG",
                DisplayName = "JPEG",
                Description = "Compressed Photo",
                TargetExtension = ".jpg",
                AccentColor = "#10B981"
            });
            options.Add(new ConversionOption
            {
                Id = "WEBP",
                DisplayName = "WEBP",
                Description = "Modern Web Format",
                TargetExtension = ".webp",
                AccentColor = "#06B6D4"
            });
        }

        return options;
    }

    public Task<string> ConvertAsync(string sourcePath, string targetExtension, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Source file not found: {sourcePath}");
            }

            var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
            var normalizedTargetExt = targetExtension.StartsWith('.') ? targetExtension : "." + targetExtension;

            // Generate unique destination path
            var destPath = GetUniqueDestinationPath(directory, fileNameWithoutExt, normalizedTargetExt);

            using var inputStream = File.OpenRead(sourcePath);
            using var sourceBitmap = SKBitmap.Decode(inputStream);

            if (sourceBitmap == null)
            {
                throw new InvalidOperationException($"Failed to decode image from: {sourcePath}");
            }

            SKEncodedImageFormat format;
            int quality = 90;

            switch (normalizedTargetExt.ToLowerInvariant())
            {
                case ".png":
                    format = SKEncodedImageFormat.Png;
                    quality = 100;
                    break;
                case ".jpg":
                case ".jpeg":
                    format = SKEncodedImageFormat.Jpeg;
                    quality = 92;
                    break;
                case ".webp":
                    format = SKEncodedImageFormat.Webp;
                    quality = 90;
                    break;
                case ".bmp":
                    format = SKEncodedImageFormat.Bmp;
                    quality = 100;
                    break;
                case ".gif":
                    format = SKEncodedImageFormat.Gif;
                    quality = 90;
                    break;
                case ".ico":
                    format = SKEncodedImageFormat.Ico;
                    quality = 100;
                    break;
                default:
                    throw new NotSupportedException($"Target format '{normalizedTargetExt}' is not supported.");
            }

            // Handle transparency when exporting to JPEG (composite onto white background)
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
                // Special handling for ICO (Windows Icon): if image is larger than 256x256, scale to 256x256 max
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

    private static string GetUniqueDestinationPath(string directory, string baseFileName, string extension)
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
