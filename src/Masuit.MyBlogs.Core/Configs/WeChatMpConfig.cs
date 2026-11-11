namespace Masuit.MyBlogs.Core.Configs;

/// <summary>
/// 微信公众号文章推送配置，对应 appsettings.json 的 WeChatMp 节。
/// 本功能按「个人订阅号」设计，不使用服务号才有的能力（模板消息、客服消息群发等）。
/// 个人订阅号能调通的接口：拿 access_token、图片上传（uploadimg）、永久素材（add_material）、
/// 新建草稿（draft/add)。自 2025 年 7 月起微信回收了个人主体账号的发布接口（freepublish/submit）
/// 权限，调发布会报 48001，所以默认只建草稿，最终发表请在公众号后台或「公众号助手」App 手动完成；
/// 手动发表/群发每天只有 1 次机会，一次最多 8 篇，文章出现在粉丝的订阅号消息列表里（折叠展示），
/// 不会像服务号那样以消息通知形式推送。
/// 凭据来自微信公众平台（https://mp.weixin.qq.com）：AppID 与 AppSecret 在「开发 - 基本配置」里查看；
/// 调用接口前还需把服务器出口 IP 加到「开发 - 基本配置 - IP 白名单」，否则拿 access_token 会报 40164。
/// 凭据只能填在服务器的 appsettings.json 里，严禁提交真实密钥到仓库、严禁在聊天中发送。
/// </summary>
public class WeChatMpConfig
{
	/// <summary>
	/// 是否启用微信公众号文章推送；关闭时推送接口直接拒绝，不调用微信 API
	/// </summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// 微信公众号的 AppID（开发 - 基本配置）
	/// </summary>
	public string AppId { get; set; }

	/// <summary>
	/// 微信公众号的 AppSecret（开发 - 基本配置），只在服务器配置里填写
	/// </summary>
	public string AppSecret { get; set; }

	/// <summary>
	/// 建草稿后是否自动提交发布（调 freepublish/submit）。
	/// 默认 false：只把文章写进公众号草稿箱，由管理员在公众号后台或「公众号助手」App 检查后手动发表/群发。
	/// 设为 true 时会尝试调发布接口，但个人订阅号自 2025 年 7 月起已被微信回收该接口权限，会报 48001，
	/// 此时草稿仍保留在草稿箱、不会丢失，只需手动发表；只有企业主体已认证的账号调发布接口才能成功。
	/// 注意「发表」和「群发」是两回事：发表是让文章上线并生成永久链接，不推送给粉丝、不占用每天 1 次的
	/// 群发次数；群发才是把文章推送到粉丝的订阅号消息列表（订阅号每天 1 次、一次最多 8 篇），需在后台手动操作。
	/// </summary>
	public bool AutoPublish { get; set; }

	/// <summary>
	/// 默认封面永久素材 media_id（可选）。
	/// 文章里没有图片、又没填这里时，推送会失败并提示需要封面图；
	/// 填了这里就用这个封面。封面素材需先在公众号后台「素材管理 - 图片」上传，
	/// 再用 media/get 或素材列表拿到 media_id。
	/// </summary>
	public string DefaultThumbMediaId { get; set; }

	/// <summary>
	/// 微信 API 根地址，一般不用改；联调或走代理时可覆盖
	/// </summary>
	public string ApiBaseUrl { get; set; } = "https://api.weixin.qq.com";

	/// <summary>
	/// 推送通道是否可用：启用开关打开且 AppID、AppSecret 都已填写
	/// </summary>
	public bool Configured => Enabled && !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppSecret);
}
