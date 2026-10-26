using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Masuit.MyBlogs.Core.Common;

/// <summary>
/// 一条规则的运行时计数（活跃连接、累计连接、累计字节），TCP 转发与路径代理共用同一套计数
/// </summary>
public sealed class PortForwardCounters
{
	/// <summary>
	/// 当前活跃连接数，由 Interlocked 维护
	/// </summary>
	public long ActiveConnections;

	/// <summary>
	/// 累计处理连接/请求数，由 Interlocked 维护
	/// </summary>
	public long TotalConnections;

	/// <summary>
	/// 累计转发字节数，由 Interlocked 维护
	/// </summary>
	public long TotalBytes;
}

/// <summary>
/// 一条路径转发规则的运行时快照：博客侧的挂载前缀与代理目标，供代理中间件逐请求匹配
/// </summary>
public sealed class PortForwardPathTarget
{
	/// <summary>
	/// 规则id
	/// </summary>
	public int RuleId { get; init; }

	/// <summary>
	/// 博客侧挂载前缀，如 /mcp；匹配时不区分大小写，要求段边界，转发时剥掉
	/// </summary>
	public string Path { get; init; }

	/// <summary>
	/// 目标主机：IP 或主机名
	/// </summary>
	public string TargetHost { get; init; }

	/// <summary>
	/// 目标端口
	/// </summary>
	public int TargetPort { get; init; }
}

/// <summary>
/// 一条端口转发规则的运行时状态快照，供管理列表合并展示
/// </summary>
public sealed class PortForwardStatus
{
	/// <summary>
	/// 是否正在监听
	/// </summary>
	public bool IsListening { get; set; }

	/// <summary>
	/// 状态说明：监听中 / 已停用 / 监听失败原因等
	/// </summary>
	public string StatusMessage { get; set; }

	/// <summary>
	/// 当前活跃连接数
	/// </summary>
	public long ActiveConnections { get; set; }

	/// <summary>
	/// 累计接受连接数
	/// </summary>
	public long TotalConnections { get; set; }

	/// <summary>
	/// 累计转发字节数（双向合计）
	/// </summary>
	public long TotalBytes { get; set; }
}

/// <summary>
/// 端口转发引擎：按数据库中已启用的规则维护本机 TCP 监听，把每个连接双向转发到目标主机端口。
/// 以单例 + 宿主服务形式运行，规则增删改后由控制器调用 <see cref="ReloadAsync"/> 即时对齐监听，无需重启应用。
/// 单条规则监听失败（端口被占用等）只记录该规则的状态并写日志，不影响应用和其他规则。
/// </summary>
public sealed class PortForwardRuntime(IServiceScopeFactory scopeFactory, ILogger<PortForwardRuntime> logger) : IHostedService
{
	/// <summary>
	/// 连接目标主机时等待的最长秒数，超时放弃该连接
	/// </summary>
	private const int ConnectTimeoutSeconds = 15;

	/// <summary>
	/// 路径代理专用 HttpClient 的名称：无超时（长连接流式响应靠请求取消令牌收尾）、不跟随重定向、不解压，保证代理透明
	/// </summary>
	public const string ProxyHttpClientName = "PortForwardProxy";

	/// <summary>
	/// 当前实际运行的监听，键为规则id
	/// </summary>
	private readonly ConcurrentDictionary<int, ActiveForward> _active = new();

	/// <summary>
	/// 每条规则的运行时计数，键为规则id；TCP 转发与路径代理共用，规则不在监听时（如只用路径代理）也能累计
	/// </summary>
	private readonly ConcurrentDictionary<int, PortForwardCounters> _counters = new();

	/// <summary>
	/// 当前生效的路径转发规则快照，按前缀长度降序排列（最长前缀优先）；Reload 时整体原子替换，中间件逐请求只读
	/// </summary>
	private volatile PortForwardPathTarget[] _pathTargets = [];

	/// <summary>
	/// 最近一次监听启动失败的说明，键为规则id；成功启动后清除
	/// </summary>
	private readonly ConcurrentDictionary<int, string> _startErrors = new();

	/// <summary>
	/// 串行化多次同步，避免并发增删规则时重复绑定同一端口
	/// </summary>
	private readonly SemaphoreSlim _syncGate = new(1, 1);

	/// <summary>
	/// 宿主启动时按已启用规则开启全部监听；加载失败只记日志，不能拖垮应用启动
	/// </summary>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			await ReloadAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "端口转发引擎启动加载规则失败");
		}
	}

	/// <summary>
	/// 宿主停止时关闭全部监听并断开现有连接
	/// </summary>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public async Task StopAsync(CancellationToken cancellationToken)
	{
		foreach (var ruleId in _active.Keys)
		{
			if (_active.TryRemove(ruleId, out var forward))
			{
				await forward.StopAsync();
			}
		}
	}

	/// <summary>
	/// 把实际监听与数据库中已启用的规则对齐：新增的启动、删除/停用/改过的先停后启、未变化的保持不动。
	/// 规则保存或删除后由控制器调用，立即生效；方法内部兜底全部异常，不向调用方抛错
	/// </summary>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public async Task ReloadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			await _syncGate.WaitAsync(cancellationToken);
			try
			{
				List<PortForwardRule> rules;
				await using (var scope = scopeFactory.CreateAsyncScope())
				{
					var service = scope.ServiceProvider.GetRequiredService<IPortForwardRuleService>();
					rules = service.GetQuery(r => r.Enabled).ToList();
				}

				_pathTargets = rules.Where(r => !string.IsNullOrWhiteSpace(r.Path))
					.Select(r => new PortForwardPathTarget
					{
						RuleId = r.Id,
						Path = r.Path.Trim(),
						TargetHost = r.TargetHost,
						TargetPort = r.TargetPort
					})
					.OrderByDescending(t => t.Path.Length)
					.ToArray();

				var desired = rules.ToDictionary(r => r.Id);
				foreach (var ruleId in _active.Keys)
				{
					var keep = desired.TryGetValue(ruleId, out var rule)
						&& _active.TryGetValue(ruleId, out var current)
						&& current.Fingerprint == Fingerprint(rule);
					if (!keep && _active.TryRemove(ruleId, out var removed))
					{
						await removed.StopAsync();
					}
				}

				foreach (var rule in rules)
				{
					if (!_active.ContainsKey(rule.Id))
					{
						StartForward(rule);
					}
				}
			}
			finally
			{
				_syncGate.Release();
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "端口转发引擎同步规则失败");
		}
	}

	/// <summary>
	/// 读取一条规则当前的运行时状态：启用且在监听则带连接计数；启用但启动失败则带失败原因；其余返回未监听状态
	/// </summary>
	/// <param name="rule">规则（用于判断是否启用）</param>
	/// <returns></returns>
	public PortForwardStatus GetStatus(PortForwardRule rule)
	{
		if (!rule.Enabled)
		{
			return new PortForwardStatus { StatusMessage = "已停用" };
		}

		var counters = GetCounters(rule.Id);
		var status = new PortForwardStatus
		{
			ActiveConnections = Interlocked.Read(ref counters.ActiveConnections),
			TotalConnections = Interlocked.Read(ref counters.TotalConnections),
			TotalBytes = Interlocked.Read(ref counters.TotalBytes)
		};
		if (_active.ContainsKey(rule.Id))
		{
			status.IsListening = true;
			status.StatusMessage = "监听中";
			return status;
		}

		status.StatusMessage = _startErrors.TryGetValue(rule.Id, out var error) ? error : "未启动";
		return status;
	}

	/// <summary>
	/// 取得一条规则的运行时计数（不存在则创建）；TCP 转发与路径代理共用
	/// </summary>
	/// <param name="ruleId"></param>
	/// <returns></returns>
	public PortForwardCounters GetCounters(int ruleId) => _counters.GetOrAdd(ruleId, static _ => new PortForwardCounters());

	/// <summary>
	/// 按请求路径匹配路径转发规则：路径等于规则前缀、或以「前缀 + /」开头（段边界）即命中，快照已按前缀长度降序排列，最长前缀优先；
	/// 未命中返回 null。供代理中间件逐请求调用，只读快照、零分配
	/// </summary>
	/// <param name="requestPath">请求路径（不含查询串）</param>
	/// <returns></returns>
	public PortForwardPathTarget MatchPath(string requestPath)
	{
		if (string.IsNullOrEmpty(requestPath))
		{
			return null;
		}

		foreach (var target in _pathTargets)
		{
			if (requestPath.Equals(target.Path, StringComparison.OrdinalIgnoreCase)
				|| requestPath.StartsWith(target.Path + "/", StringComparison.OrdinalIgnoreCase))
			{
				return target;
			}
		}

		return null;
	}

	/// <summary>
	/// 生成决定监听是否需要重启的规则指纹
	/// </summary>
	/// <param name="rule"></param>
	/// <returns></returns>
	private static string Fingerprint(PortForwardRule rule) => $"{rule.ListenPort}|{rule.TargetHost}|{rule.TargetPort}";

	/// <summary>
	/// 为一条规则创建监听并开始接受连接；绑定失败时记录原因供界面展示并写日志，不向上抛错
	/// </summary>
	/// <param name="rule"></param>
	private void StartForward(PortForwardRule rule)
	{
		TcpListener listener;
		try
		{
			listener = CreateListener(rule.ListenPort);
		}
		catch (Exception ex)
		{
			_startErrors[rule.Id] = $"监听失败：{ex.Message}";
			logger.LogError(ex, "端口转发监听启动失败，规则={RuleId}，监听端口={ListenPort}", rule.Id, rule.ListenPort);
			return;
		}

		var forward = new ActiveForward(rule, listener, GetCounters(rule.Id));
		_active[rule.Id] = forward;
		_startErrors.TryRemove(rule.Id, out _);
		forward.AcceptTask = AcceptLoopAsync(forward);
		logger.LogInformation("端口转发已启动：{ListenPort} -> {TargetHost}:{TargetPort}（规则 {RuleId}）", rule.ListenPort, rule.TargetHost, rule.TargetPort, rule.Id);
	}

	/// <summary>
	/// 创建监听套接字：优先 IPv6 双栈（同时接受 IPv4 与 IPv6），绑定失败时回退到 IPv4
	/// </summary>
	/// <param name="port"></param>
	/// <returns></returns>
	private TcpListener CreateListener(int port)
	{
		if (!Socket.OSSupportsIPv6)
		{
			var ipv4Only = new TcpListener(IPAddress.Any, port);
			ipv4Only.Start();
			return ipv4Only;
		}

		try
		{
			var listener = new TcpListener(IPAddress.IPv6Any, port);
			listener.Server.DualMode = true;
			listener.Start();
			return listener;
		}
		catch (SocketException ex)
		{
			logger.LogError(ex, "IPv6 双栈监听绑定失败，回退到 IPv4 监听，端口={ListenPort}", port);
			var fallback = new TcpListener(IPAddress.Any, port);
			fallback.Start();
			return fallback;
		}
	}

	/// <summary>
	/// 持续接受新连接并为每个连接启动独立的转发处理
	/// </summary>
	/// <param name="forward"></param>
	/// <returns></returns>
	private async Task AcceptLoopAsync(ActiveForward forward)
	{
		while (!forward.StopSource.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await forward.Listener.AcceptTcpClientAsync(forward.StopSource.Token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "端口转发接受连接失败，规则={RuleId}，监听端口={ListenPort}", forward.Rule.Id, forward.Rule.ListenPort);
				break;
			}

			_ = HandleConnectionAsync(forward, client);
		}
	}

	/// <summary>
	/// 处理单个转发连接：连接目标后双向搬运数据，任一方向结束即关闭整条连接
	/// </summary>
	/// <param name="forward"></param>
	/// <param name="client"></param>
	/// <returns></returns>
	private async Task HandleConnectionAsync(ActiveForward forward, TcpClient client)
	{
		Interlocked.Increment(ref forward.Counters.TotalConnections);
		Interlocked.Increment(ref forward.Counters.ActiveConnections);
		using var clientScope = client;
		using var remote = new TcpClient();
		try
		{
			using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
			await remote.ConnectAsync(forward.Rule.TargetHost, forward.Rule.TargetPort, connectTimeout.Token);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "端口转发连接目标失败，规则={RuleId}，目标={TargetHost}:{TargetPort}", forward.Rule.Id, forward.Rule.TargetHost, forward.Rule.TargetPort);
			Interlocked.Decrement(ref forward.Counters.ActiveConnections);
			return;
		}

		try
		{
			using var connectionSource = CancellationTokenSource.CreateLinkedTokenSource(forward.StopSource.Token);
			var clientStream = client.GetStream();
			var remoteStream = remote.GetStream();
			var upload = PumpAsync(clientStream, remoteStream, forward, connectionSource.Token);
			var download = PumpAsync(remoteStream, clientStream, forward, connectionSource.Token);
			await Task.WhenAny(upload, download);
			connectionSource.Cancel();
			try
			{
				await Task.WhenAll(upload, download);
			}
			catch (Exception ex)
			{
				// 任一方向先结束时取消另一方向属于正常收尾，这里只记录不处理
				logger.LogError(ex, "端口转发连接收尾异常，规则={RuleId}", forward.Rule.Id);
			}
		}
		finally
		{
			Interlocked.Decrement(ref forward.Counters.ActiveConnections);
		}
	}

	/// <summary>
	/// 把一个方向的字节流持续搬运到另一端，并累计转发字节数
	/// </summary>
	/// <param name="source"></param>
	/// <param name="destination"></param>
	/// <param name="forward"></param>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	private static async Task PumpAsync(NetworkStream source, NetworkStream destination, ActiveForward forward, CancellationToken cancellationToken)
	{
		var buffer = new byte[64 * 1024];
		while (true)
		{
			var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
			if (read == 0)
			{
				break;
			}

			await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			Interlocked.Add(ref forward.Counters.TotalBytes, read);
		}
	}

	/// <summary>
	/// 一条正在运行的监听及其连接计数
	/// </summary>
	/// <param name="rule"></param>
	/// <param name="listener"></param>
	/// <param name="counters">与路径代理共用的运行时计数</param>
	private sealed class ActiveForward(PortForwardRule rule, TcpListener listener, PortForwardCounters counters)
	{
		/// <summary>
		/// 监听对应的规则快照
		/// </summary>
		public PortForwardRule Rule { get; } = rule;

		/// <summary>
		/// 决定是否需要重启监听的规则指纹
		/// </summary>
		public string Fingerprint { get; } = $"{rule.ListenPort}|{rule.TargetHost}|{rule.TargetPort}";

		/// <summary>
		/// 已开始监听的套接字
		/// </summary>
		public TcpListener Listener { get; } = listener;

		/// <summary>
		/// 停止该监听时取消接受循环与全部连接的令牌源
		/// </summary>
		public CancellationTokenSource StopSource { get; } = new();

		/// <summary>
		/// 接受连接循环的任务
		/// </summary>
		public Task AcceptTask { get; set; } = Task.CompletedTask;

		/// <summary>
		/// 运行时计数，与同规则的路径代理共用
		/// </summary>
		public PortForwardCounters Counters { get; } = counters;

		/// <summary>
		/// 停止监听：取消全部连接处理、关闭套接字并等待接受循环退出
		/// </summary>
		/// <returns></returns>
		public async Task StopAsync()
		{
			await StopSource.CancelAsync();
			Listener.Stop();
			try
			{
				await AcceptTask.WaitAsync(TimeSpan.FromSeconds(5));
			}
			catch (Exception)
			{
				// 接受循环以取消结束或未能及时退出都不影响后续同步，监听套接字已关闭，这里忽略
			}

			StopSource.Dispose();
		}
	}
}

public static class PortForwardServiceExtension
{
	/// <summary>
	/// 注册端口转发引擎：单例既作为宿主服务随应用启停，也供控制器读取状态与触发规则同步
	/// </summary>
	/// <param name="services"></param>
	/// <returns></returns>
	public static IServiceCollection AddPortForward(this IServiceCollection services)
	{
		services.AddSingleton<PortForwardRuntime>();
		services.AddHostedService(sp => sp.GetRequiredService<PortForwardRuntime>());
		services.AddHttpClient(PortForwardRuntime.ProxyHttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
			.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
			{
				AllowAutoRedirect = false,
				UseCookies = false,
				AutomaticDecompression = DecompressionMethods.None,
				PooledConnectionLifetime = TimeSpan.FromMinutes(5)
			});
		return services;
	}
}
