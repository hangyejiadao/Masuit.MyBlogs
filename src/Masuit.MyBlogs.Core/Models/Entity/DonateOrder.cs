namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 打赏订单：访客在文章页扫码打赏时生成，走支付宝当面付（alipay.trade.precreate）；
/// 支付成功后由异步通知（状态查询接口兜底）置为已支付，并同步写入一条 Donate 记录用于打赏墙展示。
/// 与商店订单 AppOrder 的区别：打赏无需登录、无需绑定博客用户，昵称与留言均为访客自填。
/// </summary>
[Table("DonateOrder")]
public class DonateOrder : BaseEntity
{
	/// <summary>
	/// 订单号，全局唯一，同时作为支付宝交易的商户订单号（out_trade_no）；
	/// 规则与 AppOrder 一致：yyyyMMddHHmmss + 6 位随机数字，建单时查库兜底防重
	/// </summary>
	[Required, MaxLength(32)]
	public string OrderNo { get; set; }

	/// <summary>
	/// 打赏人昵称（访客自填，可空；为空时展示为“热心网友”）
	/// </summary>
	[MaxLength(32)]
	public string NickName { get; set; }

	/// <summary>
	/// 打赏留言（访客自填，可空）
	/// </summary>
	[MaxLength(200)]
	public string Message { get; set; }

	/// <summary>
	/// 打赏金额（元），下单时确定，异步通知与状态查询都以此金额校验
	/// </summary>
	public decimal Amount { get; set; }

	/// <summary>
	/// 订单状态：待支付 / 已支付 / 已关闭，复用 AppOrderStatus
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
	/// 当面付预下单返回的二维码内容（qr_code），前端据此生成二维码图片供扫码；
	/// 二维码有效期 2 小时（预下单时 timeout_express=2h），过期需重新下单
	/// </summary>
	[MaxLength(512)]
	public string QrCode { get; set; }
}
