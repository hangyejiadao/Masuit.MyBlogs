using Essensoft.Paylinks.Alipay.Payments.Notify;

namespace Masuit.MyBlogs.Core.Infrastructure.Services.Interface;

/// <summary>
/// 支付宝支付服务：封装电脑网站支付（alipay.trade.page.pay）的下单表单构建、
/// 当面付扫码支付（alipay.trade.precreate）的二维码生成与交易查询、以及异步通知验签。
/// 凭据从 AppConfig.Alipay 读取（服务器 appsettings.json 的 Alipay 节），按请求传入 Paylinks 客户端；
/// 凭据未配置时 Configured 为 false，调用方必须拦截，绝不能绕过支付。
/// 当面付与电脑网站支付共用同一套应用凭据，但产品不同：当面付只需在开放平台签约「当面付」产品，
/// 扫码支付在支付宝 App 内完成，不需要在自有域名上挂支付页面，因此不需要域名 ICP 备案。
/// </summary>
public interface IAlipayPayService
{
	/// <summary>
	/// 支付通道是否已配置可用（启用开关 + AppId + 私钥 + 支付宝公钥齐全）
	/// </summary>
	bool Configured { get; }

	/// <summary>
	/// 为一笔待支付订单构建支付宝电脑网站支付的自动提交表单 HTML（整页跳转支付宝收银台）
	/// </summary>
	/// <param name="order">待支付订单，订单号即商户订单号</param>
	/// <param name="subject">订单标题，展示在支付宝收银台</param>
	/// <param name="notifyUrl">异步通知地址</param>
	/// <param name="returnUrl">同步跳转地址</param>
	/// <returns>可直接整页输出的支付表单 HTML</returns>
	Task<string> BuildPagePayFormAsync(AppOrder order, string subject, string notifyUrl, string returnUrl);

	/// <summary>
	/// 当面付扫码支付预下单（alipay.trade.precreate）：为一笔打赏订单生成支付二维码内容。
	/// 二维码有效期 2 小时（timeout_express=2h），过期需重新下单；调用方须把返回的 QrCode 落库，
	/// 前端据此生成二维码图片供访客用支付宝 App 扫码支付。
	/// </summary>
	/// <param name="outTradeNo">商户订单号（打赏订单号）</param>
	/// <param name="amount">金额（元），保留两位小数</param>
	/// <param name="subject">订单标题，展示在支付宝付款页</param>
	/// <param name="notifyUrl">异步通知地址</param>
	/// <returns>预下单结果：二维码内容与商户订单号；网关返回非 10000 时抛异常，调用方按失败处理</returns>
	Task<AlipayPrecreateResult> CreatePrecreateQrAsync(string outTradeNo, decimal amount, string subject, string notifyUrl);

	/// <summary>
	/// 查询一笔支付宝交易的状态（alipay.trade.query），供前端轮询兜底：
	/// 异步通知延迟或丢失时，靠主动查询确认收款，避免用户付了钱页面却一直显示待支付。
	/// </summary>
	/// <param name="outTradeNo">商户订单号</param>
	/// <returns>交易查询结果：交易状态（WAIT_BUYER_PAY / TRADE_SUCCESS / TRADE_FINISHED / TRADE_CLOSED）、支付宝交易号、实付金额；订单不存在时 TradeStatus 为空</returns>
	Task<AlipayTradeQueryResult> QueryTradeAsync(string outTradeNo);

	/// <summary>
	/// 校验支付宝异步通知的签名并解析为通知模型；验签失败会抛出异常，调用方据此回 failure
	/// </summary>
	/// <param name="parameters">通知的全部表单参数</param>
	/// <returns>验签通过的交易状态通知</returns>
	Task<AlipayTradeStatusSyncNotify> VerifyNotifyAsync(IDictionary<string, string> parameters);
}

/// <summary>
/// 当面付预下单结果
/// </summary>
public sealed class AlipayPrecreateResult
{
	/// <summary>
	/// 商户订单号
	/// </summary>
	public required string OutTradeNo { get; init; }

	/// <summary>
	/// 支付二维码内容（qr_code），前端据此生成二维码图片
	/// </summary>
	public required string QrCode { get; init; }
}

/// <summary>
/// 支付宝交易查询结果
/// </summary>
public sealed class AlipayTradeQueryResult
{
	/// <summary>
	/// 交易状态：WAIT_BUYER_PAY（待支付）、TRADE_SUCCESS（已支付）、TRADE_FINISHED（交易完成）、TRADE_CLOSED（已关闭）；查无此单时为空
	/// </summary>
	public string TradeStatus { get; init; }

	/// <summary>
	/// 支付宝交易号（trade_no），未支付时为空
	/// </summary>
	public string TradeNo { get; init; }

	/// <summary>
	/// 实付金额（total_amount），未支付时为空
	/// </summary>
	public string TotalAmount { get; init; }

	/// <summary>
	/// 是否已支付成功（TRADE_SUCCESS 或 TRADE_FINISHED）
	/// </summary>
	public bool IsPaid => TradeStatus is "TRADE_SUCCESS" or "TRADE_FINISHED";
}
