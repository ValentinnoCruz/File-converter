using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RadialConvert.Core.Services;
using RadialConvert.UI;
using SkiaSharp;
using Point = System.Windows.Point;

namespace RadialConvert.Tests;

[TestClass]
public class ImageConverterTests
{
    private string _testDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "OpenTangerine_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, true);
            }
            catch { }
        }
    }

    private string CreateSampleWebpFile(string fileName = "sample.webp")
    {
        string filePath = Path.Combine(_testDir, fileName);
        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint
        {
            Color = SKColors.Crimson,
            IsAntialias = true
        };
        canvas.DrawCircle(32, 32, 28, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 90);
        using var fs = File.Create(filePath);
        data.SaveTo(fs);

        return filePath;
    }

    [TestMethod]
    public void CanHandle_SupportedAndUnsupportedExtensions()
    {
        var converter = new ImageConverter();

        Assert.IsTrue(converter.CanHandle("photo.webp"));
        Assert.IsTrue(converter.CanHandle("IMAGE.PNG"));
        Assert.IsTrue(converter.CanHandle("test.jpg"));
        Assert.IsTrue(converter.CanHandle("test.jpeg"));
        Assert.IsTrue(converter.CanHandle("test.bmp"));
        Assert.IsTrue(converter.CanHandle("test.gif"));
        Assert.IsTrue(converter.CanHandle("sub.srt"));
        Assert.IsTrue(converter.CanHandle("sub.vtt"));
        Assert.IsTrue(converter.CanHandle("notes.one"));

        Assert.IsFalse(converter.CanHandle("archive.zip"));
        Assert.IsFalse(converter.CanHandle(""));
    }

    [TestMethod]
    public void GetOptions_ForWebP_ContainsTangerineStandardFormats()
    {
        var converter = new ImageConverter();
        var options = converter.GetOptions(new[] { "test.webp" });

        Assert.IsNotNull(options);
        Assert.IsTrue(options.Count >= 5);

        Assert.IsTrue(options.Any(o => o.Id == "PNG"), "Must include PNG");
        Assert.IsTrue(options.Any(o => o.Id == "JPEG"), "Must include JPEG");
        Assert.IsTrue(options.Any(o => o.Id == "WEBP"), "Must include WEBP");
        Assert.IsTrue(options.Any(o => o.Id == "BMP"), "Must include BMP");
        Assert.IsTrue(options.Any(o => o.Id == "PDF"), "Must include Image to PDF");
    }

    [TestMethod]
    public async Task ConvertAsync_WebPToPng_CreatesValidPngFileInSameDirectory()
    {
        var converter = new ImageConverter();
        string sourceWebp = CreateSampleWebpFile("icon.webp");

        string createdPng = await converter.ConvertAsync(sourceWebp, "png");

        Assert.IsTrue(File.Exists(createdPng), "Converted PNG file should exist on disk");
        Assert.AreEqual(Path.GetDirectoryName(sourceWebp), Path.GetDirectoryName(createdPng), "Must be saved in the same directory");
        Assert.AreEqual(".png", Path.GetExtension(createdPng).ToLowerInvariant());

        using var decoded = SKBitmap.Decode(createdPng);
        Assert.IsNotNull(decoded, "Resulting file must decode as a valid image");
        Assert.AreEqual(64, decoded.Width);
        Assert.AreEqual(64, decoded.Height);
    }

    [TestMethod]
    public async Task ConvertAsync_WebPToJpeg_CreatesValidJpegFileInSameDirectory()
    {
        var converter = new ImageConverter();
        string sourceWebp = CreateSampleWebpFile("landscape.webp");

        string createdJpeg = await converter.ConvertAsync(sourceWebp, "jpg");

        Assert.IsTrue(File.Exists(createdJpeg), "Converted JPEG file should exist on disk");
        Assert.AreEqual(Path.GetDirectoryName(sourceWebp), Path.GetDirectoryName(createdJpeg), "Must be saved in the same directory");
        Assert.AreEqual(".jpg", Path.GetExtension(createdJpeg).ToLowerInvariant());

        using var decoded = SKBitmap.Decode(createdJpeg);
        Assert.IsNotNull(decoded, "Resulting file must decode as a valid image");
        Assert.AreEqual(64, decoded.Width);
        Assert.AreEqual(64, decoded.Height);
    }

    [TestMethod]
    public async Task ConvertAsync_WebPToPdf_CreatesValidPdf()
    {
        var converter = new ImageConverter();
        string sourceWebp = CreateSampleWebpFile("document_scan.webp");

        string createdPdf = await converter.ConvertAsync(sourceWebp, "pdf");

        Assert.IsTrue(File.Exists(createdPdf));
        Assert.AreEqual(".pdf", Path.GetExtension(createdPdf).ToLowerInvariant());

        var bytes = File.ReadAllBytes(createdPdf);
        string header = System.Text.Encoding.ASCII.GetString(bytes.Take(5).ToArray());
        Assert.AreEqual("%PDF-", header);
    }

    [TestMethod]
    public async Task ConvertBatchAsync_MultipleImages_CreatesAllOutputs()
    {
        var converter = new ImageConverter();
        string img1 = CreateSampleWebpFile("batch1.webp");
        string img2 = CreateSampleWebpFile("batch2.webp");
        string img3 = CreateSampleWebpFile("batch3.webp");

        var outputs = await converter.ConvertBatchAsync(new[] { img1, img2, img3 }, "PNG");

        Assert.AreEqual(3, outputs.Count);
        foreach (var outPath in outputs)
        {
            Assert.IsTrue(File.Exists(outPath));
            Assert.AreEqual(".png", Path.GetExtension(outPath));
        }
    }

    [TestMethod]
    public async Task ConvertAsync_DuplicateNameHandling_AppendsIndexNumber()
    {
        var converter = new ImageConverter();
        string sourceWebp = CreateSampleWebpFile("picture.webp");

        string firstPng = await converter.ConvertAsync(sourceWebp, "png");
        Assert.IsTrue(File.Exists(firstPng));
        Assert.AreEqual("picture.png", Path.GetFileName(firstPng));

        string secondPng = await converter.ConvertAsync(sourceWebp, "png");
        Assert.IsTrue(File.Exists(secondPng));
        Assert.AreEqual("picture (1).png", Path.GetFileName(secondPng));

        string thirdPng = await converter.ConvertAsync(sourceWebp, "png");
        Assert.IsTrue(File.Exists(thirdPng));
        Assert.AreEqual("picture (2).png", Path.GetFileName(thirdPng));
    }

    [TestMethod]
    public async Task ConvertAsync_SrtToVtt_ProducesValidWebVtt()
    {
        var converter = new ImageConverter();
        string srtPath = Path.Combine(_testDir, "test.srt");
        File.WriteAllText(srtPath, "1\n00:00:01,000 --> 00:00:03,000\nHello World\n");

        string vttPath = await converter.ConvertAsync(srtPath, "vtt");
        Assert.IsTrue(File.Exists(vttPath));
        string content = File.ReadAllText(vttPath);
        Assert.IsTrue(content.StartsWith("WEBVTT"));
        Assert.IsTrue(content.Contains("00:00:01.000 --> 00:00:03.000"));
    }

    [TestMethod]
    public void RadialGeometryHelper_CreatesRoundedGlassSegment()
    {
        var center = new Point(190, 190);
        var geom = RadialGeometryHelper.CreateRoundedGlassSegmentGeometry(center, 0, 6);

        Assert.IsNotNull(geom);
        Assert.AreEqual(1, geom.Figures.Count);
        Assert.IsTrue(geom.Figures[0].IsClosed);

        // Test label position at 12 o'clock for index 0
        var labelPos = RadialGeometryHelper.GetLabelPosition(center, 0, 6, radius: 120);
        Assert.AreEqual(190, labelPos.X, 0.001);
        Assert.IsTrue(labelPos.Y < 190, "12 o'clock should place Y above center");

        // Sector hit-test: point above center (0 deg) should return sector 0
        var topPoint = new Point(190, 70); // radius 120 above center
        int sector = RadialGeometryHelper.GetSectorIndex(topPoint, center, 6);
        Assert.AreEqual(0, sector);

        // Center hub hit-test: point inside inner radius should return -1
        var hubPoint = new Point(190, 190);
        int hubSector = RadialGeometryHelper.GetSectorIndex(hubPoint, center, 6);
        Assert.AreEqual(-1, hubSector);
    }

    [TestMethod]
    public void Test_HookLifecycle()
    {
        const string mutexName = "Global\\OpenTangerine_SingleInstance_Mutex";
        try
        {
            var m = new Mutex(true, mutexName, out bool createdNew);
            Console.WriteLine($"createdNew: {createdNew}");
            if (!createdNew)
            {
                try
                {
                    bool acquired = m.WaitOne(0);
                    Console.WriteLine($"WaitOne(0): {acquired}");
                }
                catch (AbandonedMutexException)
                {
                    Console.WriteLine("Mutex was abandoned by killed process!");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Mutex exception: {ex.Message}");
        }
    }

    [TestMethod]
    public void OneNote_CanHandleAndGetOptions()
    {
        var converter = new ImageConverter();
        Assert.IsTrue(converter.CanHandle("meeting.one"));

        var options = converter.GetOptions(new[] { "meeting.one" });
        Assert.IsNotNull(options);
        Assert.IsTrue(options.Any(o => o.Id == "PDF"));
        Assert.IsTrue(options.Any(o => o.Id == "DOCX"));
        Assert.IsTrue(options.Any(o => o.Id == "XPS"));
    }

    [TestMethod]
    public async Task OneNote_ConvertAsync_BulkPostTcToPdfAndDocx()
    {
        string samplePath = @"C:\Users\Didis\Downloads\Bulk POST TC's via CSV.one";
        if (!File.Exists(samplePath))
        {
            Assert.Inconclusive("Sample OneNote test file not found in Downloads.");
            return;
        }

        // Copy to isolated test directory
        string copiedPath = Path.Combine(_testDir, "Bulk POST TC's via CSV.one");
        File.Copy(samplePath, copiedPath);

        var converter = new ImageConverter();

        // 1. Convert to PDF
        string createdPdf = await converter.ConvertAsync(copiedPath, "pdf");
        Assert.IsTrue(File.Exists(createdPdf), "PDF should be generated");
        Assert.AreEqual(".pdf", Path.GetExtension(createdPdf).ToLowerInvariant());
        var pdfBytes = File.ReadAllBytes(createdPdf);
        Assert.IsTrue(pdfBytes.Length > 100_000, "PDF should have content (>100KB)");
        string pdfHeader = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
        Assert.AreEqual("%PDF-", pdfHeader);

        // 2. Convert to DOCX
        string createdDocx = await converter.ConvertAsync(copiedPath, "docx");
        Assert.IsTrue(File.Exists(createdDocx), "DOCX should be generated");
        Assert.AreEqual(".docx", Path.GetExtension(createdDocx).ToLowerInvariant());
        var docxBytes = File.ReadAllBytes(createdDocx);
        Assert.IsTrue(docxBytes.Length > 50_000, "DOCX should have content (>50KB)");
        string docxHeader = System.Text.Encoding.ASCII.GetString(docxBytes.Take(2).ToArray());
        Assert.AreEqual("PK", docxHeader, "DOCX file must be a valid Office Open XML ZIP package");
    }
}
