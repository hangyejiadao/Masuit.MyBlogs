namespace Masuit.MyBlogs.Core.Models.Command;

/// <summary>
/// 公众号配置保存命令：AppSecret 留空表示保留已保存的值，ClearSecret 为 true 表示显式清除
/// </summary>
public sealed class WeChatSettingsCommand
{
    /// <summary>
    /// 是否启用公众号同步
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 公众号 AppID
    /// </summary>
    public string AppId { get; set; }

    /// <summary>
    /// 公众号 AppSecret（留空保留原值）
    /// </summary>
    public string AppSecret { get; set; }

    /// <summary>
    /// 清除已保存的 AppSecret
    /// </summary>
    public bool ClearSecret { get; set; }

    /// <summary>
    /// 默认作者（留空用文章作者）
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// 默认封面图地址（文章正文无图时使用）
    /// </summary>
    public string DefaultCoverUrl { get; set; }

    /// <summary>
    /// 同步后是否默认直接发布
    /// </summary>
    public bool AutoPublish { get; set; }

    /// <summary>
    /// 是否开启评论
    /// </summary>
    public bool OpenComment { get; set; }

    /// <summary>
    /// 是否仅粉丝可评论
    /// </summary>
    public bool FansOnlyComment { get; set; }

    /// <summary>
    /// 文章发布/编辑保存后是否自动同步到草稿箱
    /// </summary>
    public bool AutoSync { get; set; }
}

/// <summary>
/// 文章同步到公众号命令
/// </summary>
public sealed class WeChatSyncCommand
{
    /// <summary>
    /// 文章 id
    /// </summary>
    public int PostId { get; set; }

    /// <summary>
    /// 是否在同步到草稿后直接发布；不传则用公众号配置里的「同步后直接发布」默认值
    /// </summary>
    public bool? Publish { get; set; }
}

/// <summary>
/// 批量查询同步状态命令（文章列表页用）
/// </summary>
public sealed class WeChatSyncStatusesCommand
{
    /// <summary>
    /// 文章 id 集合
    /// </summary>
    public int[] PostIds { get; set; } = [];
}
