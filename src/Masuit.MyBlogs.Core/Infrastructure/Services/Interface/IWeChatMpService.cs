namespace Masuit.MyBlogs.Core.Infrastructure.Services.Interface;

/// <summary>
/// 微信公众号文章推送服务：把博客文章同步到公众号草稿箱（可选自动发表）。
/// 按「个人订阅号」设计：只用 access_token、图片上传、永久素材、草稿、发布这几个接口，
/// 不用服务号才有的模板消息、客服消息群发等能力。
/// 个人订阅号自 2025 年 7 月起已被微信回收发布接口（freepublish/submit）权限，
/// 调发布会报 48001，此时草稿仍保留，需在公众号后台或「公众号助手」App 手动发表/群发。
/// </summary>
public interface IWeChatMpService
{
	/// <summary>
	/// 推送通道是否可用：启用开关打开且 AppID、AppSecret 都已填写
	/// </summary>
	bool Configured { get; }

	/// <summary>
	/// 拿 access_token（Redis 缓存约 100 分钟，过期自动刷新；40001/42001 时清缓存重试一次）
	/// </summary>
	Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// 把一篇已发布博客文章推送到公众号：转微信图文 HTML、上传正文图片与封面、建草稿，
	/// publish=true 且配置 AutoPublish=true 时再调发布接口提交发表。
	/// 草稿创建成功即返回 Success=true；发表失败不影响草稿，原因写在结果的 PublishError 里。
	/// </summary>
	/// <param name="post">博客文章（须已发布）</param>
	/// <param name="articleUrl">博客原文链接，写进图文的 content_source_url</param>
	/// <param name="publish">是否同时尝试自动发表（最终是否发表还受配置 AutoPublish 控制）</param>
	Task<WeChatMpPushResult> PushPostAsync(Post post, string articleUrl, bool publish, CancellationToken cancellationToken = default);

	/// <summary>
	/// 取一篇文章最近一次的推送记录，没有则返回 null
	/// </summary>
	Task<WeChatMpPushRecord> GetLatestRecordAsync(int postId, CancellationToken cancellationToken = default);

	/// <summary>
	/// 对「发表中」的记录调 freepublish/get 查一次发表状态，更新记录并返回；
	/// 记录不是发表中、或没有 publish_id 时直接返回原记录
	/// </summary>
	Task<WeChatMpPushRecord> RefreshPublishStatusAsync(int recordId, CancellationToken cancellationToken = default);
}
