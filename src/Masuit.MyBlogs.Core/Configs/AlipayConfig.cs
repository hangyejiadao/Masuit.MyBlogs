namespace Masuit.MyBlogs.Core.Configs;

/// <summary>
/// 支付宝支付配置，对应 appsettings.json 的 Alipay 节。
/// 凭据来自支付宝开放平台（https://open.alipay.com）创建的应用：
/// AppId、应用私钥（商户私钥，PKCS8 格式）、支付宝公钥（用应用公钥换得的那串，不是应用公钥本身）。
/// 凭据只能填在服务器的 appsettings.json 里，严禁提交真实密钥到仓库、严禁在聊天中发送。
/// </summary>
public class AlipayConfig
{
	/// <summary>
	/// 是否启用支付宝支付；关闭时商店只建单不发起支付，支付页提示联系管理员
	/// </summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// 支付宝开放平台应用的 AppId
	/// </summary>
	public string AppId { get; set; }

	/// <summary>
	/// 商户私钥（应用私钥，PKCS8 格式，可带或不带 PEM 头尾）
	/// </summary>
	public string PrivateKey { get; set; }

	/// <summary>
	/// 支付宝公钥（在开放平台「应用公钥」上传后由支付宝生成的公钥）
	/// </summary>
	public string AlipayPublicKey { get; set; }

	/// <summary>
	/// 支付宝网关地址，正式环境为 https://openapi.alipay.com/gateway.do，沙箱为 https://openapi-sandbox.dl.alipaydev.com/gateway.do
	/// </summary>
	public string Gateway { get; set; } = "https://openapi.alipay.com/gateway.do";

	/// <summary>
	/// 支付异步通知地址（服务器 POST 回调），留空则按站点域名自动拼 /Store/AlipayNotify
	/// </summary>
	public string NotifyUrl { get; set; }

	/// <summary>
	/// 支付同步跳转地址（用户浏览器跳回），留空则按站点域名自动拼 /Store/Result（支付宝会附带 out_trade_no 等参数）
	/// </summary>
	public string ReturnUrl { get; set; }

	/// <summary>
	/// 支付通道是否可用：启用开关打开且 AppId、私钥、公钥都已填写
	/// </summary>
	public bool Configured => Enabled && !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(PrivateKey) && !string.IsNullOrWhiteSpace(AlipayPublicKey);
}
