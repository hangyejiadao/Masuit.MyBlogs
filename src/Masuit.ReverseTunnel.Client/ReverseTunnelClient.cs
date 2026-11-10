using System.Net.WebSockets;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Masuit.ReverseTunnel.Client;

/// <summary>
/// 反向隧道协议客户端。控制通道和数据通道都会发送密钥，服务端必须校验通过后才会继续转发。
/// </summary>
public sealed class ReverseTunnelClient(ILogger<ReverseTunnelClient> logger)
{
	private const int MaxJsonMessageBytes = 16 * 1024;
	private static readonly TimeSpan ControlHeartbeatInterval = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan ControlHeartbeatTimeout = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan[] DataConnectRetryDelays = [TimeSpan.Zero, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750)];

	public async Task RunSessionAsync(ClientConfiguration configuration, CancellationToken cancellationToken, Action<DateTimeOffset> connected)
	{
		using var control = new ClientWebSocket();
		control.Options.KeepAliveInterval = ControlHeartbeatInterval;
		await control.ConnectAsync(BuildEndpointUri(configuration.ServerUrl, "/tunnel/control"), cancellationToken);
		await SendJsonAsync(control, new { type = "hello", tunnelId = configuration.TunnelId, token = configuration.Token }, cancellationToken);
		using JsonDocument welcome = await ReceiveJsonAsync(control, cancellationToken) ?? throw new InvalidOperationException("服务端未返回 welcome，密钥或隧道编号可能不正确。");
		if (!IsMessage(welcome, "welcome"))
		{
			throw new InvalidOperationException("服务端拒绝了隧道注册，请检查密钥和白名单。");
		}
		int? listenPort = welcome.RootElement.TryGetProperty("listenPort", out JsonElement listenPortElement) && listenPortElement.TryGetInt32(out int port)
			? port
			: null;
		connected(DateTimeOffset.UtcNow);
		logger.LogInformation("反向隧道控制连接已建立，隧道={TunnelId}，服务端监听端口={ListenPort}", configuration.TunnelId, listenPort?.ToString() ?? "未知");
		using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var heartbeatMonitor = new HeartbeatMonitor();
		Task receive = ReceiveControlMessagesAsync(control, configuration, heartbeatMonitor, sessionCancellation.Token);
		Task heartbeat = SendHeartbeatAsync(control, heartbeatMonitor, sessionCancellation.Token);
		Task completed = await Task.WhenAny(receive, heartbeat);
		sessionCancellation.Cancel();
		try
		{
			await Task.WhenAll(receive, heartbeat);
		}
		catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
		{
		}
		if (!cancellationToken.IsCancellationRequested && completed.IsFaulted)
		{
			Exception? exception = completed.Exception?.GetBaseException();
			if (exception is not null)
			{
				throw exception;
			}
		}
	}

	private async Task ReceiveControlMessagesAsync(ClientWebSocket control, ClientConfiguration configuration, HeartbeatMonitor heartbeatMonitor, CancellationToken cancellationToken)
	{
		while (control.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			using JsonDocument? message = await ReceiveJsonAsync(control, cancellationToken);
			if (message is null)
			{
				return;
			}
			if (IsMessage(message, "pong"))
			{
				heartbeatMonitor.MarkPong();
				continue;
			}
			if (IsMessage(message, "open") && message.RootElement.TryGetProperty("connectionId", out JsonElement id))
			{
				string connectionId = id.GetString() ?? string.Empty;
				// 可选的每连接目标：路径转发时服务端会随 open 带上规则填写的目标，老规则不带则回退到客户端配置的默认目标
				string? targetHost = message.RootElement.TryGetProperty("targetHost", out JsonElement hostElement) && hostElement.ValueKind == JsonValueKind.String
					? hostElement.GetString()
					: null;
				int? targetPort = message.RootElement.TryGetProperty("targetPort", out JsonElement portElement) && portElement.TryGetInt32(out int portValue) && portValue > 0
					? portValue
					: null;
				logger.LogInformation("收到公网连接请求，隧道={TunnelId}，连接={ConnectionId}", configuration.TunnelId, connectionId);
				_ = HandleConnectionAsync(configuration, connectionId, targetHost, targetPort, cancellationToken);
			}
		}
	}

	private static async Task SendHeartbeatAsync(ClientWebSocket control, HeartbeatMonitor heartbeatMonitor, CancellationToken cancellationToken)
	{
		while (control.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(ControlHeartbeatInterval, cancellationToken);
			long pongVersion = heartbeatMonitor.PongVersion;
			await SendJsonAsync(control, new { type = "ping" }, cancellationToken);
			await heartbeatMonitor.WaitForNextPongAsync(pongVersion, ControlHeartbeatTimeout, cancellationToken);
		}
	}

	private async Task HandleConnectionAsync(ClientConfiguration configuration, string connectionId, string? targetHost, int? targetPort, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(connectionId))
		{
			return;
		}
		// 服务端随 open 指定了目标（路径转发）时优先用它，缺失的部分回退到客户端配置的默认目标
		string localHost = string.IsNullOrWhiteSpace(targetHost) ? configuration.LocalHost : targetHost;
		int localPort = targetPort ?? configuration.LocalPort;
		using var local = new TcpClient();
		ClientWebSocket? data = null;
		try
		{
			data = await ConnectDataSocketAsync(configuration, connectionId, cancellationToken);
			logger.LogInformation("数据通道认证成功，隧道={TunnelId}，连接={ConnectionId}", configuration.TunnelId, connectionId);
			await local.ConnectAsync(localHost, localPort, cancellationToken);
			logger.LogInformation("已连接本地目标，隧道={TunnelId}，连接={ConnectionId}，目标={LocalHost}:{LocalPort}", configuration.TunnelId, connectionId, localHost, localPort);
			using NetworkStream localStream = local.GetStream();
			using var relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			Task upload = CopyLocalToWebSocketAsync(localStream, data, relayCancellation.Token);
			Task download = CopyWebSocketToLocalAsync(data, localStream, relayCancellation.Token);
			await Task.WhenAny(upload, download);
			relayCancellation.Cancel();
			try
			{
				await Task.WhenAll(upload, download);
			}
			catch (OperationCanceledException) when (relayCancellation.IsCancellationRequested)
			{
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "内网连接转发失败，连接={ConnectionId}", connectionId);
		}
		finally
		{
			data?.Dispose();
		}
	}

	private async Task<ClientWebSocket> ConnectDataSocketAsync(ClientConfiguration configuration, string connectionId, CancellationToken cancellationToken)
	{
		Exception? lastException = null;
		for (int attempt = 0; attempt < DataConnectRetryDelays.Length; attempt++)
		{
			if (DataConnectRetryDelays[attempt] > TimeSpan.Zero)
			{
				await Task.Delay(DataConnectRetryDelays[attempt], cancellationToken);
			}

			var data = new ClientWebSocket();
			try
			{
				await data.ConnectAsync(BuildEndpointUri(configuration.ServerUrl, "/tunnel/data"), cancellationToken);
				logger.LogInformation("数据通道已连接，隧道={TunnelId}，连接={ConnectionId}，尝试次数={Attempt}", configuration.TunnelId, connectionId, attempt + 1);
				await SendJsonAsync(data, new { type = "data", tunnelId = configuration.TunnelId, connectionId, token = configuration.Token }, cancellationToken);
				using JsonDocument ready = await ReceiveJsonAsync(data, cancellationToken) ?? throw new InvalidOperationException("服务端未确认数据连接。");
				if (!IsMessage(ready, "ready"))
				{
					throw new InvalidOperationException("服务端拒绝了数据连接，密钥校验失败或连接已过期。");
				}
				return data;
			}
			catch (WebSocketException exception) when (!cancellationToken.IsCancellationRequested && attempt + 1 < DataConnectRetryDelays.Length)
			{
				lastException = exception;
				data.Dispose();
				logger.LogWarning("数据通道建立失败，准备重试，隧道={TunnelId}，连接={ConnectionId}，尝试次数={Attempt}，原因={Message}", configuration.TunnelId, connectionId, attempt + 1, exception.Message);
			}
			catch
			{
				data.Dispose();
				throw;
			}
		}

		throw new WebSocketException("数据通道建立失败，已达到重试次数。", lastException);
	}

	private static async Task CopyLocalToWebSocketAsync(NetworkStream source, ClientWebSocket destination, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[64 * 1024];
		while (true)
		{
			int read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
			if (read == 0)
			{
				return;
			}
			await destination.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, true, cancellationToken);
		}
	}

	private static async Task CopyWebSocketToLocalAsync(ClientWebSocket source, NetworkStream destination, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[64 * 1024];
		while (true)
		{
			WebSocketReceiveResult result = await source.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				return;
			}
			if (result.MessageType == WebSocketMessageType.Binary)
			{
				await destination.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
			}
		}
	}

	private static Uri BuildEndpointUri(string serverUrl, string endpoint)
	{
		Uri server = new(serverUrl.Trim());
		string scheme = server.Scheme is "https" or "wss" ? "wss" : "ws";
		string basePath = server.AbsolutePath.TrimEnd('/');
		string path = basePath.EndsWith("/tunnel", StringComparison.OrdinalIgnoreCase) ? basePath + endpoint[7..] : endpoint;
		return new UriBuilder(scheme, server.Host, server.Port, path).Uri;
	}

	private static async Task SendJsonAsync(ClientWebSocket socket, object value, CancellationToken cancellationToken)
	{
		byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
		await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
	}

	private static async Task<JsonDocument?> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[8192];
		using var message = new MemoryStream();
		while (true)
		{
			WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close || result.MessageType != WebSocketMessageType.Text)
			{
				return null;
			}
			message.Write(buffer, 0, result.Count);
			if (message.Length > MaxJsonMessageBytes)
			{
				return null;
			}
			if (result.EndOfMessage)
			{
				return JsonDocument.Parse(message.ToArray());
			}
		}
	}

	private static bool IsMessage(JsonDocument message, string expectedType)
	{
		return message.RootElement.TryGetProperty("type", out JsonElement type)
			&& string.Equals(type.GetString(), expectedType, StringComparison.Ordinal);
	}

	private sealed class HeartbeatMonitor
	{
		private long _pongVersion;

		public long PongVersion => Interlocked.Read(ref _pongVersion);

		public void MarkPong()
		{
			Interlocked.Increment(ref _pongVersion);
		}

		public async Task WaitForNextPongAsync(long previousVersion, TimeSpan timeout, CancellationToken cancellationToken)
		{
			using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutCancellation.CancelAfter(timeout);
			while (PongVersion == previousVersion)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250), timeoutCancellation.Token);
			}
		}
	}
}
