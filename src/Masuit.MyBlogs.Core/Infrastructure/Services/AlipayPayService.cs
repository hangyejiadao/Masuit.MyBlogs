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
/// 当面付（alipay.trade.precreate / alipay.trade.query）同样走本类的手工 RSA2 签名 + HttpClient 直调网关，
/// 并对网关响应的 sign 做 RSA2 验签，避免只信明文 code；异步通知复用同一套凭据的 Paylinks 验签。
/// </summary>
/// <param name="notifyClient">支付宝通知验签客户端</param>
/// <param name="httpClientFactory">网关直调（当面付预下单、交易查询）的 HttpClient 工厂</param>
public sealed class AlipayPayService(IAlipayNotifyClient notifyClient, IHttpClientFactory httpClientFactory) : IAlipayPayService
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
		parameters["sign"] = SignParameters(parameters, config.PrivateKey);
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
	public async Task<AlipayPrecreateResult> CreatePrecreateQrAsync(string outTradeNo, decimal amount, string subject, string notifyUrl)
	{
		var config = AppConfig.Alipay;
		var bizContent = JsonSerializer.Serialize(new Dictionary<string, string>
		{
			["out_trade_no"] = outTradeNo,
			["total_amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture),
			["subject"] = subject,
			// 二维码有效期 2 小时，过期后访客需重新下单（前端据此提示）
			["timeout_express"] = "2h"
		});
		var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
		{
			["app_id"] = config.AppId?.Trim(),
			["biz_content"] = bizContent,
			["charset"] = "utf-8",
			["format"] = "JSON",
			["method"] = "alipay.trade.precreate",
			["notify_url"] = notifyUrl,
			["sign_type"] = "RSA2",
			["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
			["version"] = "1.0"
		};
		parameters["sign"] = SignParameters(parameters, config.PrivateKey);
		var response = await CallGatewayAsync("alipay_trade_precreate_response", parameters);
		var code = response.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
		if (code != "10000")
		{
			var subMsg = response.TryGetProperty("sub_msg", out var subMsgElement) ? subMsgElement.GetString() : null;
			var msg = response.TryGetProperty("msg", out var msgElement) ? msgElement.GetString() : null;
			throw new InvalidOperationException($"支付宝当面付预下单失败：{code} {msg} {subMsg}".Trim());
		}

		var qrCode = response.TryGetProperty("qr_code", out var qrCodeElement) ? qrCodeElement.GetString() : null;
		if (string.IsNullOrWhiteSpace(qrCode))
		{
			throw new InvalidOperationException("支付宝当面付预下单成功但未返回二维码内容");
		}

		return new AlipayPrecreateResult { OutTradeNo = outTradeNo, QrCode = qrCode };
	}

	/// <inheritdoc />
	public async Task<AlipayTradeQueryResult> QueryTradeAsync(string outTradeNo)
	{
		var config = AppConfig.Alipay;
		var bizContent = JsonSerializer.Serialize(new Dictionary<string, string>
		{
			["out_trade_no"] = outTradeNo
		});
		var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
		{
			["app_id"] = config.AppId?.Trim(),
			["biz_content"] = bizContent,
			["charset"] = "utf-8",
			["format"] = "JSON",
			["method"] = "alipay.trade.query",
			["sign_type"] = "RSA2",
			["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
			["version"] = "1.0"
		};
		parameters["sign"] = SignParameters(parameters, config.PrivateKey);
		var response = await CallGatewayAsync("alipay_trade_query_response", parameters);
		var code = response.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
		if (code == "40004")
		{
			// 业务处理失败（常见为交易不存在），按“查无此单”返回，调用方据此继续等待
			return new AlipayTradeQueryResult { TradeStatus = null, TradeNo = null, TotalAmount = null };
		}

		if (code != "10000")
		{
			var subMsg = response.TryGetProperty("sub_msg", out var subMsgElement) ? subMsgElement.GetString() : null;
			var msg = response.TryGetProperty("msg", out var msgElement) ? msgElement.GetString() : null;
			throw new InvalidOperationException($"支付宝交易查询失败：{code} {msg} {subMsg}".Trim());
		}

		return new AlipayTradeQueryResult
		{
			TradeStatus = response.TryGetProperty("trade_status", out var statusElement) ? statusElement.GetString() : null,
			TradeNo = response.TryGetProperty("trade_no", out var tradeNoElement) ? tradeNoElement.GetString() : null,
			TotalAmount = response.TryGetProperty("total_amount", out var amountElement) ? amountElement.GetString() : null
		};
	}

	/// <inheritdoc />
	public async Task<AlipayTradeStatusSyncNotify> VerifyNotifyAsync(IDictionary<string, string> parameters)
	{
		return await notifyClient.ExecuteAsync<AlipayTradeStatusSyncNotify>(parameters, CreateOptions());
	}

	/// <summary>
	/// 直调支付宝网关：按 RSA2 签名后的参数 POST 表单，解析响应节点并验签后返回业务节点。
	/// 响应验签失败或网关返回 error_response 时抛异常，调用方按失败处理（不落库、不改订单）。
	/// </summary>
	/// <param name="responseNode">业务响应节点名，例如 alipay_trade_precreate_response</param>
	/// <param name="parameters">已签名的全部请求参数</param>
	/// <returns>业务响应节点（已验签）</returns>
	private async Task<JsonElement> CallGatewayAsync(string responseNode, SortedDictionary<string, string> parameters)
	{
		var config = AppConfig.Alipay;
		var gateway = string.IsNullOrWhiteSpace(config.Gateway) ? "https://openapi.alipay.com/gateway.do" : config.Gateway.Trim();
		using var httpClient = httpClientFactory.CreateClient();
		httpClient.Timeout = TimeSpan.FromSeconds(30);
		using var content = new FormUrlEncodedContent(parameters);
		using var httpResponse = await httpClient.PostAsync(gateway, content);
		httpResponse.EnsureSuccessStatusCode();
		var json = await httpResponse.Content.ReadAsStringAsync();
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;
		if (root.TryGetProperty("error_response", out var error))
		{
			var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
			var subMsg = error.TryGetProperty("sub_msg", out var subMsgElement) ? subMsgElement.GetString() : null;
			var msg = error.TryGetProperty("msg", out var msgElement) ? msgElement.GetString() : null;
			throw new InvalidOperationException($"支付宝网关返回错误：{code} {msg} {subMsg}".Trim());
		}

		if (!root.TryGetProperty(responseNode, out var response))
		{
			throw new InvalidOperationException($"支付宝网关响应缺少节点 {responseNode}");
		}

		// 验签：sign 是对业务响应节点原始 JSON 的 RSA2 签名，验签失败说明响应不可信
		if (root.TryGetProperty("sign", out var signElement) && !string.IsNullOrWhiteSpace(signElement.GetString()))
		{
			var signValid = VerifyResponseSign(response.GetRawText(), signElement.GetString(), config.AlipayPublicKey);
			if (!signValid)
			{
				throw new InvalidOperationException("支付宝网关响应验签失败");
			}
		}

		return response.Clone();
	}

	/// <summary>
	/// RSA2 签名：参数按 key 字典序拼接 key=value（&amp; 连接，不做 URL 编码），
	/// 用商户私钥（PKCS8）做 SHA256 + PKCS1 签名，返回 Base64 签名值。
	/// 与电脑网站支付表单共用同一签名口径。
	/// </summary>
	/// <param name="parameters">待签参数（不含 sign 本身）</param>
	/// <param name="privateKey">商户私钥，可带或不带 PEM 头尾</param>
	/// <returns>Base64 签名值</returns>
	private static string SignParameters(SortedDictionary<string, string> parameters, string privateKey)
	{
		var signContent = string.Join("&", parameters.Select(parameter => $"{parameter.Key}={parameter.Value}"));
		using var rsa = RSA.Create();
		rsa.ImportPkcs8PrivateKey(DecodePemBase64(privateKey), out _);
		var signature = rsa.SignData(Encoding.UTF8.GetBytes(signContent), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		return Convert.ToBase64String(signature);
	}

	/// <summary>
	/// RSA2 验签：用支付宝公钥验证网关响应签名的合法性。
	/// </summary>
	/// <param name="content">被签名的原始内容（业务响应节点的原始 JSON）</param>
	/// <param name="sign">Base64 签名值</param>
	/// <param name="alipayPublicKey">支付宝公钥，可带或不带 PEM 头尾</param>
	/// <returns>验签是否通过</returns>
	private static bool VerifyResponseSign(string content, string sign, string alipayPublicKey)
	{
		using var rsa = RSA.Create();
		rsa.ImportSubjectPublicKeyInfo(DecodePemBase64(alipayPublicKey), out _);
		return rsa.VerifyData(Encoding.UTF8.GetBytes(content), Convert.FromBase64String(sign), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
	}

	/// <summary>
	/// 把可能带 PEM 头尾（-----BEGIN PRIVATE KEY----- / -----BEGIN PUBLIC KEY-----）的密钥
	/// 还原为原始 DER 字节；不带头尾的纯 Base64 字符串直接解码。空值会抛异常，调用方按未配置处理。
	/// </summary>
	/// <param name="key">PEM 或纯 Base64 的密钥文本</param>
	/// <returns>DER 字节</returns>
	private static byte[] DecodePemBase64(string key)
	{
		var text = key.Trim();
		text = text.Replace("-----BEGIN PRIVATE KEY-----", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace("-----END PRIVATE KEY-----", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace("-----BEGIN PUBLIC KEY-----", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace("-----END PUBLIC KEY-----", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace("-----BEGIN RSA PRIVATE KEY-----", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace("-----END RSA PRIVATE KEY-----", string.Empty, StringComparison.OrdinalIgnoreCase);
		text = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
		return Convert.FromBase64String(text);
	}
}
