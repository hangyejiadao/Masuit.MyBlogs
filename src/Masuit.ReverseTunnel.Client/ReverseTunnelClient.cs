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

	public async Task RunSessionAsync(ClientConfiguration configuration, CancellationToken cancellationToken, Action<DateTimeOffset> connected)
	{
		using var control = new ClientWebSocket();
		await control.ConnectAsync(BuildEndpointUri(configuration.ServerUrl, "/tunnel/control"), cancellationToken);
		await SendJsonAsync(control, new { type = "hello", tunnelId = configuration.TunnelId, token = configuration.Token }, cancellationToken);
		using JsonDocument welcome = await ReceiveJsonAsync(control, cancellationToken) ?? throw new InvalidOperationException("服务端未返回 welcome，密钥或隧道编号可能不正确。");
		if (!IsMessage(welcome, "welcome"))
		{
			throw new InvalidOperationException("服务端拒绝了隧道注册，请检查密钥和白名单。");
		}
		connected(DateTimeOffset.UtcNow);
		logger.LogInformation("反向隧道控制连接已建立，隧道={TunnelId}", configuration.TunnelId);
		while (control.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			using JsonDocument? message = await ReceiveJsonAsync(control, cancellationToken);
			if (message is null)
			{
				return;
			}
			if (IsMessage(message, "open") && message.RootElement.TryGetProperty("connectionId", out JsonElement id))
			{
				_ = HandleConnectionAsync(configuration, id.GetString() ?? string.Empty, cancellationToken);
			}
		}
	}

	private async Task HandleConnectionAsync(ClientConfiguration configuration, string connectionId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(connectionId))
		{
			return;
		}
		using var data = new ClientWebSocket();
		using var local = new TcpClient();
		try
		{
			await data.ConnectAsync(BuildEndpointUri(configuration.ServerUrl, "/tunnel/data"), cancellationToken);
			await SendJsonAsync(data, new { type = "data", tunnelId = configuration.TunnelId, connectionId, token = configuration.Token }, cancellationToken);
			using JsonDocument ready = await ReceiveJsonAsync(data, cancellationToken) ?? throw new InvalidOperationException("服务端未确认数据连接。");
			if (!IsMessage(ready, "ready"))
			{
				throw new InvalidOperationException("服务端拒绝了数据连接，密钥校验失败或连接已过期。");
			}
			await local.ConnectAsync(configuration.LocalHost, configuration.LocalPort, cancellationToken);
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
}
