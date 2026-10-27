namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 刹车游戏（REDLINE）排行榜记录：每个 IP 只保留一条最好成绩
/// </summary>
[Table("BrakeLeaderboardEntry")]
public class BrakeLeaderboardEntry : BaseEntity
{
	/// <summary>
	/// 提交成绩的客户端 IP（IPv4 或 IPv6），全表唯一
	/// </summary>
	[Required(ErrorMessage = "IP不能为空！"), MaxLength(45)]
	public string Ip { get; set; }

	/// <summary>
	/// IP 归属区域（省份+城市），解析不到时为「未知」
	/// </summary>
	[Required(ErrorMessage = "区域不能为空！"), MaxLength(128)]
	public string Region { get; set; }

	/// <summary>
	/// 刹停总用时（秒），榜单分数，越小越靠前
	/// </summary>
	public double Time { get; set; }

	/// <summary>
	/// 反应时间（秒）
	/// </summary>
	public double Reaction { get; set; }

	/// <summary>
	/// 制动距离（米）
	/// </summary>
	public double Distance { get; set; }

	/// <summary>
	/// 峰值刹车力度（N）
	/// </summary>
	public double PeakForce { get; set; }

	/// <summary>
	/// 首次提交时间
	/// </summary>
	public DateTime CreatedAt { get; set; }

	/// <summary>
	/// 最好成绩最近一次更新时间
	/// </summary>
	public DateTime UpdatedAt { get; set; }
}
