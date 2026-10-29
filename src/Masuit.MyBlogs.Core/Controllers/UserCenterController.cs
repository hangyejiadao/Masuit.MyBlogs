using Hangfire;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.MyBlogs.Core.Extensions.Hangfire;
using Masuit.Tools.AspNetCore.ModelBinder;
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
	/// 用户中心独立注册页：普通用户自助注册入口，与登录页同一套 RSA 密钥分发方式（公钥写 Cookie、私钥存 Session），
	/// 注册提交走本控制器的 Register 接口；已登录用户直接回跳，不重复注册。
	/// </summary>
	/// <param name="returnUrl">注册成功后回到的站内地址，仅接受站内相对路径，缺省回用户中心首页</param>
	/// <returns></returns>
	[HttpGet]
	public IActionResult Register(string returnUrl = null)
	{
		var target = NormalizeReturnUrl(returnUrl);
		if (CurrentUser.Id > 0)
		{
			return LocalRedirect(target);
		}

		try
		{
			// 与登录页一致的 RSA 密钥分发：公钥写 Cookie 供前端加密密码，私钥存 Session 供注册接口解密
			var keys = RsaCrypt.GenerateRsaKeys(RsaKeyType.PKCS1);
			Response.Cookies.Append(nameof(keys.PublicKey), keys.PublicKey, new CookieOptions
			{
				SameSite = SameSiteMode.Lax
			});
			HttpContext.Session.Set(nameof(keys.PrivateKey), keys.PrivateKey);
		}
		catch (Exception ex)
		{
			LogManager.Error("用户中心注册页初始化 RSA 密钥失败", ex);
			throw;
		}

		ViewBag.ReturnUrl = target;
		return View();
	}

	/// <summary>
	/// 普通用户注册提交：图形验证码校验 → 邮箱格式与邮箱验证码校验（验证码须先经 /Validate/SendCode 发送到注册邮箱，与评论流程共用同一份 Redis 验证码）→ RSA 解密密码 → 服务端校验（用户名/昵称唯一性、密码长度与两次一致）→
	/// 复用 UserInfoService.Register 落库（内部加盐哈希，固定为普通用户）→ 自动登录（写 Session 并记登录记录）→ 返回回跳地址。
	/// </summary>
	/// <param name="username">用户名，3-20 位且不能包含空白字符，注册后用于登录</param>
	/// <param name="nickname">昵称，可空，空时取用户名，最长 30 位</param>
	/// <param name="email">邮箱，须格式正确且不与现有邮箱重复</param>
	/// <param name="emailCode">邮箱验证码，注册前发送到注册邮箱的验证码，校验通过后即失效</param>
	/// <param name="password">RSA 加密后的密码，明文 6-20 位</param>
	/// <param name="passwordConfirm">RSA 加密后的确认密码，须与密码一致</param>
	/// <param name="valid">图形验证码</param>
	/// <param name="returnUrl">注册成功后回到的站内地址，仅接受站内相对路径</param>
	/// <returns>统一信封结果，成功时 Data 为回跳地址</returns>
	[HttpPost, ValidateAntiForgeryToken, DistributedLockFilter]
	public async Task<IActionResult> Register([FromBodyOrDefault, Required] string username, [FromBodyOrDefault] string nickname, [FromBodyOrDefault, Required] string email, [FromBodyOrDefault, Required] string emailCode, [FromBodyOrDefault, Required] string password, [FromBodyOrDefault, Required] string passwordConfirm, [FromBodyOrDefault, Required] string valid, [FromBodyOrDefault] string returnUrl)
	{
		string validSession = HttpContext.GetRedisSession<string>("valid") ?? string.Empty;
		if (string.IsNullOrEmpty(validSession) || string.IsNullOrEmpty(valid) || !valid.Trim().Equals(validSession, StringComparison.InvariantCultureIgnoreCase))
		{
			return ResultData(null, false, "验证码错误");
		}

		HttpContext.RemoveRedisSession("valid");
		username = username.Trim();
		nickname = nickname?.Trim() ?? string.Empty;
		email = email.Trim();
		if (username.Length is < 3 or > 20 || username.Any(char.IsWhiteSpace))
		{
			return ResultData(null, false, "用户名需为 3-20 位且不能包含空白字符");
		}

		if (nickname.Length > 30)
		{
			return ResultData(null, false, "昵称长度不能超过 30 位");
		}

		if (email.Length is < 5 or > 100 || !IsValidEmail(email))
		{
			return ResultData(null, false, "邮箱格式不正确");
		}

		var savedEmailCode = await RedisHelper.GetAsync("code:" + email);
		if (string.IsNullOrEmpty(savedEmailCode) || string.IsNullOrEmpty(emailCode) || !savedEmailCode.Equals(emailCode.Trim(), StringComparison.InvariantCultureIgnoreCase))
		{
			return ResultData(null, false, "邮箱验证码不正确或已过期，请重新获取");
		}

		string plainPassword;
		string plainPasswordConfirm;
		try
		{
			var privateKey = HttpContext.Session.Get<string>(nameof(RsaKey.PrivateKey));
			plainPassword = password.RSADecrypt(privateKey);
			plainPasswordConfirm = passwordConfirm.RSADecrypt(privateKey);
		}
		catch (Exception ex)
		{
			LogManager.Error("普通用户注册密码解密失败", ex);
			return ResultData(null, false, "注册失败，请刷新页面后重试");
		}

		if (plainPassword.Length is < 6 or > 20)
		{
			return ResultData(null, false, "密码长度需为 6-20 位");
		}

		if (!plainPassword.Equals(plainPasswordConfirm))
		{
			return ResultData(null, false, "两次输入的密码不一致");
		}

		if (UserInfoService.UsernameExist(username))
		{
			return ResultData(null, false, "该用户名已被注册");
		}

		if (UserInfoService.EmailExist(email))
		{
			return ResultData(null, false, "该邮箱已被注册");
		}

		var user = UserInfoService.Register(new UserInfo
		{
			Username = username,
			NickName = string.IsNullOrEmpty(nickname) ? username : nickname,
			Email = email,
			Password = plainPassword,
			IsAdmin = false,
			Avatar = string.Empty,
			QQorWechat = string.Empty
		});
		if (user == null)
		{
			return ResultData(null, false, "注册失败，用户名或邮箱已被注册");
		}

		await RedisHelper.DelAsync("code:" + email);
		var userDto = UserInfoService.Login(username, plainPassword);
		if (userDto != null)
		{
			HttpContext.Session.Set(SessionKey.UserInfo, userDto);
			BackgroundJob.Enqueue<IHangfireBackJob>(job => job.LoginRecord(userDto, ClientIP.ToString(), LoginType.Default));
		}

		Response.Cookies.Delete(nameof(RsaKey.PublicKey), new CookieOptions
		{
			SameSite = SameSiteMode.Lax
		});
		HttpContext.Session.Remove(nameof(RsaKey.PrivateKey));
		return ResultData(NormalizeReturnUrl(returnUrl), true, "注册成功，已自动登录");
	}

	/// <summary>
	/// 邮箱格式校验：能被 MailAddress 完整解析且域名带点（如 qq.com、gmail.com），与实体上的邮箱校验口径保持一致的宽松判定
	/// </summary>
	/// <param name="email">待校验的邮箱地址</param>
	/// <returns>格式正确返回 true</returns>
	private static bool IsValidEmail(string email)
	{
		try
		{
			var address = new System.Net.Mail.MailAddress(email);
			return address.Address.Equals(email, StringComparison.InvariantCultureIgnoreCase) && address.Host.Contains('.');
		}
		catch (FormatException)
		{
			return false;
		}
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
