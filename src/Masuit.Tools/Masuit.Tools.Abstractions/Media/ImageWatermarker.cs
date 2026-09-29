using Masuit.Tools.Systems;
using SkiaSharp;
using System;
using System.IO;

namespace Masuit.Tools.Media
{
    /// <summary>
    /// SkiaSharp 图像编码器配置
    /// </summary>
    public class SkiaEncoder
    {
        public SKEncodedImageFormat Format { get; set; } = SKEncodedImageFormat.Jpeg;
        public int Quality { get; set; } = 90;
    }

    public class ImageWatermarker
    {
        public bool SkipWatermarkForSmallImages { get; set; }
        public int SmallImagePixelsThreshold { get; set; }
        public SkiaEncoder ImageEncoder { get; set; }

        private readonly Stream _stream;

        public ImageWatermarker(Stream originStream)
        {
            _stream = originStream;
        }

        public ImageWatermarker(Stream originStream, SkiaEncoder encoder) : this(originStream)
        {
            ImageEncoder = encoder;
        }

        public ImageWatermarker(Stream originStream, SkiaEncoder encoder, bool skipWatermarkForSmallImages, int smallImagePixelsThreshold) : this(originStream, encoder)
        {
            SkipWatermarkForSmallImages = skipWatermarkForSmallImages;
            SmallImagePixelsThreshold = smallImagePixelsThreshold;
        }

        /// <summary>
        /// 添加文字水印（通过字体文件路径）
        /// </summary>
        public PooledMemoryStream AddWatermark(string watermarkText, string ttfFontPath, int fontSize, SKColor color, WatermarkPosition watermarkPosition = WatermarkPosition.BottomRight, int textPadding = 10)
        {
            using var typeface = SKTypeface.FromFile(ttfFontPath);
            return AddWatermark(watermarkText, typeface, fontSize, color, watermarkPosition, textPadding);
        }

        /// <summary>
        /// 添加文字水印（通过 SKTypeface）
        /// </summary>
        public PooledMemoryStream AddWatermark(string watermarkText, SKTypeface typeface, float fontSize, SKColor color, WatermarkPosition watermarkPosition = WatermarkPosition.BottomRight, int textPadding = 10)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            var detectedFormat = _stream.GetImageType();
            _stream.Seek(0, SeekOrigin.Begin);
            using var managedStream = new SKManagedStream(_stream);
            using var img = SKBitmap.Decode(managedStream);

            using var font = new SKFont(typeface, fontSize);
            using var paint = new SKPaint();
            paint.Color = color;
            paint.IsAntialias = true;

            float textWidth = font.MeasureText(watermarkText);
            var metrics = font.Metrics;
            float textHeight = Math.Abs(metrics.Ascent) + Math.Abs(metrics.Descent);

            if (SkipWatermarkForSmallImages && (img.Height < Math.Sqrt(SmallImagePixelsThreshold) || img.Width < Math.Sqrt(SmallImagePixelsThreshold) || img.Width <= textWidth))
            {
                return SaveBitmap(img, detectedFormat);
            }

            float ascent = Math.Abs(metrics.Ascent);
            float x, y;
            switch (watermarkPosition)
            {
                case WatermarkPosition.TopRight:
                    x = img.Width - textWidth - textPadding;
                    y = textPadding + ascent;
                    break;
                case WatermarkPosition.BottomLeft:
                    x = textPadding;
                    y = img.Height - textHeight - textPadding + ascent;
                    break;
                case WatermarkPosition.BottomRight:
                    x = img.Width - textWidth - textPadding;
                    y = img.Height - textHeight - textPadding + ascent;
                    break;
                case WatermarkPosition.Center:
                    x = (img.Width - textWidth) / 2;
                    y = (img.Height - textHeight) / 2 + ascent;
                    break;
                default:
                    x = textPadding;
                    y = textPadding + ascent;
                    break;
            }

            using var canvas = new SKCanvas(img);
            using (var blob = SKTextBlob.Create(watermarkText, font))
            {
                if (blob != null)
                {
                    canvas.DrawText(blob, x, y, paint);
                }
            }

            return SaveBitmap(img, detectedFormat);
        }

        /// <summary>
        /// 添加图片水印
        /// </summary>
        public PooledMemoryStream AddWatermark(Stream watermarkImage, float opacity = 1f, WatermarkPosition watermarkPosition = WatermarkPosition.BottomRight, int padding = 20)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            var detectedFormat = _stream.GetImageType();
            _stream.Seek(0, SeekOrigin.Begin);
            using var managedStream = new SKManagedStream(_stream);
            using var img = SKBitmap.Decode(managedStream);
            int height = img.Height;
            int width = img.Width;

            if (SkipWatermarkForSmallImages && (height < Math.Sqrt(SmallImagePixelsThreshold) || width < Math.Sqrt(SmallImagePixelsThreshold)))
            {
                return SaveBitmap(img, detectedFormat);
            }

            using var watermark = SKBitmap.Decode(watermarkImage);
            int wmMaxW = Math.Max(width / 10, 40);
            int wmMaxH = Math.Max(height / 10, 40);
            float scale = Math.Min((float)wmMaxW / watermark.Width, (float)wmMaxH / watermark.Height);
            int wmW = (int)(watermark.Width * scale);
            int wmH = (int)(watermark.Height * scale);
            using var scaledWatermark = watermark.Resize(new SKImageInfo(wmW, wmH), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            padding += (width - 1000) / 100;
            int x, y;
            switch (watermarkPosition)
            {
                case WatermarkPosition.TopRight:
                    x = width - wmW - padding;
                    y = padding;
                    break;
                case WatermarkPosition.BottomLeft:
                    x = padding;
                    y = height - wmH - padding;
                    break;
                case WatermarkPosition.BottomRight:
                    x = width - wmW - padding;
                    y = height - wmH - padding;
                    break;
                case WatermarkPosition.Center:
                    x = (width - wmW) / 2;
                    y = (height - wmH) / 2;
                    break;
                default:
                    x = padding;
                    y = padding;
                    break;
            }

            using var canvas = new SKCanvas(img);
            using var paint = new SKPaint();
            paint.Color = SKColors.White.WithAlpha((byte)(opacity * 255));
            canvas.DrawBitmap(scaledWatermark, x, y,new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.Linear), paint);

            return SaveBitmap(img, detectedFormat);
        }

        private PooledMemoryStream SaveBitmap(SKBitmap bitmap, ImageFormat? detectedFormat)
        {
            var ms = new PooledMemoryStream();
            SKEncodedImageFormat format;
            int quality;
            if (ImageEncoder != null)
            {
                format = ImageEncoder.Format;
                quality = ImageEncoder.Quality;
            }
            else
            {
                format = MapFormat(detectedFormat);
                quality = 90;
            }

            using var data = bitmap.Encode(format, quality);
            data.SaveTo(ms);
            ms.Position = 0;
            _stream.Position = 0;
            return ms;
        }

        private static SKEncodedImageFormat MapFormat(ImageFormat? format)
        {
            return format switch
            {
                ImageFormat.Jpg => SKEncodedImageFormat.Jpeg,
                ImageFormat.Png => SKEncodedImageFormat.Png,
                ImageFormat.Gif => SKEncodedImageFormat.Gif,
                ImageFormat.Bmp => SKEncodedImageFormat.Bmp,
                ImageFormat.WebP => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Png,
            };
        }

        public static ImageWatermarker FromStream(Stream stream)
        {
            return new ImageWatermarker(stream);
        }
    }
}
