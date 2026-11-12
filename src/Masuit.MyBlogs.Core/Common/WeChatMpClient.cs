using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AngleSharp;
using FreeRedis;
using Masuit.Tools.Logging;
using Microsoft.AspNetCore.Hosting;
using SkiaSharp;
using Configuration = AngleSharp.Configuration;

namespace Masuit.MyBlogs.Core.Common;

/// <summary>
/// 微信公众号 API 客户端：access_token 获取与缓存（Redis，按 AppID 隔离）、正文图片转存、
/// 封面永久素材、草稿箱新增/更新、发布。接口文档见公众号后台「开发手册」。
/// 注意：调用服务器的出口 IP 必须先在公众号后台「设置与开发 → 基本配置 → IP 白名单」中添加，否则报 40164。
/// </summary>
public sealed class WeChatMpClient
{
    private const string ApiBase = "https://api.weixin.qq.com/cgi-bin";

    /// <summary>
    /// 正文图片上传接口（media/uploadimg）的大小上限：1MB
    /// </summary>
    private const int ContentImageLimit = 1024 * 1024;

    /// <summary>
    /// 永久素材图片（封面）的大小上限：2MB
    /// </summary>
    private const int MaterialImageLimit = 2 * 1024 * 1024;

    /// <summary>
    /// 草稿正文长度上限（微信侧限制：少于 2 万字符）
    /// </summary>
    private const int ContentLengthLimit = 20000;

    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _httpClient;
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly IRedisClient _redis;

    /// <summary>
    /// 微信公众号 API 客户端（类型化 HttpClient 注入，注册见 PrepareStartup.SetupHttpClients）
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="hostEnvironment">Web 宿主环境（定位本站本地图片文件）</param>
    /// <param name="redis">Redis 客户端（缓存 access_token 与封面素材 id）</param>
    public WeChatMpClient(HttpClient httpClient, IWebHostEnvironment hostEnvironment, IRedisClient redis)
    {
        _httpClient = httpClient;
        _hostEnvironment = hostEnvironment;
        _redis = redis;
    }

    #region access_token

    /// <summary>
    /// 获取 access_token：优先读 Redis 缓存，过期前 5 分钟视为失效；forceRefresh 时强制重新获取
    /// </summary>
    /// <param name="config">公众号配置</param>
    /// <param name="forceRefresh">是否强制刷新</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>access_token</returns>
    /// <exception cref="WeChatApiException">配置不完整或微信返回错误时抛出</exception>
    public async Task<string> GetAccessTokenAsync(WeChatConfiguration config, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!config.HasCredentials)
        {
            throw new WeChatApiException(-1, "公众号 AppID / AppSecret 未配置完整，请先到后台「公众号配置」页填写并保存");
        }

        var cacheKey = TokenCacheKey(config.AppId);
        if (!forceRefresh && _redis.Get<string>(cacheKey) is { Length: > 0 } cached)
        {
            return cached;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _redis.Get<string>(cacheKey) is { Length: > 0 } cached2)
            {
                return cached2;
            }

            var url = $"{ApiBase}/token?grant_type=client_credential&appid={Uri.EscapeDataString(config.AppId)}&secret={Uri.EscapeDataString(config.AppSecret)}";
            using var doc = await GetJsonAsync(url, cancellationToken);
            var root = doc.RootElement;
            if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
            {
                throw new WeChatApiException(err.GetInt32(), root.GetProperty("errmsg").GetString());
            }

            var token = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 7200;
            _redis.Set(cacheKey, token, Math.Max(60, expiresIn - 300));
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static string TokenCacheKey(string appId) => $"WeChat:AccessToken:{appId}";

    /// <summary>
    /// 使缓存的 access_token 失效（微信返回令牌类错误时调用）
    /// </summary>
    /// <param name="config">公众号配置</param>
    private void InvalidateToken(WeChatConfiguration config) => _redis.Del(TokenCacheKey(config.AppId));

    #endregion access_token

    #region 同步主流程

    /// <summary>
    /// 把一篇文章同步到公众号：正文图片转存 → 封面素材 → 新增或更新草稿 → 可选直接发布
    /// </summary>
    /// <param name="post">文章实体</param>
    /// <param name="config">公众号配置</param>
    /// <param name="sourceUrl">原文链接（写入草稿的「阅读原文」地址）</param>
    /// <param name="publish">是否在同步后直接发布</param>
    /// <param name="existingMediaId">该文章上次同步得到的草稿 media_id，非空则更新原草稿</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>同步结果（草稿 id、发布 id、是否已发布、警告信息）</returns>
    /// <exception cref="WeChatApiException">配置、内容或微信接口错误时抛出</exception>
    public async Task<WeChatSyncResult> SyncPostAsync(Post post, WeChatConfiguration config, string sourceUrl, bool publish, string existingMediaId, CancellationToken cancellationToken)
    {
        if (!config.Enabled)
        {
            throw new WeChatApiException(-1, "公众号同步未启用，请先到后台「公众号配置」页打开启用开关");
        }

        if (!config.HasCredentials)
        {
            throw new WeChatApiException(-1, "公众号 AppID / AppSecret 未配置完整，请先到后台「公众号配置」页填写并保存");
        }

        var warnings = new List<string>();
        var html = post.ContentType == ArticleContentType.Markdown ? post.Content.ToHtml() : post.Content;
        var (content, firstImage) = await PrepareContentAsync(html, config, warnings, cancellationToken);

        if (content.Length > ContentLengthLimit)
        {
            throw new WeChatApiException(-1, $"文章正文处理后共 {content.Length} 字符，超过微信草稿的 {ContentLengthLimit} 字符上限，请拆分文章或精简内容后再同步");
        }

        var thumbMediaId = await ResolveThumbMediaIdAsync(config, firstImage, warnings, cancellationToken);
        var article = new Dictionary<string, object>
        {
            ["title"] = Truncate(post.Title?.Trim(), 64),
            ["author"] = Truncate(string.IsNullOrWhiteSpace(config.Author) ? post.Author : config.Author, 8),
            ["digest"] = await BuildDigestAsync(html),
            ["content"] = content,
            ["content_source_url"] = sourceUrl ?? "",
            ["thumb_media_id"] = thumbMediaId,
            ["show_cover_pic"] = 0,
            ["need_open_comment"] = config.OpenComment ? 1 : 0,
            ["only_fans_can_comment"] = config.FansOnlyComment ? 1 : 0
        };

        string mediaId;
        if (!string.IsNullOrWhiteSpace(existingMediaId))
        {
            try
            {
                await WithTokenAsync(config, async (token, ct) =>
                {
                    using var doc = await PostJsonAsync($"{ApiBase}/draft/update?access_token={token}", new { media_id = existingMediaId, index = 0, articles = article }, ct);
                    EnsureSuccess(doc);
                    return true;
                }, cancellationToken);
                mediaId = existingMediaId;
            }
            catch (WeChatApiException e) when (e.ErrCode == 40007)
            {
                // 草稿已在公众号后台被删，改为新建草稿
                warnings.Add("原草稿已在公众号后台删除，本次已重新创建草稿");
                mediaId = await AddDraftAsync(config, article, cancellationToken);
            }
        }
        else
        {
            mediaId = await AddDraftAsync(config, article, cancellationToken);
        }

        string publishId = null;
        var published = false;
        if (publish)
        {
            try
            {
                publishId = await WithTokenAsync(config, async (token, ct) =>
                {
                    using var doc = await PostJsonAsync($"{ApiBase}/freepublish/submit?access_token={token}", new { media_id = mediaId }, ct);
                    EnsureSuccess(doc);
                    return doc.RootElement.TryGetProperty("publish_id", out var pid) ? pid.GetString() : null;
                }, cancellationToken);
                published = publishId != null;
            }
            catch (WeChatApiException e)
            {
                warnings.Add($"草稿已同步成功，但发布失败：{e.Message}（可在公众号后台手动发布该草稿）");
            }
        }

        return new WeChatSyncResult
        {
            MediaId = mediaId,
            PublishId = publishId,
            Published = published,
            Warnings = warnings
        };
    }

    /// <summary>
    /// 新增一篇草稿，返回草稿 media_id
    /// </summary>
    /// <param name="config">公众号配置</param>
    /// <param name="article">草稿文章对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>草稿 media_id</returns>
    private Task<string> AddDraftAsync(WeChatConfiguration config, Dictionary<string, object> article, CancellationToken cancellationToken) =>
        WithTokenAsync(config, async (token, ct) =>
        {
            using var doc = await PostJsonAsync($"{ApiBase}/draft/add?access_token={token}", new { articles = new[] { article } }, ct);
            EnsureSuccess(doc);
            return doc.RootElement.GetProperty("media_id").GetString();
        }, cancellationToken);

    /// <summary>
    /// 查询草稿箱草稿总数（测试连接时顺带验证草稿接口权限）
    /// </summary>
    /// <param name="config">公众号配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>草稿总数</returns>
    public Task<int> GetDraftCountAsync(WeChatConfiguration config, CancellationToken cancellationToken) =>
        WithTokenAsync(config, async (token, ct) =>
        {
            using var doc = await PostJsonAsync($"{ApiBase}/draft/count?access_token={token}", new { }, ct);
            EnsureSuccess(doc);
            return doc.RootElement.GetProperty("total_count").GetInt32();
        }, cancellationToken);

    #endregion 同步主流程

    #region 正文与封面处理

    /// <summary>
    /// 处理正文 HTML 使其符合微信要求：剔除脚本类标签与事件属性，把每张图片下载后转存到微信图床并替换地址；
    /// 图片转存失败时移除该图并记警告。同时带回正文首图字节供封面使用。
    /// </summary>
    /// <param name="html">文章 HTML（Markdown 已在调用前转好）</param>
    /// <param name="config">公众号配置</param>
    /// <param name="warnings">警告收集列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的 HTML 与正文首图（字节+文件名），无图时首图为 null</returns>
    private async Task<(string Content, (byte[] Bytes, string FileName) FirstImage)> PrepareContentAsync(string html, WeChatConfiguration config, List<string> warnings, CancellationToken cancellationToken)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var doc = await context.OpenAsync(req => req.Content(html ?? ""), cancel: cancellationToken);
        foreach (var el in doc.QuerySelectorAll("script, iframe, form, object, embed, video, audio, link, style"))
        {
            el.Remove();
        }

        (byte[] Bytes, string FileName) firstImage = default;
        var uploaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var images = doc.QuerySelectorAll("img").ToList();
        foreach (var img in images)
        {
            foreach (var attr in img.Attributes.Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || a.Name is "srcset").ToList())
            {
                img.RemoveAttribute(attr.Name);
            }

            var src = img.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(src) && img.HasAttribute("data-src"))
            {
                src = img.GetAttribute("data-src");
                img.RemoveAttribute("data-src");
            }

            if (string.IsNullOrWhiteSpace(src))
            {
                img.Remove();
                continue;
            }

            if (src.Contains("mmbiz.qpic.cn", StringComparison.OrdinalIgnoreCase))
            {
                continue; // 已是微信图床地址，无需转存
            }

            try
            {
                if (!uploaded.TryGetValue(src, out var newUrl))
                {
                    var (bytes, fileName) = await DownloadImageAsync(src, cancellationToken);
                    if (firstImage.Bytes is null)
                    {
                        firstImage = (bytes, fileName);
                    }

                    newUrl = await UploadContentImageAsync(config, bytes, fileName, cancellationToken);
                    uploaded[src] = newUrl;
                }

                img.SetAttribute("src", newUrl);
            }
            catch (Exception e)
            {
                LogManager.Error(e);
                warnings.Add($"一张正文图片转存失败已跳过：{src}（{e.Message}）");
                img.Remove();
            }
        }

        return (doc.Body.InnerHtml, firstImage);
    }

    /// <summary>
    /// 确定封面永久素材 id：优先正文首图；正文无图时依次回退到配置的默认封面、站点 Logo，
    /// 都不可用时用代码生成一张渐变兜底封面，保证纯文字文章也能同步（公众号草稿必须带封面永久素材）；
    /// 按图片内容 MD5 缓存素材 id，避免重复上传永久素材
    /// </summary>
    /// <param name="config">公众号配置</param>
    /// <param name="firstImage">正文首图（可为默认元组）</param>
    /// <param name="warnings">警告收集列表（封面来源加载失败时记录）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>封面 thumb_media_id</returns>
    private async Task<string> ResolveThumbMediaIdAsync(WeChatConfiguration config, (byte[] Bytes, string FileName) firstImage, List<string> warnings, CancellationToken cancellationToken)
    {
        var (bytes, fileName) = firstImage;
        if (bytes is null)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.DefaultCoverUrl))
            {
                candidates.Add(config.DefaultCoverUrl.Trim());
            }

            var logo = CommonHelper.SystemSettings.GetOrAdd("logo", "")?.Trim();
            if (!string.IsNullOrWhiteSpace(logo) && !candidates.Contains(logo, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(logo);
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    (bytes, fileName) = await DownloadImageAsync(candidate, cancellationToken);
                    break;
                }
                catch (Exception e)
                {
                    LogManager.Error(e);
                    warnings.Add($"封面图来源加载失败，已自动尝试下一来源：{candidate}（{e.Message}）");
                }
            }

            if (bytes is null)
            {
                (bytes, fileName) = GenerateDefaultCover();
            }
        }

        var cacheKey = $"WeChat:Thumb:{Convert.ToHexString(MD5.HashData(bytes))}";
        if (_redis.Get<string>(cacheKey) is { Length: > 0 } cachedMediaId)
        {
            return cachedMediaId;
        }

        var mediaId = await UploadMaterialImageAsync(config, bytes, fileName, cancellationToken);
        _redis.Set(cacheKey, mediaId);
        return mediaId;
    }

    /// <summary>
    /// 生成兜底封面图（900×383，与公众号大图封面尺寸一致）：深蓝渐变背景加装饰圆与站点域名文字，JPEG 编码。
    /// 字体不可用时跳过文字只保留图形，保证任何环境下都能产出一张可用封面
    /// </summary>
    /// <returns>封面图字节与文件名</returns>
    private static (byte[] Bytes, string FileName) GenerateDefaultCover()
    {
        const int width = 900;
        const int height = 383;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        using var canvas = new SKCanvas(bitmap);
        using (var gradientPaint = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height), [SKColor.Parse("0D2A56"), SKColor.Parse("10588C")], null, SKShaderTileMode.Clamp) })
        {
            canvas.DrawRect(SKRect.Create(width, height), gradientPaint);
        }

        using (var circlePaint = new SKPaint { Color = new SKColor(255, 255, 255, 26), IsAntialias = true })
        {
            canvas.DrawCircle(790, 60, 150, circlePaint);
            canvas.DrawCircle(850, 300, 110, circlePaint);
        }

        using (var circlePaint = new SKPaint { Color = new SKColor(255, 255, 255, 18), IsAntialias = true })
        {
            canvas.DrawCircle(80, 340, 130, circlePaint);
        }

        using (var accentPaint = new SKPaint { Color = SKColor.Parse("40BEDC"), IsAntialias = true })
        {
            canvas.DrawRect(SKRect.Create(64, 236, 86, 8), accentPaint);
        }

        try
        {
            var domain = CommonHelper.SystemSettings.GetOrAdd("Domain", "")?.Trim();
            if (string.IsNullOrWhiteSpace(domain))
            {
                domain = "hangyejiadao.vip";
            }

            using var titleFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 54);
            using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText(domain, 64, 205, SKTextAlign.Left, titleFont, titlePaint);
            using var subFont = new SKFont(SKTypeface.FromFamilyName("Arial"), 30);
            using var subPaint = new SKPaint { Color = new SKColor(190, 220, 240), IsAntialias = true };
            canvas.DrawText("Tech Blog", 66, 285, SKTextAlign.Left, subFont, subPaint);
        }
        catch (Exception e)
        {
            LogManager.Error(e);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 88);
        return (data.ToArray(), "wechat-default-cover.jpg");
    }

    /// <summary>
    /// 生成摘要：取正文纯文本前 110 个字符（微信摘要上限 120 字）
    /// </summary>
    /// <param name="html">文章 HTML</param>
    /// <returns>摘要文本</returns>
    private static async Task<string> BuildDigestAsync(string html)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var doc = await context.OpenAsync(req => req.Content(html ?? ""));
        var text = string.Join(" ", doc.Body.TextContent.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        return Truncate(text, 110);
    }

    /// <summary>
    /// 下载一张图片：支持 data URI、本站相对路径（读 wwwroot 本地文件）与任意 http(s) 地址
    /// </summary>
    /// <param name="src">图片地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>图片字节与文件名</returns>
    /// <exception cref="InvalidOperationException">地址为空、文件不存在或图片过大时抛出</exception>
    private async Task<(byte[] Bytes, string FileName)> DownloadImageAsync(string src, CancellationToken cancellationToken)
    {
        src = src.Trim();
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = src.IndexOf(',');
            if (comma < 0)
            {
                throw new InvalidOperationException("data URI 图片格式不合法");
            }

            var header = src[5..comma];
            var ext = header.Contains("png") ? ".png" : header.Contains("gif") ? ".gif" : ".jpg";
            return (Convert.FromBase64String(src[(comma + 1)..]), "image" + ext);
        }

        if (!src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var relative = src.Split('?', '#')[0].TrimStart('/');
            var localPath = Path.Combine(_hostEnvironment.WebRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
            {
                throw new InvalidOperationException($"本站图片文件不存在：{src}");
            }

            return (await File.ReadAllBytesAsync(localPath, cancellationToken), Path.GetFileName(localPath));
        }

        using var resp = await _httpClient.GetAsync(src, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException("图片超过 10MB，无法转存");
        }

        var fileName = Path.GetFileName(new Uri(src).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains('.'))
        {
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            fileName = "image" + (contentType.Contains("png") ? ".png" : contentType.Contains("gif") ? ".gif" : ".jpg");
        }

        return (bytes, fileName);
    }

    #endregion 正文与封面处理

    #region 微信素材上传

    /// <summary>
    /// 上传正文图片到微信图床（media/uploadimg），返回微信侧图片地址；超过 1MB 时先压缩
    /// </summary>
    /// <param name="config">公众号配置</param>
    /// <param name="bytes">图片字节</param>
    /// <param name="fileName">文件名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>微信图床地址</returns>
    private Task<string> UploadContentImageAsync(WeChatConfiguration config, byte[] bytes, string fileName, CancellationToken cancellationToken) =>
        WithTokenAsync(config, async (token, ct) =>
        {
            var data = TryCompress(bytes, ContentImageLimit);
            using var doc = await PostMultipartAsync($"{ApiBase}/media/uploadimg?access_token={token}", data, fileName, ct);
            EnsureSuccess(doc);
            return doc.RootElement.GetProperty("url").GetString();
        }, cancellationToken);

    /// <summary>
    /// 上传封面图为永久素材（material/add_material?type=image），返回 media_id；超过 2MB 时先压缩
    /// </summary>
    /// <param name="config">公众号配置</param>
    /// <param name="bytes">图片字节</param>
    /// <param name="fileName">文件名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>永久素材 media_id</returns>
    private Task<string> UploadMaterialImageAsync(WeChatConfiguration config, byte[] bytes, string fileName, CancellationToken cancellationToken) =>
        WithTokenAsync(config, async (token, ct) =>
        {
            var data = TryCompress(bytes, MaterialImageLimit);
            using var doc = await PostMultipartAsync($"{ApiBase}/material/add_material?access_token={token}&type=image", data, fileName, ct);
            EnsureSuccess(doc);
            return doc.RootElement.GetProperty("media_id").GetString();
        }, cancellationToken);

    /// <summary>
    /// 图片超过上限时尝试用 SkiaSharp 缩放并转 JPEG 压缩（gif 动图不处理，避免丢帧后仍超限直接交给微信报错）；
    /// 压缩失败或压不下去时返回原图
    /// </summary>
    /// <param name="bytes">原图字节</param>
    /// <param name="limit">目标大小上限（字节）</param>
    /// <returns>处理后的字节</returns>
    private static byte[] TryCompress(byte[] bytes, int limit)
    {
        if (bytes.Length <= limit)
        {
            return bytes;
        }

        try
        {
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
            {
                return bytes;
            }

            foreach (var (maxWidth, quality) in new[] { (1080, 85), (800, 75), (640, 65) })
            {
                var scale = Math.Min(1d, (double)maxWidth / bitmap.Width);
                var width = Math.Max(1, (int)(bitmap.Width * scale));
                var height = Math.Max(1, (int)(bitmap.Height * scale));
                using var resized = scale < 1 ? bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)) : null;
                using var image = SKImage.FromBitmap(resized ?? bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
                var result = data.ToArray();
                if (result.Length <= limit)
                {
                    return result;
                }
            }
        }
        catch (Exception e)
        {
            LogManager.Error(e);
        }

        return bytes;
    }

    #endregion 微信素材上传

    #region HTTP 基础

    /// <summary>
    /// 带令牌执行微信接口调用：先取缓存令牌，遇令牌失效类错误（40001/42001/40014）时刷新令牌重试一次
    /// </summary>
    /// <typeparam name="T">结果类型</typeparam>
    /// <param name="config">公众号配置</param>
    /// <param name="action">以令牌为入参的调用</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>调用结果</returns>
    private async Task<T> WithTokenAsync<T>(WeChatConfiguration config, Func<string, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(config, false, cancellationToken);
        try
        {
            return await action(token, cancellationToken);
        }
        catch (WeChatApiException e) when (e.ErrCode is 40001 or 42001 or 40014)
        {
            InvalidateToken(config);
            var fresh = await GetAccessTokenAsync(config, true, cancellationToken);
            return await action(fresh, cancellationToken);
        }
    }

    /// <summary>
    /// GET 并解析 JSON 响应
    /// </summary>
    /// <param name="url">请求地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>JSON 文档（调用方负责释放）</returns>
    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var resp = await _httpClient.GetAsync(url, cancellationToken);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(text);
    }

    /// <summary>
    /// 以 JSON 方式 POST 并解析响应
    /// </summary>
    /// <param name="url">请求地址</param>
    /// <param name="body">请求体对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>JSON 文档（调用方负责释放）</returns>
    private async Task<JsonDocument> PostJsonAsync(string url, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _httpClient.PostAsync(url, content, cancellationToken);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(text);
    }

    /// <summary>
    /// 以 multipart 方式上传图片文件并解析响应（表单字段名固定为 media）
    /// </summary>
    /// <param name="url">请求地址</param>
    /// <param name="bytes">图片字节</param>
    /// <param name="fileName">文件名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>JSON 文档（调用方负责释放）</returns>
    private async Task<JsonDocument> PostMultipartAsync(string url, byte[] bytes, string fileName, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeOf(fileName));
        form.Add(fileContent, "media", string.IsNullOrWhiteSpace(fileName) ? "image.jpg" : fileName);
        using var resp = await _httpClient.PostAsync(url, form, cancellationToken);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(text);
    }

    /// <summary>
    /// 校验微信响应：errcode 存在且非 0 时抛出带中文排查提示的异常
    /// </summary>
    /// <param name="doc">微信响应文档</param>
    /// <exception cref="WeChatApiException">微信返回错误时抛出</exception>
    private static void EnsureSuccess(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("errcode", out var err) && err.ValueKind == JsonValueKind.Number && err.GetInt32() != 0)
        {
            var msg = doc.RootElement.TryGetProperty("errmsg", out var m) ? m.GetString() : "";
            throw new WeChatApiException(err.GetInt32(), msg);
        }
    }

    /// <summary>
    /// 按扩展名推断图片 MIME 类型
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <returns>MIME 类型</returns>
    private static string ContentTypeOf(string fileName) => Path.GetExtension(fileName)?.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "image/jpeg"
    };

    /// <summary>
    /// 按字符数截断文本（微信标题/作者/摘要均按字符计数）
    /// </summary>
    /// <param name="text">原文</param>
    /// <param name="max">最大字符数</param>
    /// <returns>截断后的文本</returns>
    private static string Truncate(string text, int max)
    {
        text = text?.Trim() ?? "";
        return text.Length <= max ? text : text[..max];
    }

    #endregion HTTP 基础
}

/// <summary>
/// 公众号同步结果
/// </summary>
public sealed class WeChatSyncResult
{
    /// <summary>
    /// 草稿 media_id
    /// </summary>
    public string MediaId { get; set; }

    /// <summary>
    /// 发布任务 id（直接发布时才有）
    /// </summary>
    public string PublishId { get; set; }

    /// <summary>
    /// 是否已提交发布
    /// </summary>
    public bool Published { get; set; }

    /// <summary>
    /// 同步过程中的警告（如单张图片转存失败、发布失败但草稿已保存）
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// 一篇文章的公众号同步记录，存 Redis（键 WeChat:Sync:{文章id}），用于再次同步时更新原草稿与列表页状态展示
/// </summary>
public sealed class WeChatSyncRecord
{
    /// <summary>
    /// 文章 id
    /// </summary>
    public int PostId { get; set; }

    /// <summary>
    /// 草稿 media_id
    /// </summary>
    public string MediaId { get; set; }

    /// <summary>
    /// 发布任务 id
    /// </summary>
    public string PublishId { get; set; }

    /// <summary>
    /// 状态：draft 已同步到草稿箱 / published 已发布 / failed 同步失败
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// 最近一次同步时间（yyyy-MM-dd HH:mm:ss）
    /// </summary>
    public string SyncTime { get; set; }

    /// <summary>
    /// 同步时文章的修改时间（与当前 ModifyDate 不一致说明同步后文章又改过）
    /// </summary>
    public string PostModifyDate { get; set; }

    /// <summary>
    /// 附带消息（失败原因或警告摘要）
    /// </summary>
    public string Message { get; set; }
}

/// <summary>
/// 公众号同步记录的 Redis 存取（静态工具，供后台控制器与 Hangfire 任务共用）
/// </summary>
public static class WeChatSyncStore
{
    private static string Key(int postId) => $"WeChat:Sync:{postId}";

    /// <summary>
    /// 读取一篇文章的同步记录，无记录或解析失败返回 null
    /// </summary>
    /// <param name="redis">Redis 客户端</param>
    /// <param name="postId">文章 id</param>
    /// <returns>同步记录或 null</returns>
    public static WeChatSyncRecord Get(IRedisClient redis, int postId)
    {
        try
        {
            var json = redis.Get<string>(Key(postId));
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<WeChatSyncRecord>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 保存一篇文章的同步记录
    /// </summary>
    /// <param name="redis">Redis 客户端</param>
    /// <param name="record">同步记录</param>
    public static void Save(IRedisClient redis, WeChatSyncRecord record)
    {
        redis.Set(Key(record.PostId), JsonSerializer.Serialize(record));
    }
}

/// <summary>
/// 微信接口异常：携带微信 errcode 与 errmsg，并附常见错误的中文排查提示
/// </summary>
/// <param name="errCode">微信错误码（本地校验错误为 -1）</param>
/// <param name="errMsg">微信错误消息或本地错误说明</param>
public sealed class WeChatApiException(int errCode, string errMsg) : Exception(BuildMessage(errCode, errMsg))
{
    /// <summary>
    /// 微信错误码（本地校验错误为 -1）
    /// </summary>
    public int ErrCode { get; } = errCode;

    /// <summary>
    /// 微信原始错误消息
    /// </summary>
    public string ErrMsg { get; } = errMsg;

    private static string BuildMessage(int code, string msg)
    {
        var hint = code switch
        {
            -1 => "",
            40001 or 42001 or 40014 or 40125 => "请核对「公众号配置」页的 AppID 与 AppSecret 是否与公众号后台一致",
            40013 => "AppID 不正确，请核对「公众号配置」页",
            40164 or 40165 => "服务器出口 IP 不在公众号 IP 白名单内：请到公众号后台「设置与开发 → 基本配置 → IP 白名单」添加博客服务器的出口 IP",
            48001 => "当前公众号没有该接口权限：草稿箱与发布接口需要已认证的公众号（未认证订阅号无此权限）",
            45009 => "接口调用次数已达当日上限，请明天再试",
            40007 => "草稿不存在（media_id 无效），可能已在公众号后台被删除",
            45109 => "草稿 id 不合法",
            _ => ""
        };
        var text = code == -1 ? msg : $"微信接口错误 {code}：{msg}";
        return string.IsNullOrEmpty(hint) ? text : $"{text}。{hint}";
    }
}
