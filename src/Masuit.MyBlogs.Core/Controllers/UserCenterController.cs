using Masuit.Tools.Logging;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 普通用户中心（前台）：与管理员后台（Vue 控制面板）彻底分开的普通用户自助区域，
/// 含独立登录页、我的订单与已购 License 管理；所有数据严格按当前登录用户隔离查询，绝不跨用户。
/// </summary>
public sealed class UserCenterController : BaseController
{
	/// <summary>
	/// 订单服务
	/// </summary>
	public IAppOrderService AppOrderService { get; set; }

	/// <summary>
	/// 授权服务
	/// </summary>
	public IAppLicenseService AppLicenseService { get; set; }

	/// <summary>
	/// 用户中心独立登录页：仅服务普通用户，与全站 Passport 登录页、管理员登录完全分开的独立入口。
	/// 本页只负责展示与登录后回跳，账号密码校验仍提交到 Passport 登录接口（复用其 RSA 加密传输、
	/// 图形验证码与防爆破逻辑，行为与现有登录完全一致）；第三方登录仍走 /Login/GitHub、/Login/Google，
	/// 复用现有 OAuth 配置与绑定/自动注册流程。
	/// </summary>
	/// <param name="returnUrl">登录成功后回到的站内地址，仅接受站内相对路径，缺省回用户中心首页</param>
	/// <returns></returns>
	[HttpGet]
	public IActionResult Login(string returnUrl = null)
	{
		var target = NormalizeReturnUrl(returnUrl);
		if (CurrentUser.Id > 0)
		{
			return LocalRedirect(target);
		}

		try
		{
			// 与 Passport 登录页一致的 RSA 密钥分发：公钥写 Cookie 供前端加密密码，私钥存 Session 供登录接口解密
			var keys = RsaCrypt.GenerateRsaKeys(RsaKeyType.PKCS1);
			Response.Cookies.Append(nameof(keys.PublicKey), keys.PublicKey, new CookieOptions
			{
				SameSite = SameSiteMode.Lax
			});
			HttpContext.Session.Set(nameof(keys.PrivateKey), keys.PrivateKey);
			// Passport 登录接口成功后按 refer Cookie 回跳，这里将其固定为用户中心的目标地址
			Response.Cookies.Append("refer", target, new CookieOptions
			{
				SameSite = SameSiteMode.Lax
			});
		}
		catch (Exception ex)
		{
			LogManager.Error("用户中心登录页初始化 RSA 密钥失败", ex);
			throw;
		}

		ViewBag.ReturnUrl = target;
		return View();
	}

	/// <summary>
	/// 用户中心首页：当前用户昵称/邮箱概览与子页入口（我的订单、已购 License），未登录跳独立登录页
	/// </summary>
	/// <returns></returns>
	[HttpGet]
	public IActionResult Index()
	{
		if (CurrentUser.Id <= 0)
		{
			return RedirectToLogin("/UserCenter");
		}

		try
		{
			ViewBag.OrderCount = AppOrderService.GetQuery(o => o.UserInfoId == CurrentUser.Id).Count();
			ViewBag.LicenseCount = AppLicenseService.GetQuery(l => l.UserInfoId == CurrentUser.Id).Count();
		}
		catch (Exception ex)
		{
			LogManager.Error($"用户中心首页统计加载失败，用户id：{CurrentUser.Id}", ex);
			throw;
		}

		return View(CurrentUser);
	}

	/// <summary>
	/// 我的订单：当前用户自己的全部订单（按下单时间倒序），已支付订单带出已发放的授权码与到期时间
	/// </summary>
	/// <returns></returns>
	[HttpGet]
	public IActionResult Orders()
	{
		if (CurrentUser.Id <= 0)
		{
			return RedirectToLogin("/UserCenter/Orders");
		}

		try
		{
			var orders = AppOrderService.GetQuery(o => o.UserInfoId == CurrentUser.Id).Include(o => o.Application).Include(o => o.IssuedLicense).OrderByDescending(o => o.CreatedAt).ToList();
			return View(orders);
		}
		catch (Exception ex)
		{
			LogManager.Error($"用户中心我的订单加载失败，用户id：{CurrentUser.Id}", ex);
			throw;
		}
	}

	/// <summary>
	/// 已购 License 管理：当前用户自己的全部授权，只读管理（查看全文与复制授权码，不提供修改/删除）
	/// </summary>
	/// <returns></returns>
	[HttpGet]
	public IActionResult Licenses()
	{
		if (CurrentUser.Id <= 0)
		{
			return RedirectToLogin("/UserCenter/Licenses");
		}

		try
		{
			var licenses = AppLicenseService.GetQuery(l => l.UserInfoId == CurrentUser.Id).Include(l => l.Application).Include(l => l.PricePlan).OrderByDescending(l => l.StartDate).ThenByDescending(l => l.Id).ToList();
			return View(licenses);
		}
		catch (Exception ex)
		{
			LogManager.Error($"用户中心授权列表加载失败，用户id：{CurrentUser.Id}", ex);
			throw;
		}
	}

	/// <summary>
	/// 跳转到用户中心独立登录页，并带登录成功后的回跳地址
	/// </summary>
	/// <param name="returnPath">登录成功后回到的站内路径</param>
	/// <returns></returns>
	private IActionResult RedirectToLogin(string returnPath)
	{
		return Redirect($"/UserCenter/Login?returnUrl={Uri.EscapeDataString(returnPath)}");
	}

	/// <summary>
	/// 规范化回跳地址：仅允许站内相对路径，外部地址或空值一律回用户中心首页，防止开放重定向
	/// </summary>
	/// <param name="returnUrl">待校验的回跳地址</param>
	/// <returns>安全的站内回跳地址</returns>
	private string NormalizeReturnUrl(string returnUrl)
	{
		return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/UserCenter";
	}
}
