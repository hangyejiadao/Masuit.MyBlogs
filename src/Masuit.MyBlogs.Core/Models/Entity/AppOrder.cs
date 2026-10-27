namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 软件购买订单：博客用户在前台商店选购应用的某个价格方案后生成，
/// 支付宝支付成功后凭订单快照自动发放授权，订单与发放的授权互相回填关联。
/// </summary>
[Table("AppOrder")]
public class AppOrder : BaseEntity
{
	/// <summary>
	/// 订单号，全局唯一，同时作为支付宝交易的商户订单号（out_trade_no）；
	/// 规则：yyyyMMddHHmmss + 6 位随机数字，可读性好且不易碰撞，建单时还会查库兜底防重
	/// </summary>
	[Required, MaxLength(32)]
	public string OrderNo { get; set; }

	/// <summary>
	/// 下单的博客用户id
	/// </summary>
	public int UserInfoId { get; set; }

	/// <summary>
	/// 下单的博客用户
	/// </summary>
	public virtual UserInfo User { get; set; }

	/// <summary>
	/// 购买的应用id
	/// </summary>
	public int ApplicationInfoId { get; set; }

	/// <summary>
	/// 购买的应用
	/// </summary>
	public virtual ApplicationInfo Application { get; set; }

	/// <summary>
	/// 购买的价格方案id
	/// </summary>
	public int PricePlanId { get; set; }

	/// <summary>
	/// 购买的价格方案
	/// </summary>
	public virtual AppPricePlan PricePlan { get; set; }

	/// <summary>
	/// 下单快照：方案名称（方案事后改名、改价、删除都不影响订单展示与发授权口径）
	/// </summary>
	[Required, MaxLength(64)]
	public string PlanName { get; set; }

	/// <summary>
	/// 下单快照：方案价格（元）
	/// </summary>
	public decimal PlanPrice { get; set; }

	/// <summary>
	/// 下单快照：方案有效天数，0 表示永久
	/// </summary>
	public int PlanValidityDays { get; set; }

	/// <summary>
	/// 应付金额（元），下单时等于方案价快照
	/// </summary>
	public decimal Amount { get; set; }

	/// <summary>
	/// 订单状态：待支付 / 已支付 / 已关闭
	/// </summary>
	public AppOrderStatus OrderStatus { get; set; } = AppOrderStatus.Pending;

	/// <summary>
	/// 下单时间
	/// </summary>
	public DateTime CreatedAt { get; set; }

	/// <summary>
	/// 支付完成时间（以支付宝异步通知为准），未支付为空
	/// </summary>
	public DateTime? PaidAt { get; set; }

	/// <summary>
	/// 支付宝交易号（trade_no），支付成功后由异步通知回填
	/// </summary>
	[MaxLength(64)]
	public string AlipayTradeNo { get; set; }

	/// <summary>
	/// 支付成功后自动发放的授权id，未发放为空
	/// </summary>
	public int? IssuedLicenseId { get; set; }

	/// <summary>
	/// 支付成功后自动发放的授权
	/// </summary>
	public virtual AppLicense IssuedLicense { get; set; }
}
