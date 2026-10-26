namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 端口转发规则：把本机监听端口收到的 TCP 连接双向转发到目标主机端口。
/// 博客运行在 Docker 容器中，只有 10000-10100 端口段会发布到宿主机，监听端口只能落在该区间内
/// </summary>
[Table("PortForwardRule")]
public class PortForwardRule : BaseEntity
{
	/// <summary>
	/// 规则名称
	/// </summary>
	[Required(ErrorMessage = "规则名称不能为空！"), MaxLength(64)]
	public string Name { get; set; }

	/// <summary>
	/// 本机监听端口，全局唯一，只允许 10000-10100
	/// </summary>
	public int ListenPort { get; set; }

	/// <summary>
	/// 目标主机：IP 或主机名（从博客容器视角可达的地址）
	/// </summary>
	[Required(ErrorMessage = "目标地址不能为空！"), MaxLength(256)]
	public string TargetHost { get; set; }

	/// <summary>
	/// 目标主机上的服务端口
	/// </summary>
	public int TargetPort { get; set; }

	/// <summary>
	/// 路径转发前缀（可空），如 /mcp。非空时博客收到该路径或其子路径的 HTTP 请求会剥掉此前缀、
	/// 把剩余路径代理转发到目标主机端口；为空表示不启用路径转发，与 TCP 监听相互独立
	/// </summary>
	[MaxLength(128)]
	public string Path { get; set; }

	/// <summary>
	/// 是否启用；启用后转发引擎会实际监听该端口
	/// </summary>
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// 备注
	/// </summary>
	[MaxLength(512)]
	public string Remark { get; set; }
}
