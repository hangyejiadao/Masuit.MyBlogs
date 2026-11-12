using Masuit.Tools.Logging;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 微信公众号：配置读写（AppSecret 只写不回显）、测试连接、文章同步到草稿箱/发布、同步状态查询。
/// 配置存系统设置表（键前缀 WeChat），通用系统设置接口已排除该前缀，配置只经本控制器的专用接口维护。
/// </summary>
public sealed class WeChatController : AdminController
{
    /// <summary>
    /// 系统设置服务
    /// </summary>
    public ISystemSettingService SystemSettingService { get; set; }

    /// <summary>
    /// 文章服务
    /// </summary>
    public IPostService PostService { get; set; }

    /// <summary>
    /// 微信公众号 API 客户端
    /// </summary>
    public WeChatMpClient WeChatClient { get; set; }

    private static WeChatConfiguration Config => WeChatConfiguration.Read(name => CommonHelper.SystemSettings[name]);

    /// <summary>
    /// 获取公众号配置（不含 AppSecret 明文，仅返回是否已配置）
    /// </summary>
    /// <returns>公众号配置</returns>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult GetSettings()
    {
        var config = Config;
        return ResultData(new
        {
            config.Enabled,
            config.AppId,
            config.HasSecret,
            config.Author,
            config.DefaultCoverUrl,
            config.AutoPublish,
            config.OpenComment,
            config.FansOnlyComment,
            config.AutoSync
        });
    }

    /// <summary>
    /// 保存公众号配置：AppSecret 留空保留原值，ClearSecret 为 true 时清除
    /// </summary>
    /// <param name="settings">公众号配置命令</param>
    /// <returns>保存结果</returns>
    [HttpPost]
    public async Task<ActionResult> SaveSettings([FromBody] WeChatSettingsCommand settings)
    {
        Response.Headers.CacheControl = "no-store";
        var error = WeChatConfiguration.Validate(settings, Config.HasSecret);
        if (error is not null) return ResultData(null, false, error);
        var entries = new List<SystemSetting>
        {
            new() { Name = "WeChatEnabled", Value = settings.Enabled.ToString().ToLowerInvariant() },
            new() { Name = "WeChatAppId", Value = settings.AppId?.Trim() ?? "" },
            new() { Name = "WeChatAuthor", Value = settings.Author?.Trim() ?? "" },
            new() { Name = "WeChatDefaultCoverUrl", Value = settings.DefaultCoverUrl?.Trim() ?? "" },
            new() { Name = "WeChatAutoPublish", Value = settings.AutoPublish.ToString().ToLowerInvariant() },
            new() { Name = "WeChatOpenComment", Value = settings.OpenComment.ToString().ToLowerInvariant() },
            new() { Name = "WeChatFansOnlyComment", Value = settings.FansOnlyComment.ToString().ToLowerInvariant() },
            new() { Name = "WeChatAutoSync", Value = settings.AutoSync.ToString().ToLowerInvariant() }
        };
        if (settings.ClearSecret || !string.IsNullOrWhiteSpace(settings.AppSecret))
            entries.Add(new SystemSetting { Name = "WeChatAppSecret", Value = settings.ClearSecret ? "" : settings.AppSecret.Trim() });
        await SystemSettingService.AddOrUpdateSavedAsync(item => item.Name, entries);
        foreach (var entry in entries) CommonHelper.SystemSettings[entry.Name] = entry.Value;
        return ResultData(null, true, "公众号配置保存成功！");
    }

    /// <summary>
    /// 测试连接：用已保存的配置强制刷新 access_token，并查询草稿箱总数验证草稿接口权限
    /// </summary>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>令牌与草稿权限的测试结果</returns>
    [HttpPost]
    public async Task<ActionResult> TestConnection(CancellationToken cancellationToken)
    {
        var config = Config;
        if (!config.HasCredentials)
        {
            return ResultData(null, false, "请先填写并保存 AppID 与 AppSecret，再测试连接");
        }

        try
        {
            await WeChatClient.GetAccessTokenAsync(config, true, cancellationToken);
        }
        catch (WeChatApiException e)
        {
            return ResultData(null, false, "获取 access_token 失败：" + e.Message);
        }

        try
        {
            var count = await WeChatClient.GetDraftCountAsync(config, cancellationToken);
            return ResultData(new { draftCount = count }, true, $"连接成功！access_token 获取正常，草稿箱现有 {count} 篇草稿");
        }
        catch (WeChatApiException e)
        {
            return ResultData(null, false, "access_token 获取成功，但草稿箱接口调用失败：" + e.Message);
        }
    }

    /// <summary>
    /// 把一篇文章同步到公众号（已同步过的更新原草稿）；可选择同步后直接发布
    /// </summary>
    /// <param name="cmd">同步命令（文章 id、是否发布）</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>同步结果（草稿 id、是否已发布、警告）</returns>
    [HttpPost]
    public async Task<ActionResult> SyncPost([FromBody] WeChatSyncCommand cmd, CancellationToken cancellationToken)
    {
        var config = Config;
        var post = await PostService.GetByIdAsync(cmd.PostId);
        if (post is null)
        {
            return ResultData(null, false, "文章不存在");
        }

        if (post.Status != Status.Published)
        {
            return ResultData(null, false, "只有已发布的文章才能同步到公众号，请先发布文章");
        }

        var publish = cmd.Publish ?? config.AutoPublish;
        var record = WeChatSyncStore.Get(RedisHelper, post.Id);
        try
        {
            var result = await WeChatClient.SyncPostAsync(post, config, PostUrl(post.Id), publish, record?.Status == "failed" ? null : record?.MediaId, cancellationToken);
            WeChatSyncStore.Save(RedisHelper, new WeChatSyncRecord
            {
                PostId = post.Id,
                MediaId = result.MediaId,
                PublishId = result.PublishId,
                Status = result.Published ? "published" : "draft",
                SyncTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                PostModifyDate = post.ModifyDate.ToString("yyyy-MM-dd HH:mm:ss"),
                Message = result.Warnings.Count > 0 ? string.Join("；", result.Warnings) : ""
            });
            var message = result.Published ? "已同步并提交发布！发布结果请在公众号后台「发表记录」中确认" : "已同步到公众号草稿箱！请到公众号后台确认排版后发表";
            if (result.Warnings.Count > 0)
            {
                message += "（注意：" + string.Join("；", result.Warnings) + "）";
            }

            return ResultData(new { result.MediaId, result.PublishId, result.Published, result.Warnings }, true, message);
        }
        catch (WeChatApiException e)
        {
            LogManager.Error(e);
            WeChatSyncStore.Save(RedisHelper, new WeChatSyncRecord
            {
                PostId = post.Id,
                MediaId = record?.MediaId,
                PublishId = record?.PublishId,
                Status = "failed",
                SyncTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                PostModifyDate = post.ModifyDate.ToString("yyyy-MM-dd HH:mm:ss"),
                Message = e.Message
            });
            return ResultData(null, false, "同步失败：" + e.Message);
        }
        catch (Exception e)
        {
            LogManager.Error(e);
            return ResultData(null, false, "同步失败：服务器异常，" + e.Message);
        }
    }

    /// <summary>
    /// 批量查询文章的公众号同步状态（文章列表页用）
    /// </summary>
    /// <param name="cmd">文章 id 集合</param>
    /// <returns>以文章 id 为键的同步状态字典；附带 Outdated 标记表示同步后文章又被修改过</returns>
    [HttpPost]
    public ActionResult SyncStatuses([FromBody] WeChatSyncStatusesCommand cmd)
    {
        var result = new Dictionary<int, object>();
        if (cmd.PostIds is { Length: > 0 })
        {
            var posts = PostService.GetQueryNoTracking(p => cmd.PostIds.Contains(p.Id)).Select(p => new { p.Id, p.ModifyDate }).ToDictionary(p => p.Id, p => p.ModifyDate);
            foreach (var postId in cmd.PostIds.Distinct().Take(200))
            {
                var record = WeChatSyncStore.Get(RedisHelper, postId);
                if (record is null)
                {
                    continue;
                }

                var outdated = posts.TryGetValue(postId, out var modifyDate) && record.PostModifyDate != modifyDate.ToString("yyyy-MM-dd HH:mm:ss");
                result[postId] = new
                {
                    record.Status,
                    record.SyncTime,
                    record.Message,
                    Outdated = outdated
                };
            }
        }

        return ResultData(result);
    }

    /// <summary>
    /// 生成文章原文链接（与站内其他位置一致的 Url.Action 方式），用作草稿的「阅读原文」地址
    /// </summary>
    /// <param name="postId">文章 id</param>
    /// <returns>文章绝对链接</returns>
    private string PostUrl(int postId) => Url.Action("Details", "Post", new { id = postId }, Request.Scheme);
}
