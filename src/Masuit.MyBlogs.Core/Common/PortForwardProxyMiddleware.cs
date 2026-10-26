using System.Net.Http;

namespace Masuit.MyBlogs.Core.Common;

/// <summary>
/// 端口转发路径代理中间件：注册在管道前部（静态文件与 MVC 路由之前），逐请求按转发引擎的路径规则快照匹配。
/// 命中时把请求剥掉规则前缀后透明代理到目标主机端口：方法、请求头（Authorization 等，跳过逐跳头）、请求体原样转发，
/// 响应状态码、响应头、响应体原样回传且逐块冲刷（支持 SSE 长连接，如 MCP 的 Streamable HTTP）。
/// 未命中直接放行、几乎零开销；代理失败（目标连不上等）返回 502 并写日志，不影响博客其他请求。
/// 代理流量计入对应规则的运行时统计（活跃连接数、累计字节数：请求体 + 响应体），与 TCP 转发共用同一套计数
/// </summary>
/// <param name="next"></param>
/// <param name="runtime">端口转发引擎单例，提供路径规则快照与运行时计数</param>
/// <param name="httpClientFactory"></param>
/// <param name="logger"></param>
public sealed class PortForwardProxyMiddleware(RequestDelegate next, PortForwardRuntime runtime, IHttpClientFactory httpClientFactory, ILogger<PortForwardProxyMiddleware> logger)
{
	/// <summary>
	/// 逐跳头（连接级语义，只对当前这一跳有效），转发时跳过；Host 同样跳过，由 HttpClient 按目标地址生成。
	/// 此外请求 Connection 头里点名的头也会一并跳过
	/// </summary>
	private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
	{
		"Connection", "Keep-Alive", "Proxy-Connection", "Proxy-Authorization", "Proxy-Authenticate",
		"TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host"
	};

	/// <summary>
	/// 命中路径规则则代理转发并短路管道，未命中放行给后续中间件
	/// </summary>
	/// <param name="context"></param>
	/// <returns></returns>
	public async Task InvokeAsync(HttpContext context)
	{
		var target = runtime.MatchPath(context.Request.Path.Value);
		if (target == null)
		{
			await next(context);
			return;
		}

		await ProxyAsync(context, target);
	}

	/// <summary>
	/// 执行一次代理：剥掉规则前缀拼目标地址，流式转发请求与响应，全程绑定客户端断开令牌
	/// </summary>
	/// <param name="context"></param>
	/// <param name="target">命中的路径规则快照</param>
	/// <returns></returns>
	private async Task ProxyAsync(HttpContext context, PortForwardPathTarget target)
	{
		var counters = runtime.GetCounters(target.RuleId);
		Interlocked.Increment(ref counters.TotalConnections);
		Interlocked.Increment(ref counters.ActiveConnections);
		try
		{
			var requestPath = context.Request.Path.Value ?? string.Empty;
			var remainder = requestPath.Length == target.Path.Length ? "/" : requestPath[target.Path.Length..];
			var targetUrl = $"http://{target.TargetHost}:{target.TargetPort}{remainder}{context.Request.QueryString}";
			using var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);
			BuildRequestContent(context, requestMessage, counters);
			CopyRequestHeaders(context, requestMessage);
			var client = httpClientFactory.CreateClient(PortForwardRuntime.ProxyHttpClientName);
			using var responseMessage = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
			context.Response.StatusCode = (int)responseMessage.StatusCode;
			CopyResponseHeaders(context, responseMessage);
			await CopyResponseBodyAsync(context, responseMessage, counters);
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
		{
			// 客户端主动断开（SSE 长连接结束等），属于正常收尾
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "路径转发代理失败，规则={RuleId}，路径={Path}，目标={TargetHost}:{TargetPort}", target.RuleId, target.Path, target.TargetHost, target.TargetPort);
			if (!context.Response.HasStarted)
			{
				try
				{
					context.Response.StatusCode = StatusCodes.Status502BadGateway;
					context.Response.ContentType = "text/plain; charset=utf-8";
					await context.Response.WriteAsync("Bad Gateway：端口转发目标不可达。");
				}
				catch (Exception writeEx)
				{
					logger.LogError(writeEx, "路径转发返回 502 失败，规则={RuleId}", target.RuleId);
				}
			}
			else
			{
				// 响应已开始发送（流式中断），无法再改状态码，直接断开
				context.Abort();
			}
		}
		finally
		{
			Interlocked.Decrement(ref counters.ActiveConnections);
		}
	}

	/// <summary>
	/// 有请求体时挂上流式内容：不缓冲、边读边发，读取字节数计入规则统计；已知长度时显式带上 Content-Length 避免分块发送
	/// </summary>
	/// <param name="context"></param>
	/// <param name="requestMessage"></param>
	/// <param name="counters"></param>
	private static void BuildRequestContent(HttpContext context, HttpRequestMessage requestMessage, PortForwardCounters counters)
	{
		var hasBody = context.Request.Headers.ContainsKey("Transfer-Encoding") || (context.Request.ContentLength ?? 0) > 0;
		if (!hasBody)
		{
			return;
		}

		requestMessage.Content = new StreamContent(new CountingReadStream(context.Request.Body, counters));
		if (context.Request.ContentLength is > 0)
		{
			requestMessage.Content.Headers.ContentLength = context.Request.ContentLength;
		}
	}

	/// <summary>
	/// 复制请求头到目标请求：跳过逐跳头、Connection 点名的头与单独处理的 Content-Length；
	/// 其余先放消息头，放不下的（如 Content-Type）放内容头
	/// </summary>
	/// <param name="context"></param>
	/// <param name="requestMessage"></param>
	private static void CopyRequestHeaders(HttpContext context, HttpRequestMessage requestMessage)
	{
		var skipped = new HashSet<string>(HopByHopHeaders, StringComparer.OrdinalIgnoreCase);
		if (context.Request.Headers.TryGetValue("Connection", out var connection))
		{
			foreach (var token in connection.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				skipped.Add(token);
			}
		}

		foreach (var header in context.Request.Headers)
		{
			if (skipped.Contains(header.Key) || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
			{
				requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
			}
		}
	}

	/// <summary>
	/// 复制响应头（含内容头）到博客响应，跳过逐跳头；Content-Length 原样带过去，与逐块回传的字节数一致
	/// </summary>
	/// <param name="context"></param>
	/// <param name="responseMessage"></param>
	private static void CopyResponseHeaders(HttpContext context, HttpResponseMessage responseMessage)
	{
		foreach (var header in responseMessage.Headers)
		{
			if (!HopByHopHeaders.Contains(header.Key))
			{
				context.Response.Headers[header.Key] = header.Value.ToArray();
			}
		}

		foreach (var header in responseMessage.Content.Headers)
		{
			if (!HopByHopHeaders.Contains(header.Key))
			{
				context.Response.Headers[header.Key] = header.Value.ToArray();
			}
		}
	}

	/// <summary>
	/// 逐块回传响应体：每块写完立即冲刷，保证 SSE 等流式响应不被缓冲；回传字节数计入规则统计
	/// </summary>
	/// <param name="context"></param>
	/// <param name="responseMessage"></param>
	/// <param name="counters"></param>
	/// <returns></returns>
	private static async Task CopyResponseBodyAsync(HttpContext context, HttpResponseMessage responseMessage, PortForwardCounters counters)
	{
		var source = await responseMessage.Content.ReadAsStreamAsync(context.RequestAborted);
		var buffer = new byte[32 * 1024];
		int read;
		while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), context.RequestAborted)) > 0)
		{
			await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
			await context.Response.Body.FlushAsync(context.RequestAborted);
			Interlocked.Add(ref counters.TotalBytes, read);
		}
	}

	/// <summary>
	/// 请求体计数包装流：只读、不可定位，读到的字节数累计进规则统计；不关闭内层流（其生命周期归框架管）
	/// </summary>
	/// <param name="inner">原始请求体流</param>
	/// <param name="counters">规则运行时计数</param>
	private sealed class CountingReadStream(Stream inner, PortForwardCounters counters) : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count) => Counted(inner.Read(buffer, offset, count));

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Counted(await inner.ReadAsync(buffer, offset, count, cancellationToken));

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Counted(await inner.ReadAsync(buffer, cancellationToken));

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			// 内层是框架的请求体流，不随内容释放
		}

		private int Counted(int read)
		{
			if (read > 0)
			{
				Interlocked.Add(ref counters.TotalBytes, read);
			}

			return read;
		}
	}
}
