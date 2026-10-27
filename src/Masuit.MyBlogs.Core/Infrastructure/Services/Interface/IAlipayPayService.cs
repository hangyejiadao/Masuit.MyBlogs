using Essensoft.Paylinks.Alipay.Payments.Notify;

namespace Masuit.MyBlogs.Core.Infrastructure.Services.Interface;

/// <summary>
/// 支付宝支付服务：封装电脑网站支付（alipay.trade.page.pay）的下单表单构建与异步通知验签。
/// 凭据从 AppConfig.Alipay 读取（服务器 appsettings.json 的 Alipay 节），按请求传入 Paylinks 客户端；
/// 凭据未配置时 Configured 为 false，调用方必须拦截，绝不能绕过支付。
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
	/// 校验支付宝异步通知的签名并解析为通知模型；验签失败会抛出异常，调用方据此回 failure
	/// </summary>
	/// <param name="parameters">通知的全部表单参数</param>
	/// <returns>验签通过的交易状态通知</returns>
	Task<AlipayTradeStatusSyncNotify> VerifyNotifyAsync(IDictionary<string, string> parameters);
}
