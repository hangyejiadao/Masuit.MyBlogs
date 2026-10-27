using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Essensoft.Paylinks.Alipay.Mvc.Extensions;
using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.MyBlogs.Core.Extensions.Firewall;
using Microsoft.AspNetCore.Authorization;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 前台软件购买商店：展示受授权管理的应用与价格方案，登录用户下单后走支付宝电脑网站支付，
/// 支付成功由支付宝异步通知自动发放授权（口径与后台手工签发一致）。全程不允许绕过支付直接发授权。
/// </summary>
public sealed class StoreController : BaseController
{
	/// <summary>
	/// 订单号里表示「在线购买」的授权备注前缀，通知重试时靠它找回已发但未回填的授权，保证不重复发放
	/// </summary>
	private const string OrderRemarkPrefix = "在线购买，订单号 ";

	/// <summary>
	/// 应用服务
	/// </summary>
	public IApplicationInfoService ApplicationInfoService { get; set; }

	/// <summary>
	/// 价格方案服务
	/// </summary>
	public IAppPricePlanService AppPricePlanService { get; set; }

	/// <summary>
	/// 授权服务
	/// </summary>
	public IAppLicenseService AppLicenseService { get; set; }

	/// <summary>
	/// 订单服务
	/// </summary>
	public IAppOrderService AppOrderService { get; set; }

	/// <summary>
	/// 支付宝支付服务
	/// </summary>
	public IAlipayPayService AlipayPayService { get; set; }

	/// <summary>
	/// 商店首页：全部在售应用（带价格方案，用于算最低价与方案数）
	/// </summary>
	/// <returns></returns>
	[HttpGet]
	public IActionResult Index()
	{
		var apps = ApplicationInfoService.GetQuery(a => a.Status == Status.Available).Include(a => a.PricePlans).OrderBy(a => a.Name).ToList();
		return View(apps);
	}

	/// <summary>
	/// 应用详情：应用信息、版本下载与价格方案单选列表，选中方案即显示应付金额
	/// </summary>
	/// <param name="id">应用id</param>
	/// <returns></returns>
	[HttpGet]
	public IActionResult Detail(int id)
	{
		var app = ApplicationInfoService.GetQuery(a => a.Id == id && a.Status == Status.Available).Include(a => a.PricePlans).Include(a => a.Versions).FirstOrDefault() ?? throw new NotFoundException("应用不存在或已下架！");
		ViewBag.Plans = app.PricePlans.Where(p => p.Status == Status.Available).OrderBy(p => p.Sort).ThenBy(p => p.Id).ToList();
		ViewBag.Versions = app.Versions.Where(v => v.Status == Status.Available).OrderBy(v => v.Sort).ThenByDescending(v => v.Id).ToList();
		return View(app);
	}

	/// <summary>
	/// 立即购买：必须登录；按所选方案生成待支付订单（金额与有效期取下单快照），随后跳支付页
	/// </summary>
	/// <param name="planId">所选价格方案id</param>
	/// <returns></returns>
	[HttpPost, ValidateAntiForgeryToken]
	public IActionResult Buy(int planId)
	{
		if (CurrentUser.Id <= 0)
		{
			return RedirectToLogin("/Store");
		}

		var plan = AppPricePlanService.GetQuery(p => p.Id == planId && p.Status == Status.Available).Include(p => p.Application).FirstOrDefault();
		if (plan == null || plan.Application == null || plan.Application.Status != Status.Available)
		{
			throw new NotFoundException("价格方案不存在或应用已下架！");
		}

		if (plan.Price <= 0)
		{
			TempData["StoreError"] = "该方案为免费方案，无法在线支付，请联系管理员为您发放授权。";
			return RedirectToAction(nameof(Detail), new { id = plan.ApplicationInfoId });
		}

		var order = AppOrderService.AddEntitySaved(new AppOrder
		{
			OrderNo = GenerateOrderNo(),
			UserInfoId = CurrentUser.Id,
			ApplicationInfoId = plan.ApplicationInfoId,
			PricePlanId = plan.Id,
			PlanName = plan.Name,
			PlanPrice = plan.Price,
			PlanValidityDays = plan.ValidityDays,
			Amount = plan.Price,
			OrderStatus = AppOrderStatus.Pending,
			CreatedAt = DateTime.Now,
			Status = Status.Available
		});
		return RedirectToAction(nameof(Pay), new { id = order.Id });
	}

	/// <summary>
	/// 支付页：展示订单信息；已支付显示发放的授权，已关闭提示重新下单，
	/// 待支付且支付通道已配置时显示「支付宝支付」按钮，未配置时明确提示联系管理员（订单保持待支付）
	/// </summary>
	/// <param name="id">订单id</param>
	/// <returns></returns>
	[HttpGet]
	public IActionResult Pay(int id)
	{
		if (CurrentUser.Id <= 0)
		{
			return RedirectToLogin($"/Store/Pay/{id}");
		}

		var order = GetOwnOrder(id);
		ViewBag.AlipayConfigured = AlipayPayService.Configured;
		ViewBag.IssuedLicense = order.IssuedLicenseId.HasValue ? AppLicenseService.GetById(order.IssuedLicenseId.Value) : null;
		return View(order);
	}

	/// <summary>
	/// 发起支付宝支付：仅订单本人、订单待支付、通道已配置三者同时满足才构建支付表单并整页输出（自动跳支付宝）
	/// </summary>
	/// <param name="id">订单id</param>
	/// <returns></returns>
	[HttpPost, ValidateAntiForgeryToken]
	public async Task<IActionResult> PayNow(int id)
	{
		if (CurrentUser.Id <= 0)
		{
			return RedirectToLogin($"/Store/Pay/{id}");
		}

		var order = GetOwnOrder(id);
		if (order.OrderStatus != AppOrderStatus.Pending)
		{
			return RedirectToAction(nameof(Pay), new { id });
		}

		if (!AlipayPayService.Configured)
		{
			TempData["StoreError"] = "支付通道尚未配置，请联系管理员。";
			return RedirectToAction(nameof(Pay), new { id });
		}

		var subject = $"{order.Application?.Name} - {order.PlanName}";
		var form = await AlipayPayService.BuildPagePayFormAsync(order, subject, GetNotifyUrl(), GetReturnUrl());
		return Content(form, "text/html", Encoding.UTF8);
	}

	/// <summary>
	/// 支付宝异步通知（服务器回调）：验签 → 校验应用/订单/金额 → 订单置已支付 → 自动发放授权 → 回 success。
	/// 处理必须幂等：重复通知只确认不重复发授权；任何校验失败都回 failure 让支付宝重试。
	/// 本接口只负责「确认已收款并履约」，同步跳回页只展示状态、从不改订单。
	/// </summary>
	/// <returns></returns>
	[HttpPost, AllowAnonymous, AllowAccessFirewall]
	public async Task<IActionResult> AlipayNotify()
	{
		try
		{
			if (!AlipayPayService.Configured)
			{
				return Content("failure");
			}

			var parameters = await this.GetAlipayParametersAsync();
			var notify = await AlipayPayService.VerifyNotifyAsync(parameters);
			if (notify == null || notify.AppId != AppConfig.Alipay.AppId)
			{
				return Content("failure");
			}

			if (notify.TradeStatus is not ("TRADE_SUCCESS" or "TRADE_FINISHED"))
			{
				// 已验签的非成功状态通知（如等待付款、交易关闭）无需处理，直接确认收到，避免支付宝无意义重试
				return Content("success");
			}

			var order = AppOrderService.GetQuery(o => o.OrderNo == notify.OutTradeNo).FirstOrDefault();
			if (order == null)
			{
				return Content("failure");
			}

			if (!decimal.TryParse(notify.TotalAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var paidAmount) || paidAmount != order.Amount)
			{
				return Content("failure");
			}

			if (order.OrderStatus == AppOrderStatus.Paid)
			{
				return Content("success");
			}

			if (order.OrderStatus != AppOrderStatus.Pending)
			{
				return Content("failure");
			}

			// 同一订单的并发/重试通知串行化，避免重复发授权；拿不到锁说明已有一次通知在处理，本次直接回 failure 等重试
			var lockKey = $"alipay:notify:{order.OrderNo}";
			if (!await RedisHelper.SetNxAsync(lockKey, "1", TimeSpan.FromSeconds(60)))
			{
				return Content("failure");
			}

			try
			{
				MarkPaidAndIssueLicense(order, notify.TradeNo, notify.GmtPayment.DateTime);
			}
			finally
			{
				await RedisHelper.DelAsync(lockKey);
			}

			return Content("success");
		}
		catch
		{
			return Content("failure");
		}
	}

	/// <summary>
	/// 支付同步跳回结果页：支付宝把 out_trade_no 等参数拼在 ReturnUrl 后带回。
	/// 这里只按订单当前状态展示结果（状态以异步通知为准），不做任何状态变更。
	/// </summary>
	/// <param name="outTradeNo">支付宝回传的商户订单号</param>
	/// <returns></returns>
	[HttpGet]
	public IActionResult Result([FromQuery(Name = "out_trade_no")] string outTradeNo)
	{
		if (CurrentUser.Id <= 0)
		{
			return RedirectToLogin("/Store/MyOrders");
		}

		var order = AppOrderService.GetQuery(o => o.OrderNo == outTradeNo).Include(o => o.Application).FirstOrDefault();
		if (order == null || (order.UserInfoId != CurrentUser.Id && !CurrentUser.IsAdmin))
		{
			throw new NotFoundException("订单不存在！");
		}

		ViewBag.IssuedLicense = order.IssuedLicenseId.HasValue ? AppLicenseService.GetById(order.IssuedLicenseId.Value) : null;
		return View(order);
	}

	/// <summary>
	/// 我的订单：当前用户的全部订单（按下单时间倒序），已支付订单带出已发放的授权码与到期时间
	/// </summary>
	/// <returns></returns>
	[HttpGet]
	public IActionResult MyOrders()
	{
		if (CurrentUser.Id <= 0)
		{
			return RedirectToLogin("/Store/MyOrders");
		}

		var orders = AppOrderService.GetQuery(o => o.UserInfoId == CurrentUser.Id).Include(o => o.Application).Include(o => o.IssuedLicense).OrderByDescending(o => o.CreatedAt).ToList();
		return View(orders);
	}

	/// <summary>
	/// 把一笔已确认收款的订单置为已支付并自动发放授权；授权口径与后台签发一致：
	/// 成交价取订单快照、生效日为今天、到期日 = 生效日 + 方案天数（永久按 +100 年落库），
	/// 授权备注写入订单号；若此前已发过但未回填（异常中断后重试），按备注找回那条授权回填，不重复发放。
	/// </summary>
	/// <param name="order">待履约的订单（待支付状态）</param>
	/// <param name="tradeNo">支付宝交易号</param>
	/// <param name="paidAt">支付宝回传的付款时间</param>
	private void MarkPaidAndIssueLicense(AppOrder order, string tradeNo, DateTime paidAt)
	{
		var remark = OrderRemarkPrefix + order.OrderNo;
		var license = order.IssuedLicenseId.HasValue
			? AppLicenseService.GetById(order.IssuedLicenseId.Value)
			: AppLicenseService.GetQuery(l => l.Remark == remark).FirstOrDefault();
		if (license == null)
		{
			var user = UserInfoService.GetById(order.UserInfoId) ?? throw new InvalidOperationException("订单用户不存在");
			var startDate = DateTime.Today;
			var expireDate = order.PlanValidityDays > 0 ? startDate.AddDays(order.PlanValidityDays) : startDate.AddYears(100);
			license = AppLicenseService.AddEntitySaved(new AppLicense
			{
				ApplicationInfoId = order.ApplicationInfoId,
				UserInfoId = order.UserInfoId,
				PricePlanId = order.PricePlanId,
				PaidPrice = order.PlanPrice,
				LicenseKey = GenerateUniqueLicenseKey(user),
				StartDate = startDate,
				ExpireDate = expireDate,
				Remark = remark,
				Status = Status.Available,
				LastReminderStage = -1
			});
		}

		order.IssuedLicenseId = license.Id;
		order.OrderStatus = AppOrderStatus.Paid;
		order.PaidAt = paidAt;
		order.AlipayTradeNo = tradeNo;
		AppOrderService.SaveChanges();
	}

	/// <summary>
	/// 读取订单并校验归属：只能访问自己的订单（管理员除外），不存在或不属于本人时抛未找到
	/// </summary>
	/// <param name="id">订单id</param>
	/// <returns></returns>
	private AppOrder GetOwnOrder(int id)
	{
		var order = AppOrderService.GetQuery(o => o.Id == id).Include(o => o.Application).FirstOrDefault() ?? throw new NotFoundException("订单不存在！");
		if (order.UserInfoId != CurrentUser.Id && !CurrentUser.IsAdmin)
		{
			throw new NotFoundException("订单不存在！");
		}

		return order;
	}

	/// <summary>
	/// 跳转到前台登录页并带回跳地址（Passport 登录页用 from 参数回跳）
	/// </summary>
	/// <param name="returnPath">登录成功后回到的站内路径</param>
	/// <returns></returns>
	private IActionResult RedirectToLogin(string returnPath)
	{
		return Redirect($"/Passport/Login?from={Uri.EscapeDataString(returnPath)}");
	}

	/// <summary>
	/// 异步通知地址：优先配置值，留空时按本次请求的域名拼 /Store/AlipayNotify
	/// </summary>
	/// <returns></returns>
	private string GetNotifyUrl()
	{
		return string.IsNullOrWhiteSpace(AppConfig.Alipay.NotifyUrl)
			? $"{Request.Scheme}://{Request.Host}/Store/AlipayNotify"
			: AppConfig.Alipay.NotifyUrl.Trim();
	}

	/// <summary>
	/// 同步跳回地址：优先配置值，留空时按本次请求的域名拼 /Store/Result
	/// </summary>
	/// <returns></returns>
	private string GetReturnUrl()
	{
		return string.IsNullOrWhiteSpace(AppConfig.Alipay.ReturnUrl)
			? $"{Request.Scheme}://{Request.Host}/Store/Result"
			: AppConfig.Alipay.ReturnUrl.Trim();
	}

	/// <summary>
	/// 生成订单号：yyyyMMddHHmmss + 6 位随机数字；落库前查唯一性，极低概率碰撞时重新生成
	/// </summary>
	/// <returns></returns>
	private string GenerateOrderNo()
	{
		string orderNo;
		do
		{
			orderNo = DateTime.Now.ToString("yyyyMMddHHmmss") + RandomNumberGenerator.GetInt32(100000, 1000000);
		} while (AppOrderService.Any(o => o.OrderNo == orderNo));
		return orderNo;
	}

	/// <summary>
	/// 为被授权用户生成授权码，与后台签发同一算法：
	/// 以「用户邮箱（无邮箱时用用户名）+ 当前毫秒时间戳 + 随机数」为原文做 SHA256，生成后再查库确认未被占用
	/// </summary>
	/// <param name="user">被授权的博客用户</param>
	/// <returns></returns>
	private string GenerateUniqueLicenseKey(UserInfo user)
	{
		var identity = string.IsNullOrWhiteSpace(user.Email) ? user.Username : user.Email.Trim();
		string key;
		do
		{
			var material = $"{identity}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|{Guid.NewGuid():N}";
			key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
		} while (AppLicenseService.Any(l => l.LicenseKey == key));
		return key;
	}
}
