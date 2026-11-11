using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.MyBlogs.Core.Infrastructure.Services;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 微信公众号文章推送（管理员后台用）：把博客文章同步到公众号草稿箱，可选自动发表。
/// 按「个人订阅号」设计：默认只建草稿，最终发表/群发在公众号后台或「公众号助手」App 手动完成。
/// 个人订阅号自 2025 年 7 月起已被微信回收发布接口权限，自动发表会报 48001，草稿不受影响。
/// </summary>
public sealed class WeChatMpController : BaseController
{
	public IWeChatMpService WeChatMpService { get; set; }

	public IPostService PostService { get; set; }

	/// <summary>
	/// 推送通道状态：是否启用、是否已配好凭据、是否开启自动发表。
	/// 前端据此决定「同步到公众号」按钮是否可用、是否显示「同时发表」选项。
	/// </summary>
	/// <returns></returns>
	[HttpGet, MyAuthorize]
	public ActionResult GetStatus()
	{
		var config = AppConfig.WeChatMp;
		return ResultData(new
		{
			enabled = config.Enabled,
			configured = config.Configured,
			autoPublish = config.AutoPublish,
			hint = config.Configured
				? "推送可用：默认只创建草稿，发表/群发请在公众号后台或「公众号助手」App 手动完成（订阅号每天群发 1 次、一次最多 8 篇）。"
				: "推送未配置：请在服务器 appsettings.json 的 WeChatMp 节填写 AppId、AppSecret 并把 Enabled 设为 true。"
		}, true, "ok");
	}

	/// <summary>
	/// 把一篇文章推送到公众号：建草稿，可选自动发表。
	/// publish=true 且配置 AutoPublish=true 时才调发布接口；个人订阅号调发布会报 48001，
	/// 此时草稿已保留，返回的 Message 里会说明需手动发表。
	/// </summary>
	/// <param name="id">博客文章 Id</param>
	/// <param name="publish">是否同时尝试自动发表</param>
	/// <returns></returns>
	[HttpPost, MyAuthorize]
	public async Task<ActionResult> Push(int id, bool publish = false)
	{
		var post = await PostService.GetByIdAsync(id);
		if (post == null)
		{
			return ResultData(null, false, "文章不存在。");
		}

		var articleUrl = Url.Action("Details", "Post", new { id = post.Id }, Request.Scheme);
		var result = await WeChatMpService.PushPostAsync(post, articleUrl, publish);
		return ResultData(result, result.Success, result.Message);
	}

	/// <summary>
	/// 取一篇文章最近一次的推送记录（状态、草稿 media_id、发表链接、失败原因）。
	/// </summary>
	/// <param name="id">博客文章 Id</param>
	/// <returns></returns>
	[HttpGet, MyAuthorize]
	public async Task<ActionResult> GetRecord(int id)
	{
		var record = await WeChatMpService.GetLatestRecordAsync(id);
		return ResultData(record == null ? null : ToDto(record), true, record == null ? "这篇文章还没有推送记录。" : "ok");
	}

	/// <summary>
	/// 对一条「发表中」的推送记录查一次发表状态（freepublish/get），更新记录并返回最新状态。
	/// 记录不是发表中时直接返回原记录。
	/// </summary>
	/// <param name="id">推送记录 Id</param>
	/// <returns></returns>
	[HttpPost, MyAuthorize]
	public async Task<ActionResult> RefreshStatus(int id)
	{
		try
		{
			var record = await WeChatMpService.RefreshPublishStatusAsync(id);
			if (record == null)
			{
				return ResultData(null, false, "推送记录不存在。");
			}

			return ResultData(ToDto(record), true, "ok");
		}
		catch (WeChatMpException ex)
		{
			return ResultData(null, false, ex.Message);
		}
	}

	/// <summary>推送记录只给前端需要的字段，避免把实体内部字段序列化出去</summary>
	private static object ToDto(WeChatMpPushRecord record) => new
	{
		id = record.Id,
		postId = record.PostId,
		title = record.Title,
		pushStatus = (int)record.PushStatus,
		pushStatusText = record.PushStatus.GetDisplay(),
		mediaId = record.MediaId,
		publishId = record.PublishId,
		articleUrl = record.ArticleUrl,
		publishRequested = record.PublishRequested,
		errorMessage = record.ErrorMessage,
		pushedAt = record.PushedAt,
		updatedAt = record.UpdatedAt
	};
}
