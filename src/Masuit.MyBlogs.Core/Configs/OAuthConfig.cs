namespace Masuit.MyBlogs.Core.Configs;

/// <summary>
/// 第三方单点登录配置，对应 appsettings.json 的 OAuth 节
/// </summary>
public class OAuthConfig
{
	/// <summary>
	/// GitHub 登录配置，回调地址为 https://站点域名/signin-github
	/// </summary>
	public OAuthProviderConfig GitHub { get; set; } = new();

	/// <summary>
	/// Google 登录配置，回调地址为 https://站点域名/signin-google
	/// </summary>
	public OAuthProviderConfig Google { get; set; } = new();
}

/// <summary>
/// 单个第三方平台的 OAuth 应用凭据
/// </summary>
public class OAuthProviderConfig
{
	/// <summary>
	/// OAuth 应用的 ClientId
	/// </summary>
	public string ClientId { get; set; }

	/// <summary>
	/// OAuth 应用的 ClientSecret
	/// </summary>
	public string ClientSecret { get; set; }

	/// <summary>
	/// ClientId 和 ClientSecret 都已填写时才启用该平台登录
	/// </summary>
	public bool Enabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
