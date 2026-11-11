namespace Masuit.MyBlogs.Core.Models.DTO;

/// <summary>
/// 微信公众号文章推送结果：返回给控制器/前端展示用。
/// 注意「草稿成功、发表失败」是一种正常结果（个人订阅号调发布接口会报 48001）：
/// 此时 Success 为 true、Status 为 DraftCreated，Message 里会说明需手动发表，草稿不会丢失。
/// </summary>
public class WeChatMpPushResult
{
	/// <summary>
	/// 推送主流程是否成功：草稿已创建即为 true；只有建草稿本身失败时才为 false
	/// </summary>
	public bool Success { get; set; }

	/// <summary>
	/// 给管理员看的结果说明（中文），包含发表失败时的手动发表提示
	/// </summary>
	public string Message { get; set; }

	/// <summary>
	/// 推送记录的状态
	/// </summary>
	public WeChatMpPushStatus Status { get; set; }

	/// <summary>
	/// 本次推送产生的记录 Id（失败时为 0）
	/// </summary>
	public int RecordId { get; set; }

	/// <summary>
	/// 微信草稿 media_id，草稿箱里定位这篇草稿用
	/// </summary>
	public string MediaId { get; set; }

	/// <summary>
	/// 发表任务 publish_id（仅自动发表提交成功时有值）
	/// </summary>
	public string PublishId { get; set; }

	/// <summary>
	/// 发表成功后微信返回的文章链接（需发表状态轮询确认后才有值）
	/// </summary>
	public string ArticleUrl { get; set; }

	/// <summary>
	/// 本次是否尝试了自动发表
	/// </summary>
	public bool PublishAttempted { get; set; }

	/// <summary>
	/// 自动发表失败时的原因（成功或未尝试时为空）
	/// </summary>
	public string PublishError { get; set; }

	/// <summary>
	/// 不阻断推送的警告，如个别正文图片上传失败（图片会保留原链接，微信编辑器里可能不显示）
	/// </summary>
	public List<string> Warnings { get; set; } = new();
}
