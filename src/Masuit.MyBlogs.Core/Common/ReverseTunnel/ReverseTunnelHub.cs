using System.Net;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Masuit.MyBlogs.Core.Common;
using Masuit.MyBlogs.Core.Models.Entity;

namespace Masuit.MyBlogs.Core.Common.ReverseTunnel;

/// <summary>
/// 管理内网客户端主动建立的 WebSocket 控制连接，并把公网 TCP 连接配对到内网数据连接。
/// 控制连接只传输认证与连接编号，实际业务数据使用每条连接独立的数据 WebSocket。
/// </summary>
public sealed class ReverseTunnelHub(ILogger<ReverseTunnelHub> logger, IOptionsMonitor<ReverseTunnelOptions> options)
{
	private const string ControlPath = "/tunnel/control";
	private const string DataPath = "/tunnel/data";
	private const int MaxControlMessageBytes = 16 * 1024;
	private const int MaxTunnelIdLength = 64;
	private static readonly TimeSpan DataConnectionTimeout = TimeSpan.FromSeconds(15);
	private readonly ConcurrentDictionary<string, TunnelRule> _rules = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, TunnelSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, PendingConnection> _pending = new(StringComparer.Ordinal);

	/// <summary>
	/// 用数据库中的启用规则刷新可注册的内网隧道，规则变化时会关闭旧令牌对应的控制连接。
	/// </summary>
	/// <param name="rules">当前启用的端口转发规则。</param>
	public void UpdateRules(IEnumerable<PortForwardRule> rules)
	{
		var next = rules
			.Where(rule => rule.Mode == PortForwardMode.ReverseTunnel)
			.Where(rule => !string.IsNullOrWhiteSpace(rule.TunnelId) && !string.IsNullOrWhiteSpace(rule.TunnelTokenHash))
			.GroupBy(rule => rule.TunnelId.Trim(), StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.ToDictionary(
				rule => rule.TunnelId.Trim(),
				rule => new TunnelRule(rule.Id, rule.TunnelId.Trim(), rule.TunnelTokenHash.Trim(), rule.ListenPort),
				StringComparer.OrdinalIgnoreCase);
		foreach (var existing in _rules)
		{
			if (!next.TryGetValue(existing.Key, out var replacement) || !existing.Value.Equals(replacement))
			{
				if (_sessions.TryRemove(existing.Key, out var session))
				{
					session.Stop();
				}
			}
		}
		_rules.Clear();
		foreach (var item in next)
		{
			_rules[item.Key] = item.Value;
		}
	}

	/// <summary>
	/// 处理内网客户端的控制 WebSocket，首帧必须是 hello 消息，后续仅用于保持连接与响应 ping。
	/// </summary>
	/// <param name="context">当前 HTTP 上下文。</param>
	/// <returns>控制连接关闭后完成的任务。</returns>
	public async Task HandleControlAsync(HttpContext context)
	{
		if (!IsAllowedClient(context))
		{
			return;
		}
		if (!context.WebSockets.IsWebSocketRequest)
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			await context.Response.WriteAsync($"该地址仅接受 WebSocket 连接：{ControlPath}。", context.RequestAborted);
			return;
		}
		WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
		using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
		JsonDocument? hello = await ReceiveJsonAsync(socket, lifetime.Token);
		if (!TryReadHello(hello, out var tunnelId, out var token) || !TryAuthenticate(tunnelId, token, out var rule))
		{
			await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "认证失败");
			socket.Dispose();
			return;
		}
		var session = new TunnelSession(tunnelId, socket, this);
		if (_sessions.TryGetValue(tunnelId, out var previous) && _sessions.TryUpdate(tunnelId, session, previous))
		{
			previous.Stop();
		}
		else
		{
			_sessions[tunnelId] = session;
		}
		await session.SendAsync(new { type = "welcome", tunnelId, listenPort = rule.ListenPort }, lifetime.Token);
		try
		{
			await session.ReceiveLoopAsync(lifetime.Token);
		}
		finally
		{
			if (_sessions.TryGetValue(tunnelId, out var current) && ReferenceEquals(current, session))
			{
				_sessions.TryRemove(tunnelId, out _);
			}
			session.StopPendingConnections();
			socket.Dispose();
		}
	}

	/// <summary>
	/// 处理内网客户端为某个公网连接建立的数据 WebSocket，并在认证后交给等待中的 TCP 连接。
	/// </summary>
	/// <param name="context">当前 HTTP 上下文。</param>
	/// <returns>数据连接关闭后完成的任务。</returns>
	public async Task HandleDataAsync(HttpContext context)
	{
		if (!IsAllowedClient(context))
		{
			return;
		}
		if (!context.WebSockets.IsWebSocketRequest)
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			await context.Response.WriteAsync($"该地址仅接受 WebSocket 连接：{DataPath}。", context.RequestAborted);
			return;
		}
		WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
		var handedOff = false;
		try
		{
			using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
			JsonDocument? hello = await ReceiveJsonAsync(socket, lifetime.Token);
			if (!TryReadDataHello(hello, out var tunnelId, out var connectionId, out var token)
				|| !TryAuthenticate(tunnelId, token, out _)
				|| !_pending.TryGetValue(connectionId, out var pending)
				|| !pending.Matches(tunnelId))
			{
				await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "数据连接认证失败");
				return;
			}
			await SendAsync(socket, new { type = "ready", connectionId }, lifetime.Token);
			if (!pending.TrySetSocket(socket))
			{
				await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "连接已失效");
				return;
			}
			handedOff = true;
			// 保持当前 WebSocket 请求存活到转发结束，避免 ASP.NET 请求收尾时提前释放数据连接。
			await pending.Completion.Task.WaitAsync(lifetime.Token);
		}
		finally
		{
			if (!handedOff)
			{
				socket.Dispose();
			}
		}
	}

	/// <summary>
	/// 把一个公网 TCP 连接转发给对应的内网客户端；客户端离线或数据通道超时会直接关闭公网连接。
	/// </summary>
	/// <param name="rule">当前端口转发规则。</param>
	/// <param name="client">公网入站 TCP 连接。</param>
	/// <param name="counters">用于累计活跃连接和流量的计数器。</param>
	/// <param name="request">当前公网连接的请求记录。</param>
	/// <param name="cancellationToken">规则停止或应用退出取消标记。</param>
	/// <returns>连接关闭后完成的任务。</returns>
	public async Task ForwardAsync(PortForwardRule rule, TcpClient client, PortForwardCounters counters, PortForwardRequestRecord request, CancellationToken cancellationToken)
	{
		string tunnelId = rule.TunnelId?.Trim() ?? string.Empty;
		if (tunnelId.Length == 0 || !_sessions.TryGetValue(tunnelId, out var session))
		{
			logger.LogWarning("公网连接到达但内网隧道控制会话不存在，规则={RuleId}，隧道={TunnelId}，监听端口={ListenPort}", rule.Id, tunnelId, rule.ListenPort);
			request.MarkFailed("内网客户端未连接");
			return;
		}
		var connectionId = Guid.NewGuid().ToString("N");
		var pending = new PendingConnection(connectionId, tunnelId, client);
		if (!_pending.TryAdd(connectionId, pending))
		{
			request.MarkFailed("创建转发连接失败");
			return;
		}
		try
		{
			await session.SendAsync(new { type = "open", connectionId }, cancellationToken);
			using WebSocket? dataSocket = await pending.Socket.Task.WaitAsync(DataConnectionTimeout, cancellationToken);
			if (dataSocket is null)
			{
				request.MarkFailed("内网客户端数据通道未建立");
				return;
			}
			await RelayAsync(client.GetStream(), dataSocket, counters, request, cancellationToken);
			request.MarkCompleted();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			request.MarkCancelled();
		}
		catch (TimeoutException)
		{
			logger.LogWarning("内网隧道数据连接超时，规则={RuleId}，隧道={TunnelId}", rule.Id, rule.TunnelId);
			request.MarkFailed("内网客户端数据通道连接超时");
		}
		catch (WebSocketException exception)
		{
			logger.LogWarning(exception, "内网隧道数据连接异常，规则={RuleId}，隧道={TunnelId}", rule.Id, tunnelId);
			request.MarkFailed("内网隧道数据连接异常");
			RemoveSessionIfCurrent(tunnelId, session);
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "内网隧道转发失败，规则={RuleId}，隧道={TunnelId}", rule.Id, tunnelId);
			request.MarkFailed("内网隧道转发失败");
			RemoveSessionIfCurrent(tunnelId, session);
		}
		finally
		{
			_pending.TryRemove(connectionId, out _);
			pending.Complete();
		}
	}

	/// <summary>
	/// 为 HTTP 路径代理打开一条隧道字节流：注册待配对连接后通知客户端建立数据通道，
	/// 客户端连上后把数据 WebSocket 包装成流返回；流释放时摘除待配对连接并通知数据请求收尾。
	/// 目标只在规则填了真实值时才随 open 消息下发（主机非空才带 targetHost、端口大于 0 才带 targetPort），
	/// 缺失时客户端回退到自身配置的默认目标；老客户端会忽略这两个字段，不影响兼容性。
	/// </summary>
	/// <param name="tunnelId">内网穿透编号。</param>
	/// <param name="targetHost">规则填写的目标主机（客户端所在内网的地址），可为空。</param>
	/// <param name="targetPort">规则填写的目标端口，0 表示用客户端默认目标端口。</param>
	/// <param name="cancellationToken">调用方取消标记（HTTP 请求结束等）。</param>
	/// <returns>与内网目标服务相连的字节流。</returns>
	/// <exception cref="InvalidOperationException">客户端离线、数据通道超时或未建立时抛出，由调用方按网关错误处理。</exception>
	public async Task<Stream> OpenStreamAsync(string tunnelId, string targetHost, int targetPort, CancellationToken cancellationToken)
	{
		tunnelId = tunnelId?.Trim() ?? string.Empty;
		if (tunnelId.Length == 0 || !_sessions.TryGetValue(tunnelId, out var session))
		{
			logger.LogWarning("HTTP 路径代理打开隧道流失败：内网客户端未连接，隧道={TunnelId}", tunnelId);
			throw new InvalidOperationException("内网客户端未连接");
		}
		var connectionId = Guid.NewGuid().ToString("N");
		var pending = new PendingConnection(connectionId, tunnelId, null);
		if (!_pending.TryAdd(connectionId, pending))
		{
			throw new InvalidOperationException("创建转发连接失败");
		}
		try
		{
			var open = new Dictionary<string, object> { ["type"] = "open", ["connectionId"] = connectionId };
			if (!string.IsNullOrWhiteSpace(targetHost))
			{
				open["targetHost"] = targetHost.Trim();
			}
			if (targetPort > 0)
			{
				open["targetPort"] = targetPort;
			}
			await session.SendAsync(open, cancellationToken);
			WebSocket? dataSocket = await pending.Socket.Task.WaitAsync(DataConnectionTimeout, cancellationToken);
			if (dataSocket is null)
			{
				throw new InvalidOperationException("内网客户端数据通道未建立");
			}
			// 流的生命周期即连接的生命周期：释放时摘除 pending 并完成它，HandleDataAsync 的等待随之结束
			return new WebSocketStream(dataSocket, () =>
			{
				_pending.TryRemove(connectionId, out _);
				pending.Complete();
			});
		}
		catch (TimeoutException)
		{
			logger.LogWarning("内网隧道数据连接超时（HTTP 路径代理），隧道={TunnelId}", tunnelId);
			_pending.TryRemove(connectionId, out _);
			pending.Cancel();
			throw new InvalidOperationException("内网客户端数据通道连接超时");
		}
		catch (OperationCanceledException)
		{
			_pending.TryRemove(connectionId, out _);
			pending.Cancel();
			throw;
		}
		catch (Exception exception)
		{
			_pending.TryRemove(connectionId, out _);
			pending.Cancel();
			if (exception is InvalidOperationException)
			{
				throw;
			}
			logger.LogWarning(exception, "打开内网隧道流失败，隧道={TunnelId}", tunnelId);
			throw new InvalidOperationException("内网隧道流打开失败", exception);
		}
	}

	private void RemoveSessionIfCurrent(string tunnelId, TunnelSession session)
	{
		if (_sessions.TryGetValue(tunnelId, out var current) && ReferenceEquals(current, session))
		{
			_sessions.TryRemove(tunnelId, out _);
			session.Stop();
		}
	}

	/// <summary>
	/// 计算隧道令牌的 SHA-256 十六进制摘要，数据库只保存摘要而不保存明文令牌。
	/// </summary>
	/// <param name="token">内网客户端使用的明文令牌。</param>
	/// <returns>大写十六进制摘要。</returns>
	public static string HashToken(string token)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
	}

	/// <summary>校验隧道编号、令牌和规则是否匹配。</summary>
	/// <param name="tunnelId">客户端声明的隧道编号。</param>
	/// <param name="token">客户端提交的令牌。</param>
	/// <param name="rule">匹配到的隧道规则。</param>
	/// <returns>认证成功时为 true。</returns>
	private bool TryAuthenticate(string tunnelId, string token, out TunnelRule rule)
	{
		rule = null;
		if (tunnelId.Length == 0 || tunnelId.Length > MaxTunnelIdLength || !_rules.TryGetValue(tunnelId, out rule))
		{
			return false;
		}
		byte[] expected = Encoding.UTF8.GetBytes(rule.TokenHash);
		byte[] actual = Encoding.UTF8.GetBytes(HashToken(token));
		return CryptographicOperations.FixedTimeEquals(expected, actual);
	}

	/// <summary>
	/// 按服务端白名单校验 WebSocket 远端地址；空白名单表示不启用来源地址限制。
	/// </summary>
	/// <param name="context">当前 HTTP 上下文。</param>
	/// <returns>来源地址允许访问时为 true。</returns>
	private bool IsAllowedClient(HttpContext context)
	{
		IPAddress? address = context.Connection.RemoteIpAddress;
		ReverseTunnelOptions currentOptions = options.CurrentValue;
		if ((address is null && (currentOptions.AllowedClientIps ?? []).Length == 0) || address is not null && currentOptions.IsAllowed(address))
		{
			return true;
		}
		logger.LogWarning("反向隧道客户端来源地址不在白名单内，地址={RemoteAddress}", address);
		context.Response.StatusCode = StatusCodes.Status403Forbidden;
		return false;
	}

	/// <summary>读取一个有大小和超时限制的 JSON 文本 WebSocket 消息。</summary>
	/// <param name="socket">待读取的 WebSocket。</param>
	/// <param name="cancellationToken">取消标记。</param>
	/// <returns>完整 JSON 文档；连接关闭、消息类型不符或格式错误时返回 null。</returns>
	private static async Task<JsonDocument?> ReceiveJsonAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(DataConnectionTimeout);
		var buffer = new byte[8192];
		using var message = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, timeout.Token);
			if (result.MessageType == WebSocketMessageType.Close || result.MessageType != WebSocketMessageType.Text)
			{
				return null;
			}
			message.Write(buffer, 0, result.Count);
			if (message.Length > MaxControlMessageBytes)
			{
				return null;
			}
			if (result.EndOfMessage)
			{
				break;
			}
		}
		try
		{
			return JsonDocument.Parse(message.ToArray());
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>读取并校验控制连接 hello 消息。</summary>
	/// <param name="document">客户端发送的 JSON 文档。</param>
	/// <param name="tunnelId">输出隧道编号。</param>
	/// <param name="token">输出明文令牌。</param>
	/// <returns>消息结构完整时为 true。</returns>
	private static bool TryReadHello(JsonDocument document, out string tunnelId, out string token)
	{
		tunnelId = string.Empty;
		token = string.Empty;
		return TryReadString(document, "type", out var type)
			&& string.Equals(type, "hello", StringComparison.Ordinal)
			&& TryReadString(document, "tunnelId", out tunnelId)
			&& TryReadString(document, "token", out token);
	}

	/// <summary>读取并校验数据连接 hello 消息。</summary>
	/// <param name="document">客户端发送的 JSON 文档。</param>
	/// <param name="tunnelId">输出隧道编号。</param>
	/// <param name="connectionId">输出公网连接编号。</param>
	/// <param name="token">输出明文令牌。</param>
	/// <returns>消息结构完整时为 true。</returns>
	private static bool TryReadDataHello(JsonDocument document, out string tunnelId, out string connectionId, out string token)
	{
		tunnelId = string.Empty;
		connectionId = string.Empty;
		token = string.Empty;
		return TryReadString(document, "type", out var type)
			&& string.Equals(type, "data", StringComparison.Ordinal)
			&& TryReadString(document, "tunnelId", out tunnelId)
			&& TryReadString(document, "connectionId", out connectionId)
			&& TryReadString(document, "token", out token);
	}

	/// <summary>从 JSON 文档读取非空字符串属性。</summary>
	/// <param name="document">JSON 文档。</param>
	/// <param name="name">属性名。</param>
	/// <param name="value">属性值。</param>
	/// <returns>属性存在且为非空字符串时为 true。</returns>
	private static bool TryReadString(JsonDocument document, string name, out string value)
	{
		value = string.Empty;
		return document is not null
			&& document.RootElement.TryGetProperty(name, out var property)
			&& property.ValueKind == JsonValueKind.String
			&& !string.IsNullOrWhiteSpace(value = property.GetString());
	}

	/// <summary>向 WebSocket 发送一个 JSON 文本消息。</summary>
	/// <param name="socket">目标 WebSocket。</param>
	/// <param name="message">消息对象。</param>
	/// <param name="cancellationToken">取消标记。</param>
	/// <returns>发送完成后结束的任务。</returns>
	private static async Task SendAsync(WebSocket socket, object message, CancellationToken cancellationToken)
	{
		byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
		await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
	}

	/// <summary>安全关闭 WebSocket，关闭握手失败时不再向上抛出异常。</summary>
	/// <param name="socket">待关闭的 WebSocket。</param>
	/// <param name="status">关闭状态。</param>
	/// <param name="description">关闭原因。</param>
	/// <returns>关闭处理任务。</returns>
	private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string description)
	{
		try
		{
			if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
			{
				await socket.CloseAsync(status, description, CancellationToken.None);
			}
		}
		catch (WebSocketException)
		{
			socket.Abort();
		}
	}

	/// <summary>在公网 TCP 流和内网数据 WebSocket 之间双向搬运数据。</summary>
	/// <param name="tcpStream">公网 TCP 流。</param>
	/// <param name="socket">内网数据 WebSocket。</param>
	/// <param name="counters">流量统计。</param>
	/// <param name="request">当前公网连接的请求记录。</param>
	/// <param name="cancellationToken">取消标记。</param>
	/// <returns>任一方向结束后完成的任务。</returns>
	private static async Task RelayAsync(NetworkStream tcpStream, WebSocket socket, PortForwardCounters counters, PortForwardRequestRecord request, CancellationToken cancellationToken)
	{
		using var relay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task upload = CopyTcpToWebSocketAsync(tcpStream, socket, counters, request, relay.Token);
		Task download = CopyWebSocketToTcpAsync(socket, tcpStream, counters, request, relay.Token);
		await Task.WhenAny(upload, download);
		relay.Cancel();
		try
		{
			await Task.WhenAll(upload, download);
		}
		catch (OperationCanceledException) when (relay.IsCancellationRequested)
		{
		}
	}

	/// <summary>把公网 TCP 数据发送为二进制 WebSocket 消息。</summary>
	/// <param name="source">TCP 输入流。</param>
	/// <param name="socket">WebSocket 输出端。</param>
	/// <param name="counters">流量统计。</param>
	/// <param name="request">当前公网连接的请求记录。</param>
	/// <param name="cancellationToken">取消标记。</param>
	/// <returns>输入流结束后完成的任务。</returns>
	private static async Task CopyTcpToWebSocketAsync(NetworkStream source, WebSocket socket, PortForwardCounters counters, PortForwardRequestRecord request, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[64 * 1024];
		while (true)
		{
			int read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
			if (read == 0)
			{
				return;
			}
			await socket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, true, cancellationToken);
			Interlocked.Add(ref counters.TotalBytes, read);
			request.AddBytes(read);
		}
	}

	/// <summary>把二进制 WebSocket 消息写回公网 TCP 流。</summary>
	/// <param name="socket">WebSocket 输入端。</param>
	/// <param name="destination">TCP 输出流。</param>
	/// <param name="counters">流量统计。</param>
	/// <param name="request">当前公网连接的请求记录。</param>
	/// <param name="cancellationToken">取消标记。</param>
	/// <returns>WebSocket 关闭后完成的任务。</returns>
	private static async Task CopyWebSocketToTcpAsync(WebSocket socket, NetworkStream destination, PortForwardCounters counters, PortForwardRequestRecord request, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[64 * 1024];
		while (true)
		{
			WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				return;
			}
			if (result.MessageType != WebSocketMessageType.Binary)
			{
				continue;
			}
			await destination.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
			Interlocked.Add(ref counters.TotalBytes, result.Count);
			request.AddBytes(result.Count);
		}
	}

	private sealed record TunnelRule(int RuleId, string TunnelId, string TokenHash, int ListenPort);

	private sealed class PendingConnection(string connectionId, string tunnelId, TcpClient? client)
	{
		public string ConnectionId { get; } = connectionId;
		public string TunnelId { get; } = tunnelId;

		/// <summary>
		/// TCP 入口的公网连接；HTTP 路径入口打开的流连接没有 TCP 连接，为 null
		/// </summary>
		public TcpClient? Client { get; } = client;
		public TaskCompletionSource<WebSocket?> Socket { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public bool Matches(string tunnelId)
		{
			return string.Equals(TunnelId, tunnelId, StringComparison.OrdinalIgnoreCase);
		}

		public bool TrySetSocket(WebSocket socket)
		{
			return Socket.TrySetResult(socket);
		}

		public void Cancel()
		{
			Socket.TrySetResult(null);
			Completion.TrySetResult(true);
		}

		public void Complete()
		{
			Completion.TrySetResult(true);
		}
	}

	/// <summary>
	/// 把一条数据 WebSocket 包装成字节流供 HTTP 路径代理使用：写即发送一个完整的二进制帧，
	/// 读逐帧返回二进制消息内容并天然处理分片（单帧未读完时下次读继续同一帧，跨帧无感知）。
	/// 不在此做字节计数：代理中间件已按 HTTP 请求/响应体计量，再计一次会重复。
	/// 释放时中止并释放 WebSocket，并回调通知摘除待配对连接、完成数据请求等待。
	/// </summary>
	/// <param name="socket">已配对的数据 WebSocket，生命周期归本流所有。</param>
	/// <param name="onDispose">首次释放时执行的收尾回调。</param>
	private sealed class WebSocketStream(WebSocket socket, Action onDispose) : Stream
	{
		private readonly byte[] _receiveBuffer = new byte[64 * 1024];
		private readonly SemaphoreSlim _sendLock = new(1, 1);
		private int _receiveOffset;
		private int _receiveCount;
		private bool _receiveEnded;
		private bool _disposed;

		public override bool CanRead => true;
		public override bool CanWrite => true;
		public override bool CanSeek => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override void Flush()
		{
		}

		public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

		public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			if (buffer.Length == 0)
			{
				return 0;
			}
			while (true)
			{
				if (_receiveCount > 0)
				{
					int copied = Math.Min(buffer.Length, _receiveCount);
					_receiveBuffer.AsMemory(_receiveOffset, copied).CopyTo(buffer);
					_receiveOffset += copied;
					_receiveCount -= copied;
					return copied;
				}
				if (_receiveEnded)
				{
					return 0;
				}
				WebSocketReceiveResult result = await socket.ReceiveAsync(_receiveBuffer, cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					_receiveEnded = true;
					return 0;
				}
				if (result.MessageType != WebSocketMessageType.Binary || result.Count == 0)
				{
					// 文本帧与空帧不属于业务字节流，跳过继续接收
					continue;
				}
				_receiveOffset = 0;
				_receiveCount = result.Count;
			}
		}

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			await _sendLock.WaitAsync(cancellationToken);
			try
			{
				await socket.SendAsync(buffer, WebSocketMessageType.Binary, true, cancellationToken);
			}
			finally
			{
				_sendLock.Release();
			}
		}

		public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => await WriteAsync(buffer.AsMemory(offset, count), cancellationToken);

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				_disposed = true;
				try
				{
					socket.Abort();
					socket.Dispose();
				}
				catch (Exception)
				{
					// 连接已断开时释放失败不影响收尾回调执行
				}
				onDispose();
			}
			base.Dispose(disposing);
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(WebSocketStream));
			}
		}
	}

	private sealed class TunnelSession(string tunnelId, WebSocket socket, ReverseTunnelHub hub)
	{
		private readonly SemaphoreSlim _sendLock = new(1, 1);

		public string TunnelId { get; } = tunnelId;

		public void Stop()
		{
			socket.Abort();
			StopPendingConnections();
		}

		public void StopPendingConnections()
		{
			foreach (PendingConnection pending in hub._pending.Values.Where(item => item.Matches(TunnelId)))
			{
				pending.Cancel();
			}
		}

		public async Task SendAsync(object message, CancellationToken cancellationToken)
		{
			await _sendLock.WaitAsync(cancellationToken);
			try
			{
				await ReverseTunnelHub.SendAsync(socket, message, cancellationToken);
			}
			finally
			{
				_sendLock.Release();
			}
		}

		public async Task ReceiveLoopAsync(CancellationToken cancellationToken)
		{
			byte[] buffer = new byte[4096];
			while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
			{
				WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					return;
				}
				if (result.MessageType == WebSocketMessageType.Text && result.EndOfMessage)
				{
					string text = Encoding.UTF8.GetString(buffer, 0, result.Count);
					if (text.Contains("\"type\":\"ping\"", StringComparison.Ordinal))
					{
						await SendAsync(new { type = "pong" }, cancellationToken);
					}
				}
			}
		}
	}
}

/// <summary>把 /tunnel/control 与 /tunnel/data 路径交给反向隧道服务端。</summary>
public sealed class ReverseTunnelMiddleware(RequestDelegate next, ReverseTunnelHub hub)
{
	/// <summary>处理反向隧道 WebSocket 请求，非隧道路由继续进入博客管道。</summary>
	/// <param name="context">当前 HTTP 上下文。</param>
	/// <returns>请求处理任务。</returns>
	public async Task InvokeAsync(HttpContext context)
	{
		if (context.Request.Path.Equals("/tunnel/control", StringComparison.OrdinalIgnoreCase))
		{
			await hub.HandleControlAsync(context);
			return;
		}
		if (context.Request.Path.Equals("/tunnel/data", StringComparison.OrdinalIgnoreCase))
		{
			await hub.HandleDataAsync(context);
			return;
		}
		await next(context);
	}
}
