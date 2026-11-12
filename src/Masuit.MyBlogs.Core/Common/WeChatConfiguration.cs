namespace Masuit.MyBlogs.Core.Common;

/// <summary>
/// 微信公众号配置：存系统设置表，键统一以 WeChat 为前缀（与 Vedio 配置同构，
/// 通用系统设置接口会排除该前缀，AppSecret 只经公众号配置页的专用接口读写、永不回显）。
/// </summary>
public sealed class WeChatConfiguration
{
    /// <summary>
    /// 公众号配置在系统设置中的键名前缀
    /// </summary>
    public const string Prefix = "WeChat";

    /// <summary>
    /// 是否启用公众号同步
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// 公众号 AppID
    /// </summary>
    public string AppId { get; init; }

    /// <summary>
    /// 公众号 AppSecret（仅服务端使用，不下发前端）
    /// </summary>
    public string AppSecret { get; init; }

    /// <summary>
    /// 默认作者（留空用文章作者；微信侧作者最长 8 个字，同步时自动截断）
    /// </summary>
    public string Author { get; init; }

    /// <summary>
    /// 默认封面图地址（文章正文无图时用作封面；可填本站相对路径或外链）
    /// </summary>
    public string DefaultCoverUrl { get; init; }

    /// <summary>
    /// 同步后是否默认直接发布（单次同步时仍可覆盖）
    /// </summary>
    public bool AutoPublish { get; init; }

    /// <summary>
    /// 是否开启评论
    /// </summary>
    public bool OpenComment { get; init; }

    /// <summary>
    /// 是否仅粉丝可评论
    /// </summary>
    public bool FansOnlyComment { get; init; }

    /// <summary>
    /// 文章在博客发布/编辑保存后是否自动同步到公众号草稿箱（Hangfire 后台执行）
    /// </summary>
    public bool AutoSync { get; init; }

    /// <summary>
    /// AppSecret 是否已配置
    /// </summary>
    public bool HasSecret => !string.IsNullOrWhiteSpace(AppSecret);

    /// <summary>
    /// AppID 与 AppSecret 是否都已配置（能调微信接口的最低要求）
    /// </summary>
    public bool HasCredentials => !string.IsNullOrWhiteSpace(AppId) && HasSecret;

    /// <summary>
    /// 从系统设置读取公众号配置
    /// </summary>
    /// <param name="read">按完整键名读取设置值的委托</param>
    /// <returns>公众号配置</returns>
    public static WeChatConfiguration Read(Func<string, string> read)
    {
        bool Flag(string name, bool fallback = false) => bool.TryParse(read(Prefix + name), out var value) ? value : fallback;
        return new WeChatConfiguration
        {
            Enabled = Flag("Enabled"),
            AppId = read(Prefix + "AppId")?.Trim(),
            AppSecret = read(Prefix + "AppSecret")?.Trim(),
            Author = read(Prefix + "Author")?.Trim(),
            DefaultCoverUrl = read(Prefix + "DefaultCoverUrl")?.Trim(),
            AutoPublish = Flag("AutoPublish"),
            OpenComment = Flag("OpenComment"),
            FansOnlyComment = Flag("FansOnlyComment"),
            AutoSync = Flag("AutoSync")
        };
    }

    /// <summary>
    /// 校验公众号配置命令，返回第一条错误（无错误返回 null）
    /// </summary>
    /// <param name="settings">待保存的配置</param>
    /// <param name="hasStoredSecret">库里是否已有 AppSecret（本次留空保留旧值时据此判断完整性）</param>
    /// <returns>错误信息或 null</returns>
    public static string Validate(WeChatSettingsCommand settings, bool hasStoredSecret)
    {
        if (settings is null) return "配置不能为空。";
        var appId = settings.AppId?.Trim();
        if (settings.Enabled)
        {
            if (string.IsNullOrEmpty(appId)) return "启用公众号同步前请先填写 AppID。";
            var hasSecret = !string.IsNullOrWhiteSpace(settings.AppSecret) || (hasStoredSecret && !settings.ClearSecret);
            if (!hasSecret) return "启用公众号同步前请先填写 AppSecret。";
        }

        if (!string.IsNullOrEmpty(appId) && (appId.Length > 64 || appId.Any(char.IsWhiteSpace))) return "AppID 格式无效。";
        var secret = settings.AppSecret?.Trim();
        if (!string.IsNullOrEmpty(secret) && (secret.Length > 128 || secret.Any(char.IsWhiteSpace))) return "AppSecret 格式无效。";
        if (settings.ClearSecret && !string.IsNullOrEmpty(secret)) return "清除 AppSecret 时请先清空 AppSecret 输入框。";
        if (settings.Author?.Trim().Length > 8) return "默认作者最长 8 个字符（微信侧限制）。";
        var cover = settings.DefaultCoverUrl?.Trim();
        if (!string.IsNullOrEmpty(cover) && cover.Length > 1024) return "默认封面图地址过长。";
        return null;
    }
}
