namespace Masuit.MyBlogs.Core.Configs;

/// <summary>
/// 应用程序配置
/// </summary>
public class AppConfig
{
    /// <summary>
    /// 数据库连接字符串
    /// </summary>
    public static string ConnString { get; set; }

    /// <summary>
    /// Redis连接字符串
    /// </summary>
    public static string Redis { get; set; }

    /// <summary>
    /// gitlab图床配置
    /// </summary>
    public static List<GitlabConfig> GitlabConfigs { get; set; } = [];

    /// <summary>
    /// 真实客户端IP转发标头
    /// </summary>
    public static string TrueClientIPHeader { get; set; }

    /// <summary>
    /// 是否允许IP直接访问
    /// </summary>
    public static bool EnableIPDirect { get; set; }

    /// <summary>
    /// 第三方单点登录配置（GitHub / Google）
    /// </summary>
    public static OAuthConfig OAuth { get; set; } = new();

    /// <summary>
    /// 支付宝支付配置（软件购买商店收款用）
    /// </summary>
    public static AlipayConfig Alipay { get; set; } = new();
}