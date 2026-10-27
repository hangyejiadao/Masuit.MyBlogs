namespace Masuit.MyBlogs.Core.Models.Enum;

/// <summary>
/// 软件购买订单状态
/// </summary>
public enum AppOrderStatus
{
	/// <summary>
	/// 待支付
	/// </summary>
	[Display(Name = "待支付")] Pending = 0,

	/// <summary>
	/// 已支付（已自动发放授权）
	/// </summary>
	[Display(Name = "已支付")] Paid = 1,

	/// <summary>
	/// 已关闭（未支付，被用户或管理员关闭）
	/// </summary>
	[Display(Name = "已关闭")] Closed = 2
}
