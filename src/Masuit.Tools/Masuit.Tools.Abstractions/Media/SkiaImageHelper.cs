using SkiaSharp;

namespace Masuit.Tools.Media;

public static class SkiaImageHelper
{
    public static SKBitmap DecodeGrayThumb(string s, int targetSize)
    {
        using var stream = File.OpenRead(s);
        return DecodeGrayThumb(stream, targetSize);
    }

    public static SKBitmap DecodeGrayThumb(Stream stream, int targetSize)
    {
        using var managedStream = new SKManagedStream(stream);
        using var codec = SKCodec.Create(managedStream);

        if (codec == null)
            throw new InvalidOperationException("无法解码图像");

        var originalInfo = codec.Info;
        float scale = Math.Min((float)targetSize / originalInfo.Width, (float)targetSize / originalInfo.Height);
        scale = Math.Min(scale, 1.0f);

        // 获取编解码器支持的最佳缩放尺寸（提高解码性能）
        var supportedScale = codec.GetScaledDimensions(scale);

        // 先解码到支持的最佳尺寸（比目标尺寸稍大）
        var intermediateInfo = new SKImageInfo(supportedScale.Width, supportedScale.Height, SKColorType.Rgba8888, // 先解码为RGBA格式，兼容性更好
            SKAlphaType.Premul);

        using var intermediateBitmap = SKBitmap.Decode(codec, intermediateInfo);
        if (intermediateBitmap == null)
            throw new InvalidOperationException("中间图像解码失败");

        // 再精确缩放到目标尺寸并转换为灰度格式
        var targetInfo = new SKImageInfo((int)Math.Round(originalInfo.Width * scale), (int)Math.Round(originalInfo.Height * scale), SKColorType.Gray8, SKAlphaType.Opaque);

        var skBitmap = new SKBitmap(targetInfo);

        // 缩放并转换颜色格式
        intermediateBitmap.ScalePixels(skBitmap, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        stream.Seek(0, SeekOrigin.Begin);
        return skBitmap;
    }

    public static List<SKBitmap> DecodeGrayFrames(string file, int targetSize)
    {
        using var stream = File.OpenRead(file);
        return DecodeGrayFrames(stream, targetSize).ToList();
    }

    public static IEnumerable<SKBitmap> DecodeGrayFrames(Stream stream, int targetSize)
    {
        using var managedStream = new SKManagedStream(stream);
        using var codec = SKCodec.Create(managedStream);

        if (codec == null)
        {
            throw new InvalidOperationException("无法解码GIF文件");
        }

        // 获取GIF全局信息（所有帧共享的原始尺寸）
        var originalInfo = codec.Info;
        int frameCount = codec.FrameCount;

        // 计算统一的缩放比例（所有帧使用相同比例，与ImageSharp行为一致）
        float scale = Math.Min(
            (float)targetSize / originalInfo.Width,
            (float)targetSize / originalInfo.Height);
        scale = Math.Min(scale, 1.0f); // 不放大小于目标尺寸的图像

        // 计算所有帧统一的目标尺寸
        int targetWidth = (int)Math.Round(originalInfo.Width * scale);
        int targetHeight = (int)Math.Round(originalInfo.Height * scale);

        // 目标格式：8位灰度，无Alpha通道（对应ImageSharp的L8）
        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

        // 遍历所有帧（与ImageSharp的for循环完全对应）
        for (int i = 0; i < frameCount; i++)
        {
            // 解码第i帧并直接转换为目标格式和尺寸
            // SkiaSharp自动处理GIF帧合成、颜色转换和高质量缩放
            var frame = new SKBitmap(info);
            var options = new SKCodecOptions(i);
            var result = codec.GetPixels(info, frame.GetPixels(), options);
            if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
            {
                yield return frame;
            }
        }
        stream.Seek(0, SeekOrigin.Begin);
    }
}