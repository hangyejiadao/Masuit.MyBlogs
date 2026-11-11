using System.ComponentModel;

namespace Masuit.MyBlogs.Core.Models.Enum;

/// <summary>
/// 微信公众号文章推送状态（WeChatMpPushRecord.PushStatus）
/// </summary>
public enum WeChatMpPushStatus
{
	/// <summary>
	/// 草稿已创建：文章已写进公众号草稿箱，等管理员在公众号后台手动发表
	/// </summary>
	[Description("草稿已创建")]
	DraftCreated,

	/// <summary>
	/// 发表中：已调用 freepublish/submit 提交发布，微信异步审核中，等 freepublish/get 确认
	/// </summary>
	[Description("发表中")]
	Publishing,

	/// <summary>
	/// 已发表：freepublish/get 返回 publish_status=0，文章已上线
	/// </summary>
	[Description("已发表")]
	Published,

	/// <summary>
	/// 推送失败：建草稿或提交发布时微信返回错误，原因见记录的 ErrorMessage
	/// </summary>
	[Description("推送失败")]
	Failed
}
