using Masuit.MyBlogs.Core.Models.Entity;

namespace Masuit.MyBlogs.Core.Common;

/// <summary>
/// 一次端口转发连接的最近请求记录。记录只保存在服务进程内存中，服务重启后清空。
/// </summary>
public sealed class PortForwardRequestRecord
{
	private readonly object _sync = new();
	private readonly long _id;
	private readonly DateTimeOffset _startedAt;
	private long _bytes;
	private string _status = "处理中";
	private string _error;
	private DateTimeOffset? _completedAt;

	public PortForwardRequestRecord(long id, PortForwardRule rule, string remoteIp, int? remotePort)
	{
		_id = id;
		_startedAt = DateTimeOffset.UtcNow;
		RuleId = rule.Id;
		RuleName = rule.Name;
		Mode = rule.Mode;
		ListenPort = rule.ListenPort;
		TunnelId = rule.TunnelId;
		RemoteIp = remoteIp;
		RemotePort = remotePort;
	}

	public int RuleId { get; }

	public string RuleName { get; }

	public PortForwardMode Mode { get; }

	public int ListenPort { get; }

	public string TunnelId { get; }

	public string RemoteIp { get; }

	public int? RemotePort { get; }

	public void AddBytes(long bytes)
	{
		if (bytes > 0)
		{
			Interlocked.Add(ref _bytes, bytes);
		}
	}

	public void MarkCompleted()
	{
		Finish("已完成", null);
	}

	public void MarkCancelled()
	{
		Finish("已取消", null);
	}

	public void MarkFailed(string error)
	{
		Finish("失败", string.IsNullOrWhiteSpace(error) ? "转发失败" : Limit(error.Trim(), 512));
	}

	public PortForwardRequestRecordSnapshot Snapshot()
	{
		lock (_sync)
		{
			var completedAt = _completedAt;
			var end = completedAt ?? DateTimeOffset.UtcNow;
			return new PortForwardRequestRecordSnapshot
			{
				Id = _id,
				RuleId = RuleId,
				RuleName = RuleName,
				Mode = Mode,
				ListenPort = ListenPort,
				TunnelId = TunnelId,
				RemoteIp = RemoteIp,
				RemotePort = RemotePort,
				StartedAt = _startedAt,
				CompletedAt = completedAt,
				DurationMs = Math.Max(0, (long)(end - _startedAt).TotalMilliseconds),
				Status = _status,
				Error = _error,
				Bytes = Interlocked.Read(ref _bytes)
			};
		}
	}

	private void Finish(string status, string error)
	{
		lock (_sync)
		{
			if (_completedAt.HasValue)
			{
				return;
			}
			_status = status;
			_error = error;
			_completedAt = DateTimeOffset.UtcNow;
		}
	}

	private static string Limit(string value, int maxLength)
	{
		return value.Length <= maxLength ? value : value[..maxLength];
	}
}

/// <summary>
/// 提供给管理接口和前端的端口转发请求记录快照。
/// </summary>
public sealed class PortForwardRequestRecordSnapshot
{
	public long Id { get; init; }

	public int RuleId { get; init; }

	public string RuleName { get; init; }

	public PortForwardMode Mode { get; init; }

	public int ListenPort { get; init; }

	public string TunnelId { get; init; }

	public string RemoteIp { get; init; }

	public int? RemotePort { get; init; }

	public DateTimeOffset StartedAt { get; init; }

	public DateTimeOffset? CompletedAt { get; init; }

	public long DurationMs { get; init; }

	public string Status { get; init; }

	public string Error { get; init; }

	public long Bytes { get; init; }
}
