namespace Masuit.ReverseTunnel.Client;

/// <summary>
/// 管理反向隧道连接生命周期，并在配置变化后重建连接。
/// </summary>
public sealed class TunnelClientService(ClientStore store, ILogger<TunnelClientService> logger, ILogger<ReverseTunnelClient> clientLogger) : BackgroundService
{
	private readonly SemaphoreSlim _reloadSignal = new(0);
	private readonly object _gate = new();
	private CancellationTokenSource? _sessionCancellation;
	private ClientRuntimeStatus _status = ClientRuntimeStatus.Stopped;
	private string? _lastError;
	private DateTimeOffset? _connectedAt;

	public ClientRuntimeStatus GetStatus()
	{
		lock (_gate)
		{
			return _status with { LastError = _lastError, ConnectedAt = _connectedAt };
		}
	}

	public void Reload()
	{
		lock (_gate)
		{
			_sessionCancellation?.Cancel();
		}
		if (_reloadSignal.CurrentCount == 0)
		{
			_reloadSignal.Release();
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			ClientConfiguration configuration = store.Load();
			if (!configuration.Enabled)
			{
				SetStatus(ClientRuntimeStatus.Stopped, null, null);
				await _reloadSignal.WaitAsync(stoppingToken);
				continue;
			}

			using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
			lock (_gate)
			{
				_sessionCancellation = sessionCancellation;
			}
			try
			{
				SetStatus(ClientRuntimeStatus.Connecting, null, null);
				var client = new ReverseTunnelClient(clientLogger);
				await client.RunSessionAsync(configuration, sessionCancellation.Token, connectedAt => SetStatus(ClientRuntimeStatus.Connected, null, connectedAt));
			}
			catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
			{
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "内网穿透客户端连接异常");
				SetStatus(ClientRuntimeStatus.Faulted, exception.Message, null);
			}
			finally
			{
				lock (_gate)
				{
					if (ReferenceEquals(_sessionCancellation, sessionCancellation))
					{
						_sessionCancellation = null;
					}
				}
			}

			if (!stoppingToken.IsCancellationRequested && store.Load().Enabled && !sessionCancellation.IsCancellationRequested)
			{
				SetStatus(ClientRuntimeStatus.Reconnecting, _lastError, null);
				if (_reloadSignal.CurrentCount > 0)
				{
					await _reloadSignal.WaitAsync(stoppingToken);
				}
				else
				{
					await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
				}
			}
		}
	}

	private void SetStatus(ClientRuntimeStatus status, string? error, DateTimeOffset? connectedAt)
	{
		lock (_gate)
		{
			_status = status;
			_lastError = error;
			_connectedAt = connectedAt;
		}
	}
}

public sealed record ClientRuntimeStatus(ClientRuntimeStatusCode State, string? LastError, DateTimeOffset? ConnectedAt)
{
	public static ClientRuntimeStatus Stopped { get; } = new(ClientRuntimeStatusCode.Stopped, null, null);
	public static ClientRuntimeStatus Connecting { get; } = new(ClientRuntimeStatusCode.Connecting, null, null);
	public static ClientRuntimeStatus Connected { get; } = new(ClientRuntimeStatusCode.Connected, null, null);
	public static ClientRuntimeStatus Reconnecting { get; } = new(ClientRuntimeStatusCode.Reconnecting, null, null);
	public static ClientRuntimeStatus Faulted { get; } = new(ClientRuntimeStatusCode.Faulted, null, null);
}

public enum ClientRuntimeStatusCode
{
	Stopped,
	Connecting,
	Connected,
	Reconnecting,
	Faulted
}
