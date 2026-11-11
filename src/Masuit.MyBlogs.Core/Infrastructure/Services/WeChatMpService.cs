using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using FreeRedis;
using Markdig;
using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Infrastructure.Repository.Interface;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Masuit.MyBlogs.Core.Infrastructure.Services;

/// <summary>
/// 微信公众号文章推送服务：把博客文章同步到公众号草稿箱（可选自动发表）。
/// 按「个人订阅号」设计：只用 access_token、图片上传（uploadimg）、永久素材（add_material）、
/// 草稿（draft/add）、发布（freepublish/submit）这几个接口，不用模板消息、客服消息群发等
/// 服务号才有的能力。个人订阅号自 2025 年 7 月起已被微信回收发布接口权限，调发布会报 48001，
/// 此时草稿仍保留在草稿箱，需在公众号后台或「公众号助手」App 手动发表/群发。
/// </summary>
public sealed class WeChatMpService(IHttpClientFactory httpClientFactory, IRedisClient cache, IWeChatMpPushRecordRepository recordRepository) : IWeChatMpService
{
	private readonly HttpClient _http = httpClientFactory.CreateClient("WeChatMp");

	/// <summary>access_token 在 Redis 里的缓存键</summary>
	private const string TokenCacheKey = "WeChatMp:AccessToken";

	/// <summary>正文图片上传限制：uploadimg 单张不超过 10MB</summary>
	private const int MaxContentImageBytes = 10 * 1024 * 1024;

	/// <summary>微信图文标题上限 64 字，摘要上限 120 字</summary>
	private const int MaxTitleLength = 64;

	private const int MaxDigestLength = 120;

	/// <summary>微信不接受的标签：整段删掉（含子节点）</summary>
	private static readonly string[] RemoveTags = ["script", "style", "iframe", "video", "audio", "source", "track", "form", "input", "button", "select", "textarea", "link", "meta", "object", "embed", "canvas", "svg", "noscript", "template"];

	/// <summary>微信不接受但内容要保留的标签：拆掉标签、保留子节点</summary>
	private static readonly string[] UnwrapTags = ["figure", "figcaption", "font", "main", "article", "aside", "header", "footer", "nav", "details", "summary", "abbr", "cite", "q", "time", "mark", "ins", "kbd", "samp", "var", "wbr", "center", "fieldset", "legend", "label", "picture"];

	private static WeChatMpConfig Config => AppConfig.WeChatMp;

	private static string ApiBaseUrl => Config.ApiBaseUrl?.TrimEnd('/') ?? "https://api.weixin.qq.com";

	public bool Configured => Config.Configured;

	/// <summary>
	/// 拿 access_token：先读 Redis 缓存（约 100 分钟），没有就调接口拿并写缓存。
	/// 微信 access_token 有效期 2 小时，这里留足余量、提前 5 分钟过期。
	/// </summary>
	public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
	{
		var cached = cache.Get<string>(TokenCacheKey);
		if (!string.IsNullOrWhiteSpace(cached))
		{
			return cached;
		}

		var (accessToken, expiresIn) = await FetchAccessTokenAsync(cancellationToken);
		var ttlSeconds = Math.Min(Math.Max(expiresIn - 300, 60), 6600);
		cache.Set(TokenCacheKey, accessToken, TimeSpan.FromSeconds(ttlSeconds));
		return accessToken;
	}

	/// <summary>
	/// 把一篇已发布博客文章推送到公众号：转微信图文 HTML、上传正文图片与封面、建草稿，
	/// publish=true 且配置 AutoPublish=true 时再调发布接口提交发表。
	/// 草稿创建成功即返回 Success=true；发表失败不影响草稿，原因写在结果的 PublishError 里。
	/// </summary>
	public async Task<WeChatMpPushResult> PushPostAsync(Post post, string articleUrl, bool publish, CancellationToken cancellationToken = default)
	{
		var result = new WeChatMpPushResult();
		var record = new WeChatMpPushRecord
		{
			PostId = post.Id,
			Title = post.Title,
			PushStatus = WeChatMpPushStatus.Failed,
			PublishRequested = publish && Config.AutoPublish,
			PushedAt = DateTime.Now,
			UpdatedAt = DateTime.Now
		};
		try
		{
			if (!Configured)
			{
				throw new WeChatMpException("微信公众号推送未启用：请在 appsettings.json 的 WeChatMp 节填写 AppId、AppSecret 并把 Enabled 设为 true。");
			}

			if (post.Status != Status.Published)
			{
				throw new WeChatMpException("这篇文章还没发布，不能推送到公众号。");
			}

			// 1. 内容转微信图文 HTML，正文图片上传成微信素材；返回的首图字节留着做封面
			var (html, warnings, coverBytes, coverFileName, coverContentType) = await ConvertToWeChatHtmlAsync(post, articleUrl, cancellationToken);
			result.Warnings.AddRange(warnings);

			// 2. 封面：优先用文章首图上传永久素材，没有图片就用配置的默认封面，两者都没有就报错
			var thumbMediaId = await ResolveThumbMediaIdAsync(coverBytes, coverFileName, coverContentType, result.Warnings, cancellationToken);

			// 3. 建草稿
			var mediaId = await CreateDraftAsync(post, html, thumbMediaId, articleUrl, cancellationToken);
			record.MediaId = mediaId;
			record.PushStatus = WeChatMpPushStatus.DraftCreated;
			result.MediaId = mediaId;
			result.Status = WeChatMpPushStatus.DraftCreated;
			result.Success = true;
			result.Message = $"草稿已创建（media_id：{mediaId}），请在公众号后台或「公众号助手」App 检查后手动发表/群发。";

			// 4. 可选自动发表：个人订阅号调发布接口会报 48001，草稿不受影响
			if (publish && Config.AutoPublish)
			{
				result.PublishAttempted = true;
				try
				{
					var publishId = await SubmitPublishAsync(mediaId, cancellationToken);
					record.PublishId = publishId;
					record.PushStatus = WeChatMpPushStatus.Publishing;
					result.PublishId = publishId;
					result.Status = WeChatMpPushStatus.Publishing;
					result.Message = $"已提交发表（publish_id：{publishId}），微信正在审核，稍后可在推送记录里查询发表状态。";
				}
				catch (WeChatMpException ex)
				{
					result.PublishError = ex.Message;
					record.ErrorCode = ex.ErrorCode;
					record.ErrorMessage = ex.Message;
					result.Message += "自动发表失败：" + ex.Message + "草稿已保留在草稿箱，请手动发表。";
				}
			}
			else if (publish)
			{
				result.Message += "（自动发表未开启，AutoPublish=false）";
			}
		}
		catch (WeChatMpException ex)
		{
			record.ErrorCode = ex.ErrorCode;
			record.ErrorMessage = ex.Message;
			result.Success = false;
			result.Status = WeChatMpPushStatus.Failed;
			result.Message = ex.Message;
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			record.ErrorMessage = "调用微信接口失败：" + ex.Message;
			result.Success = false;
			result.Status = WeChatMpPushStatus.Failed;
			result.Message = "调用微信接口失败（网络超时或连接异常），请稍后重试：" + ex.Message;
		}

		recordRepository.AddEntity(record);
		await recordRepository.SaveChangesAsync();
		result.RecordId = record.Id;
		return result;
	}

	/// <summary>取一篇文章最近一次的推送记录，没有则返回 null</summary>
	public async Task<WeChatMpPushRecord> GetLatestRecordAsync(int postId, CancellationToken cancellationToken = default)
	{
		return await recordRepository.GetQuery(r => r.PostId == postId).OrderByDescending(r => r.PushedAt).FirstOrDefaultAsync(cancellationToken);
	}

	/// <summary>
	/// 对「发表中」的记录调 freepublish/get 查一次发表状态并更新记录；
	/// publish_status：0 已发表（取文章链接）、1 发表中（不动）、2 发表失败。
	/// </summary>
	public async Task<WeChatMpPushRecord> RefreshPublishStatusAsync(int recordId, CancellationToken cancellationToken = default)
	{
		var record = await recordRepository.GetByIdAsync(recordId);
		if (record == null || record.PushStatus != WeChatMpPushStatus.Publishing || string.IsNullOrWhiteSpace(record.PublishId))
		{
			return record;
		}

		using var doc = await PostJsonWithTokenAsync("/cgi-bin/freepublish/get", new { publish_id = record.PublishId }, cancellationToken);
		var errcode = GetErrCode(doc);
		if (errcode != 0)
		{
			throw new WeChatMpException(DescribeError(errcode, GetErrMsg(doc), "publish"), errcode);
		}

		var root = doc.RootElement;
		var publishStatus = root.TryGetProperty("publish_status", out var ps) && ps.ValueKind == JsonValueKind.Number ? ps.GetInt32() : 1;
		switch (publishStatus)
		{
			case 0:
				record.PushStatus = WeChatMpPushStatus.Published;
				record.ArticleUrl = GetFirstArticleUrl(root);
				break;
			case 2:
				record.PushStatus = WeChatMpPushStatus.Failed;
				record.ErrorMessage = "微信发表失败" + (root.TryGetProperty("fail_idx", out var failIdx) ? $"（失败的文章序号：{failIdx}）" : string.Empty) + "，请在公众号后台查看原因后重新推送。";
				break;
			default:
				// 1 发表中：保持原样，等下次查询
				break;
		}

		record.UpdatedAt = DateTime.Now;
		await recordRepository.SaveChangesAsync();
		return record;
	}

	#region 微信 API 调用

	/// <summary>调微信拿 access_token（不走缓存，供 GetAccessTokenAsync 调用）</summary>
	private async Task<(string AccessToken, int ExpiresIn)> FetchAccessTokenAsync(CancellationToken ct)
	{
		var url = $"{ApiBaseUrl}/cgi-bin/token?grant_type=client_credential&appid={Uri.EscapeDataString(Config.AppId)}&secret={Uri.EscapeDataString(Config.AppSecret)}";
		using var response = await _http.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();
		using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
		var errcode = GetErrCode(doc);
		if (errcode != 0)
		{
			throw new WeChatMpException(DescribeError(errcode, GetErrMsg(doc), "token"), errcode);
		}

		var root = doc.RootElement;
		var token = root.TryGetProperty("access_token", out var t) ? t.GetString() : null;
		var expiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 7200;
		if (string.IsNullOrWhiteSpace(token))
		{
			throw new WeChatMpException("微信返回的 access_token 为空，请检查公众号配置。");
		}

		return (token, expiresIn);
	}

	/// <summary>
	/// 带 token 的 JSON POST：40001/42001（token 无效/过期）时清缓存换 token 重试一次。
	/// 返回的 JsonDocument 由调用方负责释放。
	/// </summary>
	private async Task<JsonDocument> PostJsonWithTokenAsync(string path, object payload, CancellationToken ct)
	{
		return await CallWithTokenRetryAsync(async token =>
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}{path}?access_token={Uri.EscapeDataString(token)}")
			{
				Content = JsonContent.Create(payload)
			};
			using var response = await _http.SendAsync(request, ct);
			response.EnsureSuccessStatusCode();
			return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
		}, ct);
	}

	/// <summary>
	/// 带 token 的 multipart 文件上传（图片）：40001/42001 时清缓存换 token 重试一次。
	/// 返回的 JsonDocument 由调用方负责释放。
	/// </summary>
	private async Task<JsonDocument> PostMultipartWithTokenAsync(string path, byte[] bytes, string fileName, string contentType, CancellationToken ct)
	{
		return await CallWithTokenRetryAsync(async token =>
		{
			using var content = new MultipartFormDataContent();
			var fileContent = new ByteArrayContent(bytes);
			fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
			content.Add(fileContent, "media", fileName);
			using var response = await _http.PostAsync($"{ApiBaseUrl}{path}?access_token={Uri.EscapeDataString(token)}", content, ct);
			response.EnsureSuccessStatusCode();
			return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
		}, ct);
	}

	private async Task<JsonDocument> CallWithTokenRetryAsync(Func<string, Task<JsonDocument>> call, CancellationToken ct)
	{
		for (var attempt = 0; attempt < 2; attempt++)
		{
			var token = await GetAccessTokenAsync(ct);
			var doc = await call(token);
			var errcode = GetErrCode(doc);
			if ((errcode == 40001 || errcode == 42001) && attempt == 0)
			{
				// token 无效或过期：清缓存，下一次循环重新拿 token 再调一次
				cache.Del(TokenCacheKey);
				doc.Dispose();
				continue;
			}

			return doc;
		}

		throw new WeChatMpException("拿 access_token 失败，请检查公众号配置。");
	}

	/// <summary>正文图片上传（media/uploadimg，返回微信图片地址）</summary>
	private async Task<string> UploadContentImageAsync(byte[] bytes, string fileName, string contentType, CancellationToken ct)
	{
		using var doc = await PostMultipartWithTokenAsync("/cgi-bin/media/uploadimg", bytes, fileName, contentType, ct);
		var errcode = GetErrCode(doc);
		if (errcode != 0)
		{
			throw new WeChatMpException(DescribeError(errcode, GetErrMsg(doc), "uploadimg"), errcode);
		}

		return doc.RootElement.TryGetProperty("url", out var url) ? url.GetString() : null;
	}

	/// <summary>封面上传永久素材（material/add_material?type=image，返回 media_id）</summary>
	private async Task<string> UploadThumbAsync(byte[] bytes, string fileName, string contentType, CancellationToken ct)
	{
		using var doc = await PostMultipartWithTokenAsync("/cgi-bin/material/add_material?type=image", bytes, fileName, contentType, ct);
		var errcode = GetErrCode(doc);
		if (errcode != 0)
		{
			throw new WeChatMpException(DescribeError(errcode, GetErrMsg(doc), "thumb"), errcode);
		}

		return doc.RootElement.TryGetProperty("media_id", out var mediaId) ? mediaId.GetString() : null;
	}

	/// <summary>新建草稿（draft/add），返回草稿 media_id</summary>
	private async Task<string> CreateDraftAsync(Post post, string html, string thumbMediaId, string articleUrl, CancellationToken ct)
	{
		var payload = new
		{
			articles = new[]
			{
				new
				{
					title = Truncate(post.Title, MaxTitleLength),
					author = post.Author,
					digest = BuildDigest(html),
					content = html,
					content_source_url = articleUrl,
					thumb_media_id = thumbMediaId,
					need_open_comment = 0,
					only_fans_can_comment = 0
				}
			}
		};
		using var doc = await PostJsonWithTokenAsync("/cgi-bin/draft/add", payload, ct);
		var errcode = GetErrCode(doc);
		if (errcode != 0)
		{
			throw new WeChatMpException(DescribeError(errcode, GetErrMsg(doc), "draft"), errcode);
		}

		var mediaId = doc.RootElement.TryGetProperty("media_id", out var m) ? m.GetString() : null;
		if (string.IsNullOrWhiteSpace(mediaId))
		{
			throw new WeChatMpException("微信创建草稿没有返回 media_id，请稍后重试。");
		}

		return mediaId;
	}

	/// <summary>提交发表（freepublish/submit），返回 publish_id</summary>
	private async Task<string> SubmitPublishAsync(string mediaId, CancellationToken ct)
	{
		using var doc = await PostJsonWithTokenAsync("/cgi-bin/freepublish/submit", new { media_id = mediaId }, ct);
		var errcode = GetErrCode(doc);
		if (errcode != 0)
		{
			throw new WeChatMpException(DescribeError(errcode, GetErrMsg(doc), "publish"), errcode);
		}

		return doc.RootElement.TryGetProperty("publish_id", out var p) ? p.GetString() : null;
	}

	#endregion

	#region 内容转换

	/// <summary>
	/// 把博客文章转成微信图文能接受的 HTML：
	/// Markdown 先转 HTML；去掉微信不支持的标签/属性（script、iframe、视频、表单等整段删，
	/// figure/font 等只拆标签留内容）；正文图片逐张下载后上传成微信素材并替换地址。
	/// 返回：微信 HTML、警告列表、首图字节（做封面用）及文件名/类型。
	/// </summary>
	private async Task<(string Html, List<string> Warnings, byte[] CoverBytes, string CoverFileName, string CoverContentType)> ConvertToWeChatHtmlAsync(Post post, string articleUrl, CancellationToken ct)
	{
		var warnings = new List<string>();
		var rawHtml = post.ContentType == ArticleContentType.Markdown
			? Markdown.ToHtml(post.Content ?? string.Empty, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build())
			: post.Content ?? string.Empty;

		var parser = new HtmlParser();
		using var document = parser.ParseDocument(rawHtml);

		// 1. 清理标签：不支持的整段删，只拆标签的保留内容
		foreach (var element in document.All.ToList())
		{
			var tag = element.TagName.ToLowerInvariant();
			if (RemoveTags.Contains(tag))
			{
				element.Remove();
				continue;
			}

			if (UnwrapTags.Contains(tag))
			{
				element.ReplaceWith(element.ChildNodes.ToArray());
				continue;
			}

			// 2. 清理属性：事件、data-*、class/id 一律去掉；只保留少量白名单属性
			foreach (var attr in element.Attributes.Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || a.Name.StartsWith("data-", StringComparison.OrdinalIgnoreCase) || a.Name.Equals("class", StringComparison.OrdinalIgnoreCase) || a.Name.Equals("id", StringComparison.OrdinalIgnoreCase)).ToList())
			{
				element.RemoveAttribute(attr.Name);
			}

			// 3. 链接：javascript:/data: 开头的 href 去掉，只留文字
			if (tag == "a")
			{
				var href = element.GetAttribute("href");
				if (!string.IsNullOrWhiteSpace(href) && (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)))
				{
					element.RemoveAttribute("href");
				}
			}
		}

		// 4. 正文图片：逐张上传成微信素材并替换地址；首图留着做封面
		byte[] coverBytes = null;
		string coverFileName = null, coverContentType = null;
		foreach (var img in document.QuerySelectorAll("img").ToList())
		{
			var src = img.GetAttribute("src");
			if (string.IsNullOrWhiteSpace(src))
			{
				img.Remove();
				continue;
			}

			if (src.Contains("mmbiz.qpic.cn", StringComparison.OrdinalIgnoreCase))
			{
				continue; // 已经是微信图片地址，不用再传
			}

			var (bytes, fileName, contentType, downloadError) = await DownloadImageAsync(src, articleUrl, ct);
			if (bytes == null)
			{
				warnings.Add($"正文图片上传失败（{Truncate(src, 80)}）：{downloadError}图片保留原链接，微信里可能不显示。");
				continue;
			}

			coverBytes ??= bytes;
			coverFileName ??= fileName;
			coverContentType ??= contentType;

			try
			{
				var wechatUrl = await UploadContentImageAsync(bytes, fileName, contentType, ct);
				if (!string.IsNullOrWhiteSpace(wechatUrl))
				{
					img.SetAttribute("src", wechatUrl);
				}
			}
			catch (WeChatMpException ex)
			{
				warnings.Add($"正文图片上传失败（{Truncate(src, 80)}）：{ex.Message}图片保留原链接，微信里可能不显示。");
			}
		}

		return (document.Body?.InnerHtml ?? string.Empty, warnings, coverBytes, coverFileName, coverContentType);
	}

	/// <summary>
	/// 封面 media_id：优先用文章首图上传永久素材；首图上传失败或文章没图片时，
	/// 用配置的 DefaultThumbMediaId；两者都没有就报错（微信图文必须有封面）。
	/// </summary>
	private async Task<string> ResolveThumbMediaIdAsync(byte[] coverBytes, string coverFileName, string coverContentType, List<string> warnings, CancellationToken ct)
	{
		if (coverBytes is { Length: > 0 })
		{
			try
			{
				var mediaId = await UploadThumbAsync(coverBytes, coverFileName, coverContentType, ct);
				if (!string.IsNullOrWhiteSpace(mediaId))
				{
					return mediaId;
				}
			}
			catch (WeChatMpException ex)
			{
				warnings.Add("封面图上传失败（" + ex.Message + "），改用默认封面。");
			}
		}

		var defaultThumb = Config.DefaultThumbMediaId;
		if (!string.IsNullOrWhiteSpace(defaultThumb))
		{
			return defaultThumb.Trim();
		}

		throw new WeChatMpException("这篇文章没有图片、也没有配置默认封面：微信图文必须有封面图。请在文章里加一张图片，或在 appsettings.json 的 WeChatMp.DefaultThumbMediaId 里填一个永久素材 media_id（先在公众号后台「素材管理 - 图片」上传封面再取 media_id）。");
	}

	/// <summary>
	/// 下载一张正文图片：支持 data: 图片（base64 解码）和相对地址（按站点域名补全）。
	/// 超过 10MB、不是图片、下载失败时返回 null 并给出原因。
	/// </summary>
	private async Task<(byte[] Bytes, string FileName, string ContentType, string Error)> DownloadImageAsync(string src, string articleUrl, CancellationToken ct)
	{
		try
		{
			if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
			{
				var comma = src.IndexOf(',');
				if (comma < 0)
				{
					return (null, null, null, "图片地址格式不对。");
				}

				var mime = src[5..comma].Split(';')[0];
				var bytes = Convert.FromBase64String(src[(comma + 1)..]);
				if (bytes.Length > MaxContentImageBytes)
				{
					return (null, null, null, "图片超过 10MB。");
				}

				return (bytes, "image" + ExtensionByMime(mime), mime, null);
			}

			var absoluteUrl = ResolveImageUrl(src, articleUrl);
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(15));
			using var response = await _http.GetAsync(absoluteUrl, timeout.Token);
			if (!response.IsSuccessStatusCode)
			{
				return (null, null, null, $"下载失败（HTTP {(int)response.StatusCode}）。");
			}

			var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
			if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
			{
				return (null, null, null, "地址返回的不是图片。");
			}

			var imageBytes = await response.Content.ReadAsByteArrayAsync(ct);
			if (imageBytes.Length > MaxContentImageBytes)
			{
				return (null, null, null, "图片超过 10MB。");
			}

			var fileName = "image" + ExtensionByMime(contentType);
			try
			{
				var uriFileName = Path.GetFileName(new Uri(absoluteUrl).LocalPath);
				if (!string.IsNullOrWhiteSpace(uriFileName) && uriFileName.Contains('.'))
				{
					fileName = uriFileName;
				}
			}
			catch (UriFormatException)
			{
				// 用默认文件名
			}

			return (imageBytes, fileName, contentType, null);
		}
		catch (TaskCanceledException)
		{
			return (null, null, null, "下载超时（15 秒）。");
		}
		catch (Exception ex) when (ex is HttpRequestException or FormatException or UriFormatException)
		{
			return (null, null, null, "下载失败（" + ex.Message + "）。");
		}
	}

	/// <summary>相对图片地址按站点域名补全成绝对地址；// 开头的补 https:</summary>
	private static string ResolveImageUrl(string src, string articleUrl)
	{
		if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			return src;
		}

		if (src.StartsWith("//", StringComparison.Ordinal))
		{
			return "https:" + src;
		}

		string domain = null;
		if (CommonHelper.SystemSettings.TryGetValue("Domain", out var configured) && !string.IsNullOrWhiteSpace(configured))
		{
			domain = configured.Split('|')[0].Trim();
		}

		if (string.IsNullOrWhiteSpace(domain) && Uri.TryCreate(articleUrl, UriKind.Absolute, out var articleUri))
		{
			domain = articleUri.Host;
		}

		if (string.IsNullOrWhiteSpace(domain))
		{
			return src;
		}

		return "https://" + domain.TrimEnd('/') + "/" + src.TrimStart('/');
	}

	/// <summary>摘要：正文纯文本去空白后截取 120 字；正文为空时不填（微信会用正文开头自动生成）</summary>
	private static string BuildDigest(string html)
	{
		var parser = new HtmlParser();
		using var document = parser.ParseDocument(html ?? string.Empty);
		var text = Regex.Replace(document.Body?.TextContent ?? string.Empty, @"\s+", " ").Trim();
		return Truncate(text, MaxDigestLength);
	}

	#endregion

	#region 小工具

	private static int GetErrCode(JsonDocument doc) => doc.RootElement.TryGetProperty("errcode", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;

	private static string GetErrMsg(JsonDocument doc) => doc.RootElement.TryGetProperty("errmsg", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : string.Empty;

	/// <summary>发表成功后从 freepublish/get 的 article_detail 里取第一篇文章的链接</summary>
	private static string GetFirstArticleUrl(JsonElement root)
	{
		if (root.TryGetProperty("article_detail", out var detail) && detail.TryGetProperty("item", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
		{
			var first = items[0];
			if (first.TryGetProperty("article_url", out var url) && url.ValueKind == JsonValueKind.String)
			{
				return url.GetString();
			}
		}

		return null;
	}

	/// <summary>按 MIME 类型取文件扩展名（上传图片用）</summary>
	private static string ExtensionByMime(string mime) => mime?.ToLowerInvariant() switch
	{
		"image/jpeg" => ".jpg",
		"image/png" => ".png",
		"image/gif" => ".gif",
		"image/webp" => ".webp",
		"image/bmp" => ".bmp",
		_ => ".jpg"
	};

	private static string Truncate(string value, int maxLength) => string.IsNullOrEmpty(value) || value.Length <= maxLength ? value ?? string.Empty : value[..maxLength];

	/// <summary>把微信错误码翻译成中文说明；context 区分是哪个环节（token/draft/publish/uploadimg/thumb）</summary>
	private static string DescribeError(int errcode, string errmsg, string context) => errcode switch
	{
		40001 => "access_token 无效或 AppSecret 错误（40001）：请检查 appsettings.json 里 WeChatMp 的 AppId、AppSecret 是否正确。",
		40013 => "AppID 无效（40013）：请检查配置里的 AppId 是否与公众号后台「开发 - 基本配置」里的 AppID 一致。",
		40125 => "AppSecret 无效（40125）：请在公众号后台「开发 - 基本配置」重置 AppSecret 后更新服务器配置。",
		40164 => "调用接口的 IP 不在公众号 IP 白名单里（40164）：请把服务器出口 IP 加进公众号后台「开发 - 基本配置 - IP 白名单」。",
		42001 => "access_token 已过期（42001）：已自动刷新重试仍失败，请稍后重试。",
		45009 => "接口调用频率超限（45009）：今日调用次数已用完，请明天再试（每月有 10 次清零机会）。",
		48001 => context == "publish"
			? "账号没有发布接口权限（48001）：个人订阅号自 2025 年 7 月起已被微信回收发布接口权限，不能通过接口自动发表；草稿已保留在草稿箱，请在公众号后台或「公众号助手」App 手动发表/群发。"
			: "接口没有权限（48001）：请确认公众号已开通对应接口权限；个人订阅号的草稿、图片上传接口一般可用，如持续报 48001 请在公众号后台检查接口权限设置。",
		40007 => "media_id 不合法（40007）：封面请用永久素材的 media_id（material/add_material 返回的），不要用草稿 media_id。",
		_ => $"微信接口返回错误 {errcode}：{errmsg}"
	};

	#endregion
}

/// <summary>微信公众号接口调用失败（配置问题或微信返回错误码）</summary>
public class WeChatMpException : Exception
{
	public WeChatMpException(string message, int errorCode = 0) : base(message)
	{
		ErrorCode = errorCode;
	}

	/// <summary>微信返回的 errcode；配置类错误为 0</summary>
	public int ErrorCode { get; }
}
