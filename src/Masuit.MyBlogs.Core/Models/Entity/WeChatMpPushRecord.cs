namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 微信公众号文章推送记录：一篇文章每次手动推送产生一条记录，
/// 保存微信返回的草稿 media_id、发表 publish_id、最终文章链接和失败原因。
/// 重新推送同一篇文章会新增一条记录，旧记录保留供追溯。
/// </summary>
[Table("WeChatMpPushRecord")]
public class WeChatMpPushRecord : BaseEntity
{
	/// <summary>
	/// 博客文章 Id
	/// </summary>
	public int PostId { get; set; }

	/// <summary>
	/// 推送时文章标题的快照
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// 推送状态：草稿已创建 / 发表中 / 已发表 / 推送失败
	/// </summary>
	public WeChatMpPushStatus PushStatus { get; set; }

	/// <summary>
	/// 微信草稿 media_id（draft/add 返回），草稿箱里定位这篇草稿用
	/// </summary>
	public string MediaId { get; set; }

	/// <summary>
	/// 发表任务 publish_id（freepublish/submit 返回），查发表状态用
	/// </summary>
	public string PublishId { get; set; }

	/// <summary>
	/// 发表成功后微信返回的 msg_data_id
	/// </summary>
	public long? MsgDataId { get; set; }

	/// <summary>
	/// 发表成功后微信返回的文章链接
	/// </summary>
	public string ArticleUrl { get; set; }

	/// <summary>
	/// 本次推送是否要求自动发表（取推送时 WeChatMp.AutoPublish 的值）
	/// </summary>
	public bool PublishRequested { get; set; }

	/// <summary>
	/// 微信返回的错误码（成功为 0）
	/// </summary>
	public int ErrorCode { get; set; }

	/// <summary>
	/// 失败原因或微信返回的 errmsg；正文图片个别上传失败时也会记在这里（不阻断推送）
	/// </summary>
	public string ErrorMessage { get; set; }

	/// <summary>
	/// 推送发起时间
	/// </summary>
	public DateTime PushedAt { get; set; }

	/// <summary>
	/// 记录最后更新时间（发表状态轮询确认后更新）
	/// </summary>
	public DateTime UpdatedAt { get; set; }
}
