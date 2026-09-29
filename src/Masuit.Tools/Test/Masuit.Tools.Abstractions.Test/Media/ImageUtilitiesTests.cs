using System.IO;
using Masuit.Tools.Media;
using SkiaSharp;
using Xunit;

namespace Masuit.Tools.Abstractions.Test.Media;

public class ImageUtilitiesTests
{
    [Theory]
    [InlineData("image/pjpeg", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/gif", true)]
    [InlineData("image/bmpp", true)]
    [InlineData("image/png", true)]
    [InlineData("image/tiff", false)]
    public void IsWebImage_ShouldReturnCorrectResult(string contentType, bool expected)
    {
        var result = ImageUtilities.IsWebImage(contentType);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CutImage_ShouldReturnCroppedImage()
    {
        using var image = new SKBitmap(100, 100);
        var rect = new SKRectI(10, 10, 60, 60);
        using var croppedImage = image.CutImage(rect);

        Assert.Equal(50, croppedImage.Width);
        Assert.Equal(50, croppedImage.Height);
    }

    [Fact]
    public void CutAndResize_ShouldReturnCroppedAndResizedImage()
    {
        using var image = new SKBitmap(100, 100);
        var rect = new SKRectI(10, 10, 60, 60);
        using var resizedImage = image.CutAndResize(rect, 25, 25);

        Assert.Equal(25, resizedImage.Width);
        Assert.Equal(25, resizedImage.Height);
    }

    [Fact]
    public void MakeThumbnail_ShouldCreateThumbnail()
    {
        using var image = new SKBitmap(100, 100);
        var thumbnailPath = Path.Combine(Directory.GetCurrentDirectory(), "thumbnail.jpg");
        image.MakeThumbnail(thumbnailPath, 50, 50, ResizeMode.Crop);

        Assert.True(File.Exists(thumbnailPath));
        using var thumbnail = SKBitmap.Decode(thumbnailPath);
        Assert.Equal(50, thumbnail.Width);
        Assert.Equal(50, thumbnail.Height);
        File.Delete(thumbnailPath);
    }

    [Fact]
    public void MakeThumbnail_ShouldReturnThumbnailImage()
    {
        using var image = new SKBitmap(100, 100);
        using var thumbnail = image.MakeThumbnail(50, 50, ResizeMode.Crop);

        Assert.Equal(50, thumbnail.Width);
        Assert.Equal(50, thumbnail.Height);
    }

    [Fact]
    public void LDPic_ShouldAdjustBrightness()
    {
        using var image = new SKBitmap(100, 100);
        using var adjustedImage = image.LDPic(50);

        Assert.NotEqual(image, adjustedImage);
    }

    [Fact]
    public void RePic_ShouldReturnInvertedImage()
    {
        using var image = new SKBitmap(100, 100);
        using var invertedImage = image.RePic();

        Assert.NotEqual(image, invertedImage);
    }

    [Fact]
    public void Relief_ShouldReturnReliefImage()
    {
        using var image = new SKBitmap(100, 100);
        using var reliefImage = image.Relief();

        Assert.NotEqual(image, reliefImage);
    }

    [Fact]
    public void ResizeImage_ShouldReturnResizedImage()
    {
        using var image = new SKBitmap(100, 100);
        using var resizedImage = image.ResizeImage(50, 50);

        Assert.Equal(50, resizedImage.Width);
        Assert.Equal(50, resizedImage.Height);
    }
}
