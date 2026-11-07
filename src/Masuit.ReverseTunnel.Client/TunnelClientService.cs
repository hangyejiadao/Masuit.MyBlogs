using System.Collections.Concurrent;

namespace Masuit.ReverseTunnel.Client;

/// <summary>管理多条反向隧道连接的生命周期。</summary>
public sealed class TunnelClientService(ClientStore store, ILogger<TunnelClientService> logger, ILogger<ReverseTunnelClient> clientLogger) : BackgroundService
{
	private readonly SemaphoreSlim _reloadSignal = new(0);
	private readonly ConcurrentDictionary<long, CancellationTokenSource> _sessions = new();
	private readonly ConcurrentDictionary<long, ClientRuntimeStatus> _statuses = new();

	public IReadOnlyList<ClientRuntimeStatus> GetStatuses()
	{
		return _statuses.Values.OrderBy(status => status.Id).ToArray();
	}

	public void Reload(long? id = null)
	{
		if (id is long targetId)
		{
			StopSession(targetId);
		}
		else
		{
			foreach (long sessionId in _sessions.Keys.ToArray())
			{
				StopSession(sessionId);
			}
		}
		if (_reloadSignal.CurrentCount == 0)
		{
			_reloadSignal.Release();
		}
	}

	public void Forget(long id)
	{
		StopSession(id);
		_statuses.TryRemove(id, out _);
		if (_reloadSignal.CurrentCount == 0)
		{
			_reloadSignal.Release();
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				ReconcileSessions(stoppingToken);
				await _reloadSignal.WaitAsync(stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			foreach (long sessionId in _sessions.Keys.ToArray())
			{
				StopSession(sessionId);
			}
		}
	}

	private void ReconcileSessions(CancellationToken stoppingToken)
	{
		IReadOnlyList<ClientConfiguration> configurations = store.LoadAll();
		var enabledIds = configurations.Where(configuration => configuration.Enabled).Select(configuration => configuration.Id).ToHashSet();
		foreach (long sessionId in _sessions.Keys.Where(id => !enabledIds.Contains(id)).ToArray())
		{
			StopSession(sessionId);
		}

		foreach (ClientConfiguration configuration in configurations.Where(configuration => configuration.Enabled))
		{
			if (_sessions.ContainsKey(configuration.Id))
			{
				continue;
			}
			var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
			if (_sessions.TryAdd(configuration.Id, sessionCancellation))
			{
				_ = RunSessionLoopAsync(configuration.Id, sessionCancellation);
			}
			else
			{
				sessionCancellation.Dispose();
			}
		}
	}

	private async Task RunSessionLoopAsync(long id, CancellationTokenSource sessionCancellation)
	{
		try
		{
			while (!sessionCancellation.IsCancellationRequested)
			{
				ClientConfiguration? configuration = store.Load(id);
				if (configuration is null || !configuration.Enabled)
				{
					SetStatus(id, ClientRuntimeStatus.Stopped);
					return;
				}

				string? validationError = configuration.Validate();
				if (validationError is not null)
				{
					SetStatus(id, ClientRuntimeStatus.Faulted with { LastError = validationError });
					return;
				}

				try
				{
					SetStatus(id, ClientRuntimeStatus.Connecting);
					var client = new ReverseTunnelClient(clientLogger);
					await client.RunSessionAsync(configuration, sessionCancellation.Token, connectedAt => SetStatus(id, ClientRuntimeStatus.Connected with { ConnectedAt = connectedAt }));
					if (!sessionCancellation.IsCancellationRequested)
					{
						SetStatus(id, ClientRuntimeStatus.Reconnecting);
					}
				}
				catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
				{
					return;
				}
				catch (Exception exception)
				{
					logger.LogError(exception, "内网穿透客户端连接异常，配置={ConfigurationId}", id);
					SetStatus(id, ClientRuntimeStatus.Reconnecting with { LastError = exception.Message });
				}

				await Task.Delay(TimeSpan.FromSeconds(5), sessionCancellation.Token);
			}
		}
		catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
		{
		}
		finally
		{
			bool removed = _sessions.TryRemove(new KeyValuePair<long, CancellationTokenSource>(id, sessionCancellation));
			if (removed)
			{
				SetStatus(id, ClientRuntimeStatus.Stopped);
			}
			sessionCancellation.Dispose();
		}
	}

	private void StopSession(long id)
	{
		if (_sessions.TryRemove(id, out CancellationTokenSource? sessionCancellation))
		{
			sessionCancellation.Cancel();
		}
		SetStatus(id, ClientRuntimeStatus.Stopped);
	}

	private void SetStatus(long id, ClientRuntimeStatus status)
	{
		_statuses[id] = status with { Id = id };
	}
}

public sealed record ClientRuntimeStatus(long Id, ClientRuntimeStatusCode State, string? LastError, DateTimeOffset? ConnectedAt)
{
	public static ClientRuntimeStatus Stopped { get; } = new(0, ClientRuntimeStatusCode.Stopped, null, null);
	public static ClientRuntimeStatus Connecting { get; } = new(0, ClientRuntimeStatusCode.Connecting, null, null);
	public static ClientRuntimeStatus Connected { get; } = new(0, ClientRuntimeStatusCode.Connected, null, null);
	public static ClientRuntimeStatus Reconnecting { get; } = new(0, ClientRuntimeStatusCode.Reconnecting, null, null);
	public static ClientRuntimeStatus Faulted { get; } = new(0, ClientRuntimeStatusCode.Faulted, null, null);
}

public enum ClientRuntimeStatusCode
{
	Stopped,
	Connecting,
	Connected,
	Reconnecting,
	Faulted
}
