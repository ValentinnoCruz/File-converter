using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RadialConvert.Core.Services;
using RadialConvert.UI;
using SkiaSharp;

namespace RadialConvert.Tests;

[TestClass]
public class ImageConverterTests
{
    private string _testDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "QuickConvert_Tests_" + Guid.NewGuid().ToString("N"));
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

        Assert.IsFalse(converter.CanHandle("document.pdf"));
        Assert.IsFalse(converter.CanHandle("archive.zip"));
        Assert.IsFalse(converter.CanHandle("code.cs"));
        Assert.IsFalse(converter.CanHandle(""));
    }

    [TestMethod]
    public void GetOptions_ForWebP_ContainsPngAndJpeg()
    {
        var converter = new ImageConverter();
        var options = converter.GetOptions("test.webp");

        Assert.IsNotNull(options);
        Assert.IsTrue(options.Count >= 2);

        var pngOption = options.FirstOrDefault(o => o.Id == "PNG");
        var jpegOption = options.FirstOrDefault(o => o.Id == "JPEG");

        Assert.IsNotNull(pngOption, "Options for .webp must include PNG");
        Assert.AreEqual(".png", pngOption.TargetExtension);

        Assert.IsNotNull(jpegOption, "Options for .webp must include JPEG");
        Assert.AreEqual(".jpg", jpegOption.TargetExtension);
    }

    [TestMethod]
    public async Task ConvertAsync_WebPToPng_CreatesValidPngFileInSameDirectory()
    {
        var converter = new ImageConverter();
        string sourceWebp = CreateSampleWebpFile("icon.webp");

        string createdPng = await converter.ConvertAsync(sourceWebp, ".png");

        Assert.IsTrue(File.Exists(createdPng), "Converted PNG file should exist on disk");
        Assert.AreEqual(Path.GetDirectoryName(sourceWebp), Path.GetDirectoryName(createdPng), "Must be saved in the same directory");
        Assert.AreEqual(".png", Path.GetExtension(createdPng).ToLowerInvariant());

        // Verify it decodes correctly as an image
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

        string createdJpeg = await converter.ConvertAsync(sourceWebp, ".jpg");

        Assert.IsTrue(File.Exists(createdJpeg), "Converted JPEG file should exist on disk");
        Assert.AreEqual(Path.GetDirectoryName(sourceWebp), Path.GetDirectoryName(createdJpeg), "Must be saved in the same directory");
        Assert.AreEqual(".jpg", Path.GetExtension(createdJpeg).ToLowerInvariant());

        using var decoded = SKBitmap.Decode(createdJpeg);
        Assert.IsNotNull(decoded, "Resulting file must decode as a valid image");
        Assert.AreEqual(64, decoded.Width);
        Assert.AreEqual(64, decoded.Height);
    }

    [TestMethod]
    public async Task ConvertAsync_DuplicateNameHandling_AppendsIndexNumber()
    {
        var converter = new ImageConverter();
        string sourceWebp = CreateSampleWebpFile("picture.webp");

        // First conversion -> picture.png
        string firstPng = await converter.ConvertAsync(sourceWebp, ".png");
        Assert.IsTrue(File.Exists(firstPng));
        Assert.AreEqual("picture.png", Path.GetFileName(firstPng));

        // Second conversion -> picture (1).png
        string secondPng = await converter.ConvertAsync(sourceWebp, ".png");
        Assert.IsTrue(File.Exists(secondPng));
        Assert.AreEqual("picture (1).png", Path.GetFileName(secondPng));

        // Third conversion -> picture (2).png
        string thirdPng = await converter.ConvertAsync(sourceWebp, ".png");
        Assert.IsTrue(File.Exists(thirdPng));
        Assert.AreEqual("picture (2).png", Path.GetFileName(thirdPng));
    }

    [TestMethod]
    public void RadialGeometryHelper_CreatesValidPieSliceGeometry()
    {
        var center = new System.Windows.Point(180, 180);
        var geom = RadialGeometryHelper.CreatePieSliceGeometry(center, 40, 130, 0, 90);

        Assert.IsNotNull(geom);
        Assert.AreEqual(1, geom.Figures.Count);
        Assert.IsTrue(geom.Figures[0].IsClosed);

        var labelPos = RadialGeometryHelper.GetLabelPosition(center, 85, 0, 90);
        Assert.IsTrue(labelPos.X > 180, "45-degree angle should place X to the right of center");
        Assert.IsTrue(labelPos.Y < 180, "45-degree angle from 12 o'clock should place Y above or in top quadrant");
    }
}
