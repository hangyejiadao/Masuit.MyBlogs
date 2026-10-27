using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Essensoft.Paylinks.Alipay.Client;
using Essensoft.Paylinks.Alipay.Payments.Notify;
using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Infrastructure.Services.Interface;

namespace Masuit.MyBlogs.Core.Infrastructure.Services;

/// <summary>
/// 支付宝支付服务实现：
/// 电脑网站支付的表单与 RSA2 签名在本类内按支付宝网关协议自行组装（参数构成与签名口径与官方 SDK 一致，
/// 已用 Paylinks 的验签方法交叉验证可互通）；异步通知的验签与模型映射仍走 Essensoft.Paylinks 的 NotifyClient。
/// 不用 Paylinks 的 PageExecuteAsync 发起支付的原因：其内部签名走 RSA.ImportRSAPrivateKey 的 Span 重载，
/// 在当前运行环境的 .NET + OpenSSL 组合下会以「private key decode error」失败（同一密钥用
/// ImportPkcs8PrivateKey 的 byte[] 重载导入完全正常、密钥本身合法），本类改用后者完成同一签名以规避该运行时缺陷。
/// </summary>
/// <param name="notifyClient">支付宝通知验签客户端</param>
public sealed class AlipayPayService(IAlipayNotifyClient notifyClient) : IAlipayPayService
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
	public Task<string> BuildPagePayFormAsync(AppOrder order, string subject, string notifyUrl, string returnUrl)
	{
		var config = AppConfig.Alipay;
		var gateway = string.IsNullOrWhiteSpace(config.Gateway) ? "https://openapi.alipay.com/gateway.do" : config.Gateway.Trim();
		var bizContent = JsonSerializer.Serialize(new Dictionary<string, string>
		{
			["out_trade_no"] = order.OrderNo,
			["total_amount"] = order.Amount.ToString("0.00", CultureInfo.InvariantCulture),
			["subject"] = subject,
			["product_code"] = "FAST_INSTANT_TRADE_PAY"
		});
		var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
		{
			["app_id"] = config.AppId?.Trim(),
			["biz_content"] = bizContent,
			["charset"] = "utf-8",
			["format"] = "JSON",
			["method"] = "alipay.trade.page.pay",
			["notify_url"] = notifyUrl,
			["return_url"] = returnUrl,
			["sign_type"] = "RSA2",
			["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
			["version"] = "1.0"
		};
		var signContent = string.Join("&", parameters.Select(parameter => $"{parameter.Key}={parameter.Value}"));
		using var rsa = RSA.Create();
		rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(config.PrivateKey.Trim()), out _);
		var signature = rsa.SignData(Encoding.UTF8.GetBytes(signContent), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		parameters["sign"] = Convert.ToBase64String(signature);
		var form = new StringBuilder();
		form.Append("<form id=\"alipaysubmit\" name=\"alipaysubmit\" action=\"");
		form.Append(gateway);
		form.Append("\" method=\"POST\">");
		foreach (var parameter in parameters)
		{
			form.Append("<input type=\"hidden\" name=\"");
			form.Append(parameter.Key);
			form.Append("\" value=\"");
			form.Append(WebUtility.HtmlEncode(parameter.Value));
			form.Append("\"/>");
		}

		form.Append("<input type=\"submit\" value=\"确认\" style=\"display:none\"/></form>");
		form.Append("<script>document.forms['alipaysubmit'].submit();</script>");
		return Task.FromResult(form.ToString());
	}

	/// <inheritdoc />
	public async Task<AlipayTradeStatusSyncNotify> VerifyNotifyAsync(IDictionary<string, string> parameters)
	{
		return await notifyClient.ExecuteAsync<AlipayTradeStatusSyncNotify>(parameters, CreateOptions());
	}
}
