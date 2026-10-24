using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Masuit.MyBlogs.Core.Extensions;

/// <summary>
/// 第三方单点登录的认证服务注册扩展
/// </summary>
public static class ExternalLoginExtensions
{
	/// <summary>
	/// 注册承载第三方身份的临时 Cookie 方案，并按配置启用 GitHub / Google 登录。
	/// 本站的登录态仍然是 Session 中的用户信息，这里的认证中间件只用于第三方回调阶段换取外部身份，
	/// 因此不设置任何默认方案，也不会影响原有的账号密码登录。
	/// </summary>
	/// <param name="services">服务集合</param>
	/// <param name="configuration">配置</param>
	/// <returns>服务集合</returns>
	public static IServiceCollection AddExternalLoginAuthentication(this IServiceCollection services, IConfiguration configuration)
	{
		var oauth = new OAuthConfig();
		configuration.Bind("OAuth", oauth);
		var builder = services.AddAuthentication().AddCookie(ExternalLoginController.CookieScheme, options =>
		{
			options.Cookie.Name = ".Masuit.ExternalLogin";
			options.Cookie.SameSite = SameSiteMode.Lax;
			options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
			options.SlidingExpiration = false;
		});
		if (oauth.GitHub.Enabled)
		{
			builder.AddGitHub(ExternalLoginController.GitHubProvider, options =>
			{
				options.ClientId = oauth.GitHub.ClientId;
				options.ClientSecret = oauth.GitHub.ClientSecret;
				options.SignInScheme = ExternalLoginController.CookieScheme;
				options.Scope.Add("user:email");
			});
		}

		if (oauth.Google.Enabled)
		{
			builder.AddGoogle(ExternalLoginController.GoogleProvider, options =>
			{
				options.ClientId = oauth.Google.ClientId;
				options.ClientSecret = oauth.Google.ClientSecret;
				options.SignInScheme = ExternalLoginController.CookieScheme;
			});
		}

		return services;
	}
}
