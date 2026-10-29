using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Masuit.LuceneEFCore.SearchEngine;
using Masuit.LuceneEFCore.SearchEngine.Interfaces;
using Masuit.MyBlogs.Core.Extensions.Firewall;
using Masuit.Tools.Html;
using Masuit.Tools.Logging;
using Microsoft.AspNetCore.Authorization;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 博客 MCP 服务端：供外部 AI 智能体通过 Model Context Protocol（Streamable HTTP）读取与发布博客文章。
/// 单端点 POST /api/mcp，处理 JSON-RPC 2.0（initialize、ping、tools/list、tools/call），普通 JSON 响应、无 SSE 流。
/// 路径特意选 /api/mcp 而不是 /mcp：端口转发路径代理规则 Path=/mcp 会把 /mcp 及其子路径的请求
/// 代理到 MT5 的 MCP 服务并短路博客管道，挂在 /mcp 下会被转发走；/api/mcp 不命中该规则（需精确相等或以「/mcp/」开头）。
/// 鉴权：Authorization: Bearer 令牌，令牌值存系统设置 McpPublishToken（后台「系统设置」页可自助修改）；
/// 未配置令牌时接口一律拒绝，令牌比较使用恒定时间比较。
/// 开关：系统设置 McpPublishEnabled（后台「系统设置」页的 MCP 配置卡片维护）；该键缺失时沿用旧行为（配了令牌即启用），
/// 键存在且值不为 true 时接口一律返回 503 关闭提示，与令牌是否配置无关。
/// </summary>
[Route("api/mcp")]
public sealed class McpController : BaseController
{
	/// <summary>
	/// MCP 发布令牌在系统设置中的键名
	/// </summary>
	private const string TokenSettingKey = "McpPublishToken";

	/// <summary>
	/// MCP 启用开关在系统设置中的键名（值为 true/false；键缺失表示未在后台保存过开关，沿用「配了令牌即启用」的旧行为）
	/// </summary>
	private const string EnabledSettingKey = "McpPublishEnabled";

	/// <summary>
	/// 单次请求体上限（字节）：需容纳 upload_image 的 base64 图片（图片上限 20MB，base64 约 27MB），留足余量
	/// </summary>
	private const long MaxRequestBodyBytes = 42 * 1024 * 1024;

	/// <summary>
	/// 图片大小上限（字节），与站内 UEditor 上传配置 imageMaxSize 一致
	/// </summary>
	private const int MaxImageBytes = 20480000;

	/// <summary>
	/// 允许上传的图片扩展名，与站内 UEditor 配置 imageAllowFiles 一致（另补带点的 .webp）
	/// </summary>
	private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp"
	};

	/// <summary>
	/// JSON 序列化选项：不转义中文等非 ASCII 字符，便于智能体与日志阅读
	/// </summary>
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <summary>
	/// 文章服务
	/// </summary>
	public IPostService PostService { get; set; }

	/// <summary>
	/// 分类服务
	/// </summary>
	public ICategoryService CategoryService { get; set; }

	/// <summary>
	/// 文章标签服务
	/// </summary>
	public IPostTagService PostTagService { get; set; }

	/// <summary>
	/// 搜索引擎（保存文章时同步维护 Lucene 索引，与后台发文路径一致）
	/// </summary>
	public ISearchEngine<DataContext> SearchEngine { get; set; }

	/// <summary>
	/// 图床客户端（与站内上传一致：优先图床，失败回落本地存储）
	/// </summary>
	public ImagebedClient ImagebedClient { get; set; }

	/// <summary>
	/// Web 宿主环境（定位 wwwroot 本地图片目录）
	/// </summary>
	public IWebHostEnvironment HostEnvironment { get; set; }

	/// <summary>
	/// MCP 协议入口：校验令牌后按 JSON-RPC 方法分发处理
	/// </summary>
	/// <param name="cancellationToken">请求取消令牌</param>
	/// <returns>JSON-RPC 响应；通知类消息返回 202 无正文</returns>
	[HttpPost, AllowAnonymous, AllowAccessFirewall]
	public async Task<ActionResult> Handle(CancellationToken cancellationToken)
	{
		var enabledSetting = CommonHelper.SystemSettings.GetOrAdd(EnabledSettingKey, "");
		if (!string.IsNullOrEmpty(enabledSetting) && !string.Equals(enabledSetting.Trim(), "true", StringComparison.OrdinalIgnoreCase))
		{
			return new JsonResult(new { error = "MCP 接口未启用：管理员已在后台「系统设置」中关闭 MCP 智能体发布" }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
		}

		var configuredToken = CommonHelper.SystemSettings.GetOrAdd(TokenSettingKey, "");
		if (string.IsNullOrWhiteSpace(configuredToken))
		{
			return new JsonResult(new { error = "MCP 接口未启用：管理员尚未在后台「系统设置」中配置 MCP 发布令牌（McpPublishToken）" }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
		}

		if (!IsAuthorized(configuredToken))
		{
			Response.Headers.WWWAuthenticate = "Bearer";
			return new JsonResult(new { error = "未授权：缺少或错误的 Bearer 令牌" }) { StatusCode = StatusCodes.Status401Unauthorized };
		}

		if (Request.ContentLength is > MaxRequestBodyBytes)
		{
			return new JsonResult(new { error = "请求体过大" }) { StatusCode = StatusCodes.Status413PayloadTooLarge };
		}

		string body;
		using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
		{
			body = await reader.ReadToEndAsync(cancellationToken);
		}

		if (string.IsNullOrWhiteSpace(body) || body.Length > MaxRequestBodyBytes)
		{
			return RpcError(null, -32700, "请求体为空或过大，无法解析");
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(body);
		}
		catch (JsonException)
		{
			return RpcError(null, -32700, "JSON 解析失败");
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return RpcError(null, -32600, "无效的请求：仅支持单个 JSON-RPC 请求，不支持批量请求");
			}

			var method = root.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String ? methodElement.GetString() : null;
			if (string.IsNullOrEmpty(method))
			{
				return RpcError(null, -32600, "无效的请求：缺少 method");
			}

			var hasId = root.TryGetProperty("id", out var idElement) && idElement.ValueKind != JsonValueKind.Null;
			if (!hasId)
			{
				// 通知消息（含 notifications/initialized）：无需响应，202 收下即可
				return StatusCode(StatusCodes.Status202Accepted);
			}

			var id = idElement.Clone();
			try
			{
				switch (method)
				{
					case "initialize":
						return RpcResult(id, BuildInitializeResult(root));
					case "ping":
						return RpcResult(id, new Dictionary<string, object>());
					case "tools/list":
						return RpcResult(id, new Dictionary<string, object> { ["tools"] = BuildToolsList() });
					case "tools/call":
						return await CallToolAsync(id, root, cancellationToken);
					default:
						return RpcError(id, -32601, $"不支持的方法：{method}");
				}
			}
			catch (Exception e)
			{
				LogManager.Error(e);
				return RpcError(id, -32603, "服务器内部错误");
			}
		}
	}

	/// <summary>
	/// 校验 Authorization 头中的 Bearer 令牌，与配置令牌做恒定时间比较，避免时序侧信道
	/// </summary>
	/// <param name="configuredToken">系统设置中配置的令牌</param>
	/// <returns>令牌正确返回 true</returns>
	private bool IsAuthorized(string configuredToken)
	{
		var authorization = Request.Headers.Authorization.ToString();
		if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var providedBytes = Encoding.UTF8.GetBytes(authorization["Bearer ".Length..].Trim());
		var configuredBytes = Encoding.UTF8.GetBytes(configuredToken.Trim());
		return providedBytes.Length == configuredBytes.Length && CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
	}

	/// <summary>
	/// 构造 initialize 响应：协议版本优先回显客户端在 initialize 中声明的版本，未声明时用 2025-03-26
	/// </summary>
	/// <param name="root">JSON-RPC 请求根元素</param>
	/// <returns>initialize 结果对象</returns>
	private static Dictionary<string, object> BuildInitializeResult(JsonElement root)
	{
		var protocolVersion = "2025-03-26";
		if (root.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object
			&& parameters.TryGetProperty("protocolVersion", out var versionElement) && versionElement.ValueKind == JsonValueKind.String
			&& !string.IsNullOrWhiteSpace(versionElement.GetString()))
		{
			protocolVersion = versionElement.GetString();
		}

		return new Dictionary<string, object>
		{
			["protocolVersion"] = protocolVersion,
			["capabilities"] = new Dictionary<string, object>
			{
				["tools"] = new Dictionary<string, object> { ["listChanged"] = false }
			},
			["serverInfo"] = new Dictionary<string, object>
			{
				["name"] = "masuit-myblogs-mcp",
				["version"] = "1.0.0"
			}
		};
	}

	/// <summary>
	/// 构造 tools/list 的工具定义：名称、中文说明与入参 JSON Schema
	/// </summary>
	/// <returns>工具定义数组</returns>
	private static JsonArray BuildToolsList()
	{
		var tools = new JsonArray
		{
			new JsonObject
			{
				["name"] = "list_categories",
				["description"] = "列出博客全部可用文章分类（id、名称、父级id），发布文章前先调用它确认分类 id 或名称。",
				["inputSchema"] = Schema(new JsonObject())
			},
			new JsonObject
			{
				["name"] = "list_posts",
				["description"] = "分页列出博客文章，可按标题关键词、分类、状态过滤，返回 id、标题、状态、发布时间与链接，供查重与定位文章。",
				["inputSchema"] = Schema(new JsonObject
				{
					["keyword"] = Prop("string", "标题关键词（模糊匹配），可选"),
					["categoryId"] = Prop("integer", "分类 id 过滤，可选"),
					["status"] = Prop("string", "状态过滤：published（已发布）、draft（草稿/待审核）、takedown（已下架），不传表示全部", ["published", "draft", "takedown"]),
					["page"] = Prop("integer", "页码，从 1 开始，默认 1"),
					["pageSize"] = Prop("integer", "每页条数，默认 20，最大 50")
				})
			},
			new JsonObject
			{
				["name"] = "get_post",
				["description"] = "按 id 读取单篇文章全文（标题、内容、内容格式、分类、标签、关键词、状态、发布时间、链接），改稿前先用它读原文。",
				["inputSchema"] = Schema(new JsonObject
				{
					["id"] = Prop("integer", "文章 id")
				}, "id")
			},
			new JsonObject
			{
				["name"] = "list_tags",
				["description"] = "列出博客已有标签及各自文章数（按文章数降序，最多 200 个），写文章选标签时优先复用已有标签，不要乱造新标签。",
				["inputSchema"] = Schema(new JsonObject())
			},
			new JsonObject
			{
				["name"] = "publish_post",
				["description"] = "发布一篇新文章。内容默认按 Markdown 格式（contentType=markdown），也可传 contentType=html 提交 HTML 富文本。分类用 categoryId 或 categoryName 二选一（先调 list_categories 确认）。status=published 立即发布（默认），status=draft 存为草稿（待审核）不公开展示。",
				["inputSchema"] = Schema(new JsonObject
				{
					["title"] = Prop("string", "文章标题，必填，最长 128 字"),
					["content"] = Prop("string", "文章正文，必填，最长 1000000 字；格式由 contentType 决定"),
					["contentType"] = Prop("string", "内容格式：markdown（默认）或 html", ["markdown", "html"]),
					["categoryId"] = Prop("integer", "分类 id，与 categoryName 二选一"),
					["categoryName"] = Prop("string", "分类名称（精确匹配），与 categoryId 二选一"),
					["tags"] = Prop("string", "标签，多个用英文逗号分隔，如 \"C#,教程\"，最长 255 字，可选"),
					["keywords"] = Prop("string", "文章关键词（SEO），多个用英文逗号分隔，最长 255 字，可选"),
					["status"] = Prop("string", "published 立即发布（默认）；draft 存草稿", ["published", "draft"])
				}, "title", "content")
			},
			new JsonObject
			{
				["name"] = "update_post",
				["description"] = "按 id 修改文章：只更新传入的字段，未传字段保持不变。可改标题、内容（同时可传 contentType）、分类（categoryId 或 categoryName）、标签、关键词与状态；status 可为 published（发布/上架）、draft（退回草稿）、takedown（下架）。本接口不提供删除。",
				["inputSchema"] = Schema(new JsonObject
				{
					["id"] = Prop("integer", "文章 id"),
					["title"] = Prop("string", "新标题，最长 128 字，可选"),
					["content"] = Prop("string", "新正文，最长 1000000 字，可选；格式由 contentType 决定，未传 contentType 时沿用文章原格式"),
					["contentType"] = Prop("string", "内容格式：markdown 或 html，可选", ["markdown", "html"]),
					["categoryId"] = Prop("integer", "新分类 id，与 categoryName 二选一，可选"),
					["categoryName"] = Prop("string", "新分类名称（精确匹配），与 categoryId 二选一，可选"),
					["tags"] = Prop("string", "新标签，多个用英文逗号分隔；传空字符串表示清空标签，可选"),
					["keywords"] = Prop("string", "新关键词，多个用英文逗号分隔，可选"),
					["status"] = Prop("string", "published 发布/上架；draft 退回草稿；takedown 下架，可选", ["published", "draft", "takedown"])
				}, "id")
			},
			new JsonObject
			{
				["name"] = "upload_image",
				["description"] = "上传一张图片（base64），返回可直接写进文章正文的图片 URL。存储与站内上传一致：优先图床、失败回落本站 /upload/images/。仅支持 png/jpg/jpeg/gif/bmp/ico/webp，大小不超过 20MB。",
				["inputSchema"] = Schema(new JsonObject
				{
					["fileName"] = Prop("string", "文件名（含扩展名），如 cover.png"),
					["dataBase64"] = Prop("string", "图片内容的 base64 编码（可带 data:image/...;base64, 前缀）")
				}, "fileName", "dataBase64")
			}
		};
		return tools;
	}

	/// <summary>
	/// 构造一个字符串/整数参数的 JSON Schema 片段
	/// </summary>
	/// <param name="type">JSON Schema 类型名</param>
	/// <param name="description">参数说明</param>
	/// <param name="enumValues">可选的枚举取值，非空时写入 enum</param>
	/// <returns>参数 Schema 节点</returns>
	private static JsonObject Prop(string type, string description, string[] enumValues = null)
	{
		var prop = new JsonObject
		{
			["type"] = type,
			["description"] = description
		};
		if (enumValues is { Length: > 0 })
		{
			var values = new JsonArray();
			foreach (var value in enumValues)
			{
				values.Add(value);
			}

			prop["enum"] = values;
		}

		return prop;
	}

	/// <summary>
	/// 构造工具入参的整体 JSON Schema（object 类型）
	/// </summary>
	/// <param name="properties">参数定义集合</param>
	/// <param name="required">必填参数名</param>
	/// <returns>入参 Schema 节点</returns>
	private static JsonObject Schema(JsonObject properties, params string[] required)
	{
		var schema = new JsonObject
		{
			["type"] = "object",
			["properties"] = properties
		};
		if (required is { Length: > 0 })
		{
			var list = new JsonArray();
			foreach (var name in required)
			{
				list.Add(name);
			}

			schema["required"] = list;
		}

		return schema;
	}

	/// <summary>
	/// 分发 tools/call：按工具名执行并把结果包装成 MCP 工具结果（content 文本块）；
	/// 工具自身的校验错误以 isError=true 返回给智能体，不抛到协议层
	/// </summary>
	/// <param name="id">JSON-RPC 请求 id</param>
	/// <param name="root">JSON-RPC 请求根元素</param>
	/// <param name="cancellationToken">请求取消令牌</param>
	/// <returns>JSON-RPC 响应</returns>
	private async Task<ActionResult> CallToolAsync(JsonElement id, JsonElement root, CancellationToken cancellationToken)
	{
		if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object
			|| !parameters.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
		{
			return RpcError(id, -32602, "参数错误：tools/call 需要 params.name");
		}

		var toolName = nameElement.GetString();
		var args = parameters.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Object
			? argumentsElement
			: default;
		try
		{
			object payload = toolName switch
			{
				"list_categories" => ListCategories(),
				"list_posts" => ListPosts(args),
				"get_post" => GetPost(args),
				"list_tags" => ListTags(),
				"publish_post" => await PublishPostAsync(args, cancellationToken),
				"update_post" => await UpdatePostAsync(args, cancellationToken),
				"upload_image" => await UploadImageAsync(args, cancellationToken),
				_ => throw new McpToolException($"未知工具：{toolName}")
			};
			return RpcResult(id, ToolResult(payload, false));
		}
		catch (McpToolException e)
		{
			return RpcResult(id, ToolResult(e.Message, true));
		}
		catch (Exception e)
		{
			LogManager.Error(e);
			return RpcResult(id, ToolResult("工具执行失败：服务器内部错误", true));
		}
	}

	/// <summary>
	/// list_categories：列出全部可用分类
	/// </summary>
	/// <returns>分类数组（id、名称、父级id、描述）</returns>
	private object ListCategories()
	{
		var categories = CategoryService.GetQueryNoTracking(c => c.Status == Status.Available)
			.OrderBy(c => c.Id)
			.Select(c => new { c.Id, c.Name, c.ParentId, c.Description })
			.ToList();
		return new { categories };
	}

	/// <summary>
	/// list_tags：列出已有标签及文章数，按文章数降序取前 200
	/// </summary>
	/// <returns>标签数组（名称、文章数）</returns>
	private object ListTags()
	{
		var tags = PostService.GetTags()
			.OrderByDescending(t => t.Value)
			.Take(200)
			.Select(t => new { name = t.Key, count = t.Value })
			.ToList();
		return new { tags };
	}

	/// <summary>
	/// list_posts：分页列文章，支持标题关键词、分类与状态过滤
	/// </summary>
	/// <param name="args">工具参数</param>
	/// <returns>分页结果（total、page、pageSize、posts 数组）</returns>
	private object ListPosts(JsonElement args)
	{
		var page = Math.Max(1, GetInt(args, "page") ?? 1);
		var pageSize = Math.Clamp(GetInt(args, "pageSize") ?? 20, 1, 50);
		IQueryable<Post> query = PostService.GetQueryNoTracking(p => true).Include(p => p.Category);
		if (GetString(args, "keyword")?.Trim() is { Length: > 0 } keyword)
		{
			query = query.Where(p => p.Title.Contains(keyword));
		}

		if (GetInt(args, "categoryId") is { } categoryId)
		{
			query = query.Where(p => p.CategoryId == categoryId);
		}

		if (GetString(args, "status") is { } statusText)
		{
			var status = MapStatus(statusText) ?? throw new McpToolException($"不支持的状态：{statusText}，可用值：published、draft、takedown");
			query = query.Where(p => p.Status == status);
		}

		var total = query.Count();
		var posts = query.OrderByDescending(p => p.PostDate).ThenByDescending(p => p.Id)
			.Skip((page - 1) * pageSize).Take(pageSize)
			.ToList()
			.Select(p => new
			{
				id = p.Id,
				title = p.Title,
				status = StatusToken(p.Status),
				categoryId = p.CategoryId,
				categoryName = p.Category?.Name,
				postDate = p.PostDate.ToString("yyyy-MM-dd HH:mm:ss"),
				url = PostUrl(p.Id)
			})
			.ToList();
		return new { total, page, pageSize, posts };
	}

	/// <summary>
	/// get_post：按 id 读取单篇文章全文
	/// </summary>
	/// <param name="args">工具参数（id 必填）</param>
	/// <returns>文章完整信息</returns>
	private object GetPost(JsonElement args)
	{
		var id = GetInt(args, "id") ?? throw new McpToolException("缺少参数：id");
		var post = PostService.GetQueryNoTracking(p => p.Id == id).Include(p => p.Category).FirstOrDefault()
			?? throw new McpToolException($"文章不存在：id={id}");
		return new
		{
			id = post.Id,
			title = post.Title,
			content = post.Content,
			contentType = post.ContentType == ArticleContentType.Markdown ? "markdown" : "html",
			categoryId = post.CategoryId,
			categoryName = post.Category?.Name,
			tags = post.Label,
			keywords = post.Keyword,
			status = StatusToken(post.Status),
			author = post.Author,
			postDate = post.PostDate.ToString("yyyy-MM-dd HH:mm:ss"),
			modifyDate = post.ModifyDate.ToString("yyyy-MM-dd HH:mm:ss"),
			url = PostUrl(post.Id)
		};
	}

	/// <summary>
	/// publish_post：组装文章并走与后台发文相同的保存路径（PostService.AddEntity + 标签维护 + SearchEngine.SaveChangesAsync 同步索引）
	/// </summary>
	/// <param name="args">工具参数</param>
	/// <param name="cancellationToken">请求取消令牌</param>
	/// <returns>新文章 id、链接与状态</returns>
	private async Task<object> PublishPostAsync(JsonElement args, CancellationToken cancellationToken)
	{
		var title = GetString(args, "title")?.Trim();
		if (string.IsNullOrEmpty(title))
		{
			throw new McpToolException("标题不能为空");
		}

		if (title.Length > 128)
		{
			throw new McpToolException("标题最长 128 个字符");
		}

		var content = GetString(args, "content");
		if (string.IsNullOrWhiteSpace(content))
		{
			throw new McpToolException("内容不能为空");
		}

		if (content.Length > 1000000)
		{
			throw new McpToolException("内容最长 1000000 个字符");
		}

		var category = ResolveCategory(args);
		var label = NormalizeLabel(GetString(args, "tags"));
		var keywords = NormalizeLabel(GetString(args, "keywords"));
		var status = MapStatus(GetString(args, "status") ?? "published");
		if (status is not (Status.Published or Status.Pending))
		{
			throw new McpToolException("publish_post 的 status 只支持 published（发布）或 draft（草稿）");
		}

		var contentType = ParseContentType(GetString(args, "contentType"), ArticleContentType.Markdown);
		var (author, email) = ResolveAuthor();
		var post = new Post
		{
			Title = title,
			Author = author,
			Modifier = author,
			Email = email,
			ModifierEmail = email,
			Content = await PrepareContentAsync(content, contentType, cancellationToken),
			ContentType = contentType,
			CategoryId = category.Id,
			Label = label,
			Keyword = keywords,
			Status = status.Value,
			IP = ClientIP.ToString(),
			Rss = true
		};
		PostService.AddEntity(post);
		SyncTags(post);
		if (await SearchEngine.SaveChangesAsync() <= 0)
		{
			throw new McpToolException("文章保存失败");
		}

		return new
		{
			id = post.Id,
			title = post.Title,
			status = StatusToken(post.Status),
			url = PostUrl(post.Id)
		};
	}

	/// <summary>
	/// update_post：只更新传入的字段，未传字段保持不变；内容、分类、标签、状态的处理与后台编辑页保持一致
	/// </summary>
	/// <param name="args">工具参数</param>
	/// <param name="cancellationToken">请求取消令牌</param>
	/// <returns>更新后的文章 id、链接与状态</returns>
	private async Task<object> UpdatePostAsync(JsonElement args, CancellationToken cancellationToken)
	{
		var id = GetInt(args, "id") ?? throw new McpToolException("缺少参数：id");
		var post = await PostService.GetByIdAsync(id) ?? throw new McpToolException($"文章不存在：id={id}");
		if (GetString(args, "title")?.Trim() is { } title)
		{
			if (title.Length == 0 || title.Length > 128)
			{
				throw new McpToolException("标题长度需在 1 到 128 个字符之间");
			}

			post.Title = title;
		}

		if (HasProperty(args, "categoryId") || HasProperty(args, "categoryName"))
		{
			post.CategoryId = ResolveCategory(args).Id;
		}

		if (GetString(args, "content") is { } content)
		{
			if (string.IsNullOrWhiteSpace(content) || content.Length > 1000000)
			{
				throw new McpToolException("内容不能为空且最长 1000000 个字符");
			}

			var contentType = ParseContentType(GetString(args, "contentType"), post.ContentType);
			post.Content = await PrepareContentAsync(content, contentType, cancellationToken);
			post.ContentType = contentType;
		}

		var labelChanged = false;
		if (HasProperty(args, "tags"))
		{
			post.Label = NormalizeLabel(GetString(args, "tags"));
			labelChanged = true;
		}

		if (HasProperty(args, "keywords"))
		{
			post.Keyword = NormalizeLabel(GetString(args, "keywords"));
		}

		if (GetString(args, "status") is { } statusText)
		{
			post.Status = MapStatus(statusText) ?? throw new McpToolException($"不支持的状态：{statusText}，可用值：published、draft、takedown");
		}

		var (author, email) = ResolveAuthor();
		post.Modifier = author;
		post.ModifierEmail = email;
		post.ModifyDate = DateTime.Now;
		post.IP = ClientIP.ToString();
		if (labelChanged)
		{
			SyncTags(post);
		}

		if (await SearchEngine.SaveChangesAsync() <= 0)
		{
			throw new McpToolException("文章保存失败或没有变化");
		}

		return new
		{
			id = post.Id,
			title = post.Title,
			status = StatusToken(post.Status),
			url = PostUrl(post.Id)
		};
	}

	/// <summary>
	/// upload_image：保存 base64 图片，路径与站内上传（UploadController.UploadFile 的图片分支）一致：
	/// 先试图床，失败则落本地 wwwroot 的 {UploadPath}/images/ 目录，文件名用雪花 id 加原扩展名
	/// </summary>
	/// <param name="args">工具参数（fileName、dataBase64 必填）</param>
	/// <param name="cancellationToken">请求取消令牌</param>
	/// <returns>图片的绝对 URL 与相对路径</returns>
	private async Task<object> UploadImageAsync(JsonElement args, CancellationToken cancellationToken)
	{
		var fileName = GetString(args, "fileName")?.Trim();
		if (string.IsNullOrEmpty(fileName))
		{
			throw new McpToolException("缺少参数：fileName");
		}

		var extension = Path.GetExtension(fileName).ToLowerInvariant();
		if (!AllowedImageExtensions.Contains(extension))
		{
			throw new McpToolException($"不支持的图片格式：{extension}，仅支持 png/jpg/jpeg/gif/bmp/ico/webp");
		}

		var dataBase64 = GetString(args, "dataBase64")?.Trim();
		if (string.IsNullOrEmpty(dataBase64))
		{
			throw new McpToolException("缺少参数：dataBase64");
		}

		if (dataBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
		{
			var comma = dataBase64.IndexOf(',');
			if (comma > 0)
			{
				dataBase64 = dataBase64[(comma + 1)..];
			}
		}

		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(dataBase64);
		}
		catch (FormatException)
		{
			throw new McpToolException("dataBase64 不是有效的 base64 内容");
		}

		if (bytes.Length == 0)
		{
			throw new McpToolException("图片内容为空");
		}

		if (bytes.Length > MaxImageBytes)
		{
			throw new McpToolException("图片大小超过 20MB 上限（与站内上传限制一致）");
		}

		await using var stream = new MemoryStream(bytes);
		var (imagebedUrl, success) = await ImagebedClient.UploadImage(stream, fileName, cancellationToken);
		if (success && !string.IsNullOrWhiteSpace(imagebedUrl))
		{
			return new { url = imagebedUrl, path = imagebedUrl, fileName, size = bytes.Length };
		}

		var pathBase = CommonHelper.SystemSettings.GetOrAdd("UploadPath", "upload").Trim('/', '\\');
		var storedName = SnowFlake.GetInstance().GetUniqueId() + extension;
		var directory = Path.Combine(HostEnvironment.WebRootPath, pathBase, "images");
		Directory.CreateDirectory(directory);
		await System.IO.File.WriteAllBytesAsync(Path.Combine(directory, storedName), bytes, cancellationToken);
		var relativePath = $"/{pathBase}/images/{storedName}";
		return new
		{
			url = $"{Request.Scheme}://{Request.Host}{relativePath}",
			path = relativePath,
			fileName,
			size = bytes.Length
		};
	}

	/// <summary>
	/// 按参数定位分类：categoryId 优先，其次 categoryName 精确匹配；都必须存在且处于可用状态
	/// </summary>
	/// <param name="args">工具参数</param>
	/// <returns>命中的分类</returns>
	/// <exception cref="McpToolException">未提供分类或分类不存在时抛出</exception>
	private Category ResolveCategory(JsonElement args)
	{
		Category category = null;
		if (GetInt(args, "categoryId") is { } categoryId)
		{
			category = CategoryService.GetQueryNoTracking(c => c.Id == categoryId && c.Status == Status.Available).FirstOrDefault();
			if (category == null)
			{
				throw new McpToolException($"分类不存在或不可用：categoryId={categoryId}，请先调用 list_categories 确认");
			}
		}
		else if (GetString(args, "categoryName")?.Trim() is { Length: > 0 } categoryName)
		{
			category = CategoryService.GetQueryNoTracking(c => c.Name == categoryName && c.Status == Status.Available).FirstOrDefault();
			if (category == null)
			{
				throw new McpToolException($"分类不存在或不可用：categoryName={categoryName}，请先调用 list_categories 确认");
			}
		}

		return category ?? throw new McpToolException("请选择一个分类：传 categoryId 或 categoryName（二选一）");
	}

	/// <summary>
	/// 确定 MCP 发文的作者归属：取站点第一个管理员账号（昵称作作者名，超 24 字截断；邮箱作作者邮箱），与站长本人发文一致
	/// </summary>
	/// <returns>作者名与作者邮箱</returns>
	/// <exception cref="McpToolException">站点没有管理员或管理员未留邮箱时抛出</exception>
	private (string Author, string Email) ResolveAuthor()
	{
		var admin = UserInfoService.GetQuery(u => u.IsAdmin).OrderBy(u => u.Id).FirstOrDefault()
			?? throw new McpToolException("站点尚无管理员账号，无法确定文章作者");
		if (string.IsNullOrWhiteSpace(admin.Email))
		{
			throw new McpToolException("管理员账号未填写邮箱，无法满足文章的作者邮箱必填要求");
		}

		var author = string.IsNullOrWhiteSpace(admin.NickName) ? admin.Username : admin.NickName.Trim();
		if (author.Length > 24)
		{
			author = author[..24];
		}

		return (author, admin.Email);
	}

	/// <summary>
	/// 按内容格式预处理正文：Markdown 仅去首尾空白；HTML 走与访客投稿一致的清洗管线（标准消毒、清理图片属性、本地化图片地址）
	/// </summary>
	/// <param name="content">原始正文</param>
	/// <param name="contentType">内容格式</param>
	/// <param name="cancellationToken">请求取消令牌</param>
	/// <returns>处理后的正文</returns>
	private async Task<string> PrepareContentAsync(string content, ArticleContentType contentType, CancellationToken cancellationToken)
	{
		if (contentType == ArticleContentType.Markdown)
		{
			return content.Trim();
		}

		return await ImagebedClient.ReplaceImgSrc(await content.Trim().HtmlSanitizerStandard().ClearImgAttributes(cancellationToken), cancellationToken);
	}

	/// <summary>
	/// 维护标签表与分词词库，与后台发文/编辑路径一致：按标签名新增或更新 PostTag，并把关键词与标签加入 Lucene 分词词库
	/// </summary>
	/// <param name="post">已保存或待保存的文章（Label 已赋值）</param>
	private void SyncTags(Post post)
	{
		(post.Keyword + "," + post.Label).Split(',', StringSplitOptions.RemoveEmptyEntries).ForEach(KeywordsManager.AddWords);
		PostTagService.AddOrUpdate(t => t.Name, post.Label.AsNotNull().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => new PostTag
		{
			Name = s,
			Count = PostService.Count(t => t.Label.Contains(s) && t.Status == Status.Published)
		}));
	}

	/// <summary>
	/// 生成文章详情页链接（与站内邮件模板等处一致的 Url.Action 生成方式）
	/// </summary>
	/// <param name="postId">文章 id</param>
	/// <returns>文章绝对链接</returns>
	private string PostUrl(int postId) => Url.Action("Details", "Post", new { id = postId }, Request.Scheme);

	/// <summary>
	/// 把对外状态词映射为实体状态枚举：published=已发布，draft=待审核（草稿），takedown=已下架，其余返回 null
	/// </summary>
	/// <param name="status">对外状态词</param>
	/// <returns>实体状态或 null</returns>
	private static Status? MapStatus(string status) => status?.Trim().ToLowerInvariant() switch
	{
		"published" => Status.Published,
		"draft" => Status.Pending,
		"pending" => Status.Pending,
		"takedown" => Status.Takedown,
		_ => null
	};

	/// <summary>
	/// 把实体状态枚举转换为对外状态词（与 MapStatus 互逆，未知状态回落为枚举名小写）
	/// </summary>
	/// <param name="status">实体状态</param>
	/// <returns>对外状态词</returns>
	private static string StatusToken(Status status) => status switch
	{
		Status.Published => "published",
		Status.Pending => "draft",
		Status.Takedown => "takedown",
		_ => status.ToString().ToLowerInvariant()
	};

	/// <summary>
	/// 解析内容格式参数：markdown 与 html/word，其余取值报错；未传时用给定的默认值
	/// </summary>
	/// <param name="value">参数值</param>
	/// <param name="fallback">未传参数时的默认格式</param>
	/// <returns>内容格式枚举</returns>
	/// <exception cref="McpToolException">取值不支持时抛出</exception>
	private static ArticleContentType ParseContentType(string value, ArticleContentType fallback) => value?.Trim().ToLowerInvariant() switch
	{
		null => fallback,
		"markdown" => ArticleContentType.Markdown,
		"html" => ArticleContentType.Word,
		"word" => ArticleContentType.Word,
		_ => throw new McpToolException($"不支持的内容格式：{value}，可用值：markdown、html")
	};

	/// <summary>
	/// 规范化标签/关键词串：中文逗号转英文逗号、逐项去空白；空串归一为 null，最长 255 字
	/// </summary>
	/// <param name="value">原始标签串</param>
	/// <returns>规范化后的标签串或 null</returns>
	/// <exception cref="McpToolException">超过长度上限时抛出</exception>
	private static string NormalizeLabel(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var normalized = string.Join(",", value.Replace("，", ",").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		if (normalized.Length == 0)
		{
			return null;
		}

		return normalized.Length > 255 ? throw new McpToolException("标签/关键词最长 255 个字符") : normalized;
	}

	/// <summary>
	/// 判断工具参数中是否存在指定字段（用于 update_post 区分「未传」与「传空」）
	/// </summary>
	/// <param name="args">工具参数</param>
	/// <param name="name">字段名</param>
	/// <returns>存在返回 true</returns>
	private static bool HasProperty(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out _);

	/// <summary>
	/// 读取工具参数中的字符串字段，缺失或类型不符返回 null
	/// </summary>
	/// <param name="args">工具参数</param>
	/// <param name="name">字段名</param>
	/// <returns>字符串值或 null</returns>
	private static string GetString(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;

	/// <summary>
	/// 读取工具参数中的整数字段，缺失或类型不符返回 null
	/// </summary>
	/// <param name="args">工具参数</param>
	/// <param name="name">字段名</param>
	/// <returns>整数值或 null</returns>
	private static int? GetInt(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
			? value
			: null;

	/// <summary>
	/// 把工具执行结果包装成 MCP 工具结果：payload 序列化为 JSON 文本块；错误时 isError=true 且文本为错误说明
	/// </summary>
	/// <param name="payload">工具结果对象或错误消息字符串</param>
	/// <param name="isError">是否为工具错误</param>
	/// <returns>MCP 工具结果对象</returns>
	private static Dictionary<string, object> ToolResult(object payload, bool isError) => new()
	{
		["content"] = new[]
		{
			new Dictionary<string, object>
			{
				["type"] = "text",
				["text"] = payload is string text ? text : JsonSerializer.Serialize(payload, JsonOptions)
			}
		},
		["isError"] = isError
	};

	/// <summary>
	/// 输出 JSON-RPC 成功响应
	/// </summary>
	/// <param name="id">请求 id（通知场景不会走到这里）</param>
	/// <param name="result">结果对象</param>
	/// <returns>JSON 响应</returns>
	private ActionResult RpcResult(JsonElement? id, object result) => Content(
		JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["jsonrpc"] = "2.0",
			["id"] = id,
			["result"] = result
		}, JsonOptions), "application/json; charset=utf-8");

	/// <summary>
	/// 输出 JSON-RPC 错误响应
	/// </summary>
	/// <param name="id">请求 id，解析失败时为 null</param>
	/// <param name="code">JSON-RPC 错误码</param>
	/// <param name="message">错误说明</param>
	/// <returns>JSON 响应</returns>
	private ActionResult RpcError(JsonElement? id, int code, string message) => Content(
		JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["jsonrpc"] = "2.0",
			["id"] = id,
			["error"] = new Dictionary<string, object>
			{
				["code"] = code,
				["message"] = message
			}
		}, JsonOptions), "application/json; charset=utf-8");

	/// <summary>
	/// 工具层可预期错误（参数校验、资源不存在等）：消息会原样回给智能体，不记为服务器异常
	/// </summary>
	/// <param name="message">给智能体看的错误说明</param>
	private sealed class McpToolException(string message) : Exception(message);
}
