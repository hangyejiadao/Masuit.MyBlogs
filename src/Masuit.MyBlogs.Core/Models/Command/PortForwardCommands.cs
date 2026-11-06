namespace Masuit.MyBlogs.Core.Models.Command;

/// <summary>
/// 端口转发规则保存参数（Id 为 0 表示新建）
/// </summary>
public class PortForwardRuleCommand
{
	/// <summary>
	/// 转发模式：0 为普通直连，1 为内网穿透。
	/// </summary>
	public PortForwardMode Mode { get; set; }

	/// <summary>
	/// 规则id，0 表示新建
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// 规则名称
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// 本机监听端口，只允许 10000-10100（Docker 发布端口段）
	/// </summary>
	public int ListenPort { get; set; }

	/// <summary>
	/// 目标主机：IP 或主机名
	/// </summary>
	public string TargetHost { get; set; }

	/// <summary>
	/// 目标端口
	/// </summary>
	public int TargetPort { get; set; }

	/// <summary>
	/// 路径转发前缀（可空），如 /mcp；非空时博客该路径的 HTTP 请求剥前缀代理转发到目标，为空不启用
	/// </summary>
	public string Path { get; set; }

	/// <summary>
	/// 是否启用
	/// </summary>
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// 备注
	/// </summary>
	public string Remark { get; set; }

	/// <summary>
	/// 内网穿透客户端注册编号；普通直连模式为空。
	/// </summary>
	public string TunnelId { get; set; }

	/// <summary>
	/// 内网穿透明文令牌；编辑时留空表示沿用原令牌，服务端不会回传原令牌。
	/// </summary>
	public string TunnelToken { get; set; }
}

/// <summary>
/// 按 id 操作的通用参数（删除等）
/// </summary>
public class PortForwardIdCommand
{
	/// <summary>
	/// 目标记录id
	/// </summary>
	public int Id { get; set; }
}
