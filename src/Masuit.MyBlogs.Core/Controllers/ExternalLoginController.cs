using Hangfire;
using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.MyBlogs.Core.Extensions.Firewall;
using Masuit.MyBlogs.Core.Extensions.Hangfire;
using Masuit.MyBlogs.Core.Models;
using Masuit.Tools.Logging;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 第三方单点登录（GitHub / Google）：
/// 首次登录自动注册本站账号并绑定，之后写入与账号密码登录完全相同的 Session 登录态。
/// </summary>
[ApiExplorerSettings(IgnoreApi = true), ServiceFilter(typeof(FirewallAttribute))]
public sealed class ExternalLoginController : Controller
{
	/// <summary>
	/// 承载第三方身份的临时 Cookie 认证方案名，仅在回调阶段使用
	/// </summary>
	public const string CookieScheme = "ExternalLogin";

	/// <summary>
	/// GitHub 平台标识，同时作为认证方案名
	/// </summary>
	public const string GitHubProvider = "GitHub";

	/// <summary>
	/// Google 平台标识，同时作为认证方案名
	/// </summary>
	public const string GoogleProvider = "Google";

	/// <summary>
	/// 用户服务
	/// </summary>
	public IUserInfoService UserInfoService { get; set; }

	/// <summary>
	/// 第三方账号绑定服务
	/// </summary>
	public IExternalLoginService ExternalLoginService { get; set; }

	/// <summary>
	/// 客户端的真实IP
	/// </summary>
	public string ClientIP => HttpContext.Connection.RemoteIpAddress.ToString();

	/// <summary>
	/// 跳转到 GitHub 授权页
	/// </summary>
	/// <param name="returnUrl">登录成功后回到的本站地址</param>
	/// <returns></returns>
	[HttpGet("/Login/GitHub")]
	public IActionResult GitHub(string returnUrl = null) => ChallengeProvider(GitHubProvider, returnUrl);

	/// <summary>
	/// 跳转到 Google 授权页
	/// </summary>
	/// <param name="returnUrl">登录成功后回到的本站地址</param>
	/// <returns></returns>
	[HttpGet("/Login/Google")]
	public IActionResult Google(string returnUrl = null) => ChallengeProvider(GoogleProvider, returnUrl);

	/// <summary>
	/// 第三方授权完成后的本站回调：换取外部身份，查找或自动注册本站账号，写入 Session 登录态
	/// </summary>
	/// <param name="provider">平台标识：GitHub 或 Google</param>
	/// <param name="returnUrl">登录成功后回到的本站地址</param>
	/// <returns></returns>
	[HttpGet("/Login/External/{provider}")]
	public async Task<IActionResult> Callback(string provider, string returnUrl = null)
	{
		if (provider is not (GitHubProvider or GoogleProvider) || !IsEnabled(provider))
		{
			return RedirectToAction("Login", "Passport");
		}

		try
		{
			var result = await HttpContext.AuthenticateAsync(CookieScheme);
			await HttpContext.SignOutAsync(CookieScheme);
			if (!result.Succeeded || result.Principal == null)
			{
				LogManager.Info($"第三方登录失败：{provider} 未能换取到有效身份");
				return RedirectToAction("Login", "Passport");
			}

			var principal = result.Principal;
			var providerKey = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
			if (string.IsNullOrWhiteSpace(providerKey))
			{
				LogManager.Info($"第三方登录失败：{provider} 返回的身份缺少唯一标识");
				return RedirectToAction("Login", "Passport");
			}

			var email = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value?.Trim() ?? string.Empty;
			var loginName = principal.Claims.FirstOrDefault(c => c.Type.EndsWith("login", StringComparison.OrdinalIgnoreCase))?.Value;
			var nickName = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? loginName ?? string.Empty;
			var avatar = principal.Claims.FirstOrDefault(c => c.Type.Contains("avatar", StringComparison.OrdinalIgnoreCase) || c.Type.Contains("picture", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
			var user = FindOrCreateUser(provider, providerKey, email, loginName, nickName, avatar);
			if (user == null)
			{
				LogManager.Info($"第三方登录失败：{provider} 账号 {providerKey} 未能创建或匹配到本站用户");
				return RedirectToAction("Login", "Passport");
			}

			var dto = user.ToDto();
			HttpContext.Session.Set(SessionKey.UserInfo, dto);
			BackgroundJob.Enqueue<IHangfireBackJob>(job => job.LoginRecord(dto, ClientIP, provider == GitHubProvider ? LoginType.GitHub : LoginType.Google));
			return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
		}
		catch (Exception ex)
		{
			LogManager.Info($"第三方登录异常：{provider}，{ex}");
			return RedirectToAction("Login", "Passport");
		}
	}

	/// <summary>
	/// 发起第三方授权挑战
	/// </summary>
	/// <param name="provider">平台标识</param>
	/// <param name="returnUrl">登录成功后回到的本站地址</param>
	/// <returns></returns>
	private IActionResult ChallengeProvider(string provider, string returnUrl)
	{
		if (!IsEnabled(provider))
		{
			return RedirectToAction("Login", "Passport");
		}

		var properties = new AuthenticationProperties
		{
			RedirectUri = Url.Action(nameof(Callback), new { provider, returnUrl })
		};
		return Challenge(properties, provider);
	}

	/// <summary>
	/// 平台是否已配置凭据并启用
	/// </summary>
	/// <param name="provider">平台标识</param>
	/// <returns></returns>
	private static bool IsEnabled(string provider) => provider switch
	{
		GitHubProvider => AppConfig.OAuth.GitHub.Enabled,
		GoogleProvider => AppConfig.OAuth.Google.Enabled,
		_ => false
	};

	/// <summary>
	/// 按第三方绑定关系查找用户；邮箱与已有账号相同时自动绑定到该账号；都没有时自动注册新账号
	/// </summary>
	/// <param name="provider">平台标识</param>
	/// <param name="providerKey">平台内用户唯一标识</param>
	/// <param name="email">平台返回的邮箱，可能为空</param>
	/// <param name="loginName">平台登录名（GitHub 返回，Google 为空）</param>
	/// <param name="nickName">平台昵称</param>
	/// <param name="avatar">平台头像地址</param>
	/// <returns>本站用户，失败时为 null</returns>
	private UserInfo FindOrCreateUser(string provider, string providerKey, string email, string loginName, string nickName, string avatar)
	{
		var binding = ExternalLoginService.Get(e => e.Provider == provider && e.ProviderKey == providerKey);
		if (binding != null)
		{
			return UserInfoService.GetById(binding.UserInfoId);
		}

		UserInfo user = null;
		if (!string.IsNullOrWhiteSpace(email))
		{
			user = UserInfoService.GetByUsername(email);
		}

		if (user == null)
		{
			var username = BuildUsername(provider, loginName, email, nickName);
			var displayName = string.IsNullOrWhiteSpace(nickName) ? username : nickName.Trim();
			if (displayName.Length > 32)
			{
				displayName = displayName[..32];
			}

			user = UserInfoService.Register(new UserInfo
			{
				Username = username,
				NickName = displayName,
				Email = string.IsNullOrWhiteSpace(email) ? $"{providerKey}@{(provider == GitHubProvider ? "github" : "google")}.oauth.local" : email,
				Password = Guid.NewGuid().ToString("N"),
				Avatar = avatar,
				QQorWechat = string.Empty
			});
			if (user == null)
			{
				user = UserInfoService.GetByUsername(username);
				if (user == null)
				{
					return null;
				}
			}
		}

		ExternalLoginService.AddEntitySaved(new ExternalLogin
		{
			UserInfoId = user.Id,
			Provider = provider,
			ProviderKey = providerKey,
			Email = email
		});
		return user;
	}

	/// <summary>
	/// 生成唯一的本站用户名：平台前缀 + 清洗后的登录名/邮箱前缀/昵称，冲突时追加序号
	/// </summary>
	/// <param name="provider">平台标识</param>
	/// <param name="loginName">平台登录名</param>
	/// <param name="email">平台邮箱</param>
	/// <param name="nickName">平台昵称</param>
	/// <returns>不与现有用户冲突的用户名</returns>
	private string BuildUsername(string provider, string loginName, string email, string nickName)
	{
		var raw = !string.IsNullOrWhiteSpace(loginName) ? loginName : !string.IsNullOrWhiteSpace(email) ? email.Split('@')[0] : nickName;
		if (string.IsNullOrWhiteSpace(raw))
		{
			raw = "user";
		}

		var cleaned = new string(raw.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_').ToArray());
		if (cleaned.Length > 30)
		{
			cleaned = cleaned[..30];
		}

		var baseName = provider.ToLowerInvariant() + "_" + cleaned;
		var candidate = baseName;
		var suffix = 0;
		while (UserInfoService.UsernameExist(candidate))
		{
			suffix++;
			candidate = baseName + "_" + suffix;
		}

		return candidate;
	}
}
