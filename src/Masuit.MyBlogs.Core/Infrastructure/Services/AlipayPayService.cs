using Essensoft.Paylinks.Alipay.Client;
using Essensoft.Paylinks.Alipay.Payments.Model;
using Essensoft.Paylinks.Alipay.Payments.Notify;
using Essensoft.Paylinks.Alipay.Payments.Request;
using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Infrastructure.Services.Interface;

namespace Masuit.MyBlogs.Core.Infrastructure.Services;

/// <summary>
/// 支付宝支付服务实现，基于 Essensoft.Paylinks.Alipay（Client + Payments）：
/// PageExecuteAsync 生成带 RSA2 签名的电脑网站支付表单，NotifyClient 负责异步通知验签与模型映射。
/// </summary>
/// <param name="client">支付宝 API 客户端</param>
/// <param name="notifyClient">支付宝通知验签客户端</param>
public sealed class AlipayPayService(IAlipayClient client, IAlipayNotifyClient notifyClient) : IAlipayPayService
{
	/// <summary>
	/// 支付通道是否已配置可用
	/// </summary>
	public bool Configured => AppConfig.Alipay.Configured;

	/// <summary>
	/// 把站点配置转换为 Paylinks 的按请求选项（网关缺省回落到正式环境地址）
	/// </summary>
	/// <returns></returns>
	private static AlipayClientOptions CreateOptions()
	{
		var config = AppConfig.Alipay;
		return new AlipayClientOptions
		{
			ServerUrl = string.IsNullOrWhiteSpace(config.Gateway) ? "https://openapi.alipay.com/gateway.do" : config.Gateway.Trim(),
			AppId = config.AppId?.Trim(),
			AppPrivateKey = config.PrivateKey?.Trim(),
			AlipayPublicKey = config.AlipayPublicKey?.Trim()
		};
	}

	/// <inheritdoc />
	public async Task<string> BuildPagePayFormAsync(AppOrder order, string subject, string notifyUrl, string returnUrl)
	{
		var request = new AlipayTradePagePayRequest();
		request.SetBizModel(new AlipayTradePagePayBizModel
		{
			OutTradeNo = order.OrderNo,
			TotalAmount = order.Amount.ToString("0.00"),
			Subject = subject,
			ProductCode = "FAST_INSTANT_TRADE_PAY"
		});
		request.SetNotifyUrl(notifyUrl);
		request.SetReturnUrl(returnUrl);
		return await client.PageExecuteAsync(request, CreateOptions());
	}

	/// <inheritdoc />
	public async Task<AlipayTradeStatusSyncNotify> VerifyNotifyAsync(IDictionary<string, string> parameters)
	{
		return await notifyClient.ExecuteAsync<AlipayTradeStatusSyncNotify>(parameters, CreateOptions());
	}
}
