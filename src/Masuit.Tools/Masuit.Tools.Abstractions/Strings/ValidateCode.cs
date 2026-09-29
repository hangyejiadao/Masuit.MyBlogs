using System.Security.Cryptography;
using System.Text;
using Masuit.Tools.Systems;
using SkiaSharp;
using System.Linq;

namespace Masuit.Tools.Strings;

/// <summary>
/// 画验证码
/// </summary>
public static class ValidateCode
{
    public static string CreateValidateCode(int length)
    {
        string ch = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ1234567890@#$%&?";
        byte[] b = new byte[4];
        using var cpt = RandomNumberGenerator.Create();
        cpt.GetBytes(b);
        var r = new Random(BitConverter.ToInt32(b, 0));
        var sb = new StringBuilder();
        for (int i = 0; i < length; i++)
        {
            sb.Append(ch[r.StrictNext(ch.Length)]);
        }
        return sb.ToString();
    }

    public static PooledMemoryStream CreateValidateGraphic(this string validateCode, int fontSize = 28)
    {
        // 选择字体
        var families = SKFontManager.Default.FontFamilies.ToArray();
        string[] preferred = { "Consolas", "DejaVu Sans", "KaiTi", "NSimSun", "SimSun", "SimHei", "Microsoft YaHei UI", "Arial" };
        SKTypeface typeface = null;
        foreach (var name in preferred)
        {
            if (families.Contains(name))
            {
                typeface = SKTypeface.FromFamilyName(name);
                if (typeface != null) break;
            }
        }
        typeface ??= SKTypeface.Default;

        using var measurePaint = new SKPaint();
        measurePaint.IsAntialias = true;
        using var font = new SKFont(typeface, fontSize);
        float textWidth = font.MeasureText(validateCode);
        var metrics = font.Metrics;
        float ascent = Math.Abs(metrics.Ascent);
        float descent = Math.Abs(metrics.Descent);
        float textHeight = ascent + descent;

        var width = (int)Math.Ceiling(textWidth + 15);
        var height = (int)Math.Ceiling(textHeight + 15);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        var random = new Random();

        // 背景
        canvas.Clear(SKColors.White);

        // 干扰线
        for (int i = 0; i < 65; i++)
        {
            int x1 = random.StrictNext(width);
            int x2 = random.StrictNext(width);
            int y1 = random.StrictNext(height);
            int y2 = random.StrictNext(height);
            using var linePaint = new SKPaint
            {
                Color = new SKColor((byte)random.StrictNext(255), (byte)random.StrictNext(255), (byte)random.StrictNext(255)),
                StrokeWidth = 1,
                Style = SKPaintStyle.Stroke,
                IsAntialias = false,
            };
            canvas.DrawLine(x1, y1, x2, y2, linePaint);
        }

        // 渐变文字
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height), [SKColors.Blue, SKColors.DarkRed], [0.5f, 0.5f],
            SKShaderTileMode.Repeat);

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = shader,
        };
        // SkiaSharp DrawText y = baseline
        using (var blob = SKTextBlob.Create(validateCode, font))
        {
            if (blob != null)
            {
                canvas.DrawText(blob, 3, 2 + ascent, textPaint);
            }
        }

        // 边框
        using var borderPaint = new SKPaint
        {
            Color = SKColors.Silver,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
        };
        canvas.DrawRect(new SKRect(0, 0, width - 1, height - 1), borderPaint);

        // 前景噪点
        for (int i = 0; i < 350; i++)
        {
            int x = random.StrictNext(bitmap.Width);
            int y = random.StrictNext(bitmap.Height);
            bitmap.SetPixel(x, y, new SKColor((byte)random.StrictNext(255), (byte)random.StrictNext(255), (byte)random.StrictNext(255)));
        }

        var stream = new PooledMemoryStream();
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 90);
        data.SaveTo(stream);
        stream.Position = 0;
        typeface.Dispose();
        return stream;
    }

    public static float StringWidth(this string s, int fontSize = 1)
    {
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei UI") ?? SKTypeface.Default;
        using var font = new SKFont(typeface, fontSize);
        return font.MeasureText(s);
    }

    public static float StringWidth(this string s, string fontName, int fontSize = 1)
    {
        var typeface = SKTypeface.FromFamilyName(fontName);
        if (typeface == null)
        {
            throw new ArgumentException($"字体 {fontName} 不存在，请尝试其它字体！");
        }
        using var font = new SKFont(typeface, fontSize);
        float w = font.MeasureText(s);
        typeface.Dispose();
        return w;
    }
}
