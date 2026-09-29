using System;
using System.IO;
using Masuit.Tools.Media;
using SkiaSharp;
using Xunit;

namespace Masuit.Tools.Abstractions.Test.Media;

public class ImageWatermarkerTests
{
    private static void SaveBitmap(SKBitmap bitmap, string path)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
    }

    [Fact]
    public void AddWatermark_ShouldAddImageWatermark()
    {
        var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "test4.jpg");
        using var image = new SKBitmap(100, 100);
        SaveBitmap(image, imagePath);

        var watermarkPath = Path.Combine(Directory.GetCurrentDirectory(), "watermark.png");
        using var watermark = new SKBitmap(20, 20);
        using (var data = watermark.Encode(SKEncodedImageFormat.Png, 90))
        using (var fs = File.OpenWrite(watermarkPath)) data.SaveTo(fs);

        using var imageStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
        using var watermarkStream = new FileStream(watermarkPath, FileMode.Open, FileAccess.Read);
        var watermarker = new ImageWatermarker(imageStream);
        var resultStream = watermarker.AddWatermark(watermarkStream);

        Assert.NotNull(resultStream);
        Assert.True(resultStream.Length > 0);
        try { File.Delete(imagePath); File.Delete(watermarkPath); } catch { }
    }

    [Fact]
    public void AddWatermark_ShouldAddImageWatermarkWithSkipSmallImages()
    {
        var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "test5.jpg");
        using var image = new SKBitmap(50, 50);
        SaveBitmap(image, imagePath);

        var watermarkPath = Path.Combine(Directory.GetCurrentDirectory(), "watermark2.png");
        using var watermark = new SKBitmap(20, 20);
        using (var data = watermark.Encode(SKEncodedImageFormat.Png, 90))
        using (var fs = File.OpenWrite(watermarkPath)) data.SaveTo(fs);

        using var imageStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
        using var watermarkStream = new FileStream(watermarkPath, FileMode.Open, FileAccess.Read);
        var encoder = new SkiaEncoder { Format = SKEncodedImageFormat.Jpeg, Quality = 90 };
        var watermarker = new ImageWatermarker(imageStream, encoder, true, 10000);
        var resultStream = watermarker.AddWatermark(watermarkStream);

        Assert.NotNull(resultStream);
        Assert.True(resultStream.Length > 0);
        try { File.Delete(imagePath); File.Delete(watermarkPath); } catch { }
    }
}
