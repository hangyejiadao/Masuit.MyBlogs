using Masuit.MyBlogs.Core.Extensions;
using Masuit.Tools.AspNetCore.ModelBinder;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 软件购买订单管理（后台），仅管理员可用：分页查询、按状态筛选、查看详情、关闭待支付订单。
/// 只读为主：不提供手动改已支付、手动发放授权等绕过支付的操作，授权只由支付宝异步通知自动发放。
/// </summary>
public sealed class StoreOrderController : BaseController
{
	/// <summary>
	/// 订单服务
	/// </summary>
	public IAppOrderService AppOrderService { get; set; }

	/// <summary>
	/// 订单分页查询（供后台 Vue 页面使用），按下单时间倒序；
	/// 关键字可匹配订单号、用户名、昵称、邮箱、应用名或支付宝交易号；状态参数：0 待支付、1 已支付、2 已关闭，不传为全部
	/// </summary>
	/// <param name="page">页码，从 1 开始</param>
	/// <param name="size">每页条数，最大 100</param>
	/// <param name="keyword">关键字，可空</param>
	/// <param name="status">订单状态（AppOrderStatus 的整数值），可空</param>
	/// <returns></returns>
	[HttpGet, MyAuthorize]
	public IActionResult OrderPage(int page = 1, int size = 10, string keyword = null, int? status = null)
	{
		page = Math.Max(1, page);
		size = Math.Clamp(size, 1, 100);
		var query = AppOrderService.GetQuery(o => true);
		if (status.HasValue && Enum.IsDefined(typeof(AppOrderStatus), status.Value))
		{
			var orderStatus = (AppOrderStatus)status.Value;
			query = query.Where(o => o.OrderStatus == orderStatus);
		}

		if (!string.IsNullOrWhiteSpace(keyword))
		{
			var kw = keyword.Trim();
			query = query.Where(o => o.OrderNo.Contains(kw) || o.User.Username.Contains(kw) || o.User.NickName.Contains(kw) || (o.User.Email != null && o.User.Email.Contains(kw)) || o.Application.Name.Contains(kw) || (o.AlipayTradeNo != null && o.AlipayTradeNo.Contains(kw)));
		}

		var total = query.Count();
		var rows = query.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * size).Take(size).Select(o => new
		{
			o.Id,
			o.OrderNo,
			o.UserInfoId,
			UserName = o.User.Username,
			UserNickName = o.User.NickName,
			UserEmail = o.User.Email,
			o.ApplicationInfoId,
			ApplicationName = o.Application.Name,
			o.PricePlanId,
			o.PlanName,
			o.PlanPrice,
			o.PlanValidityDays,
			o.Amount,
			o.OrderStatus,
			o.CreatedAt,
			o.PaidAt,
			o.AlipayTradeNo,
			o.IssuedLicenseId,
			IssuedLicenseKey = o.IssuedLicense == null ? null : o.IssuedLicense.LicenseKey,
			IssuedLicenseExpireDate = o.IssuedLicense == null ? (DateTime?)null : o.IssuedLicense.ExpireDate
		}).ToList();
		// 状态文案在内存中拼装，避免把本地方法塞进 EF 查询表达式导致无法翻译
		var items = rows.Select(o => new
		{
			o.Id,
			o.OrderNo,
			o.UserInfoId,
			o.UserName,
			o.UserNickName,
			o.UserEmail,
			o.ApplicationInfoId,
			o.ApplicationName,
			o.PricePlanId,
			o.PlanName,
			o.PlanPrice,
			o.PlanValidityDays,
			o.Amount,
			OrderStatus = (int)o.OrderStatus,
			OrderStatusText = StatusText(o.OrderStatus),
			o.CreatedAt,
			o.PaidAt,
			o.AlipayTradeNo,
			o.IssuedLicenseId,
			o.IssuedLicenseKey,
			o.IssuedLicenseExpireDate
		}).ToList();
		return ResultData(new { Total = total, Items = items });
	}

	/// <summary>
	/// 订单详情（供后台 Vue 页面使用），字段与分页列表一致，单条返回
	/// </summary>
	/// <param name="id">订单id</param>
	/// <returns></returns>
	[HttpGet, MyAuthorize]
	public IActionResult OrderDetail(int id)
	{
		var row = AppOrderService.GetQuery(o => o.Id == id).Select(o => new
		{
			o.Id,
			o.OrderNo,
			o.UserInfoId,
			UserName = o.User.Username,
			UserNickName = o.User.NickName,
			UserEmail = o.User.Email,
			o.ApplicationInfoId,
			ApplicationName = o.Application.Name,
			ApplicationAppKey = o.Application.AppKey,
			o.PricePlanId,
			o.PlanName,
			o.PlanPrice,
			o.PlanValidityDays,
			o.Amount,
			o.OrderStatus,
			o.CreatedAt,
			o.PaidAt,
			o.AlipayTradeNo,
			o.IssuedLicenseId,
			IssuedLicenseKey = o.IssuedLicense == null ? null : o.IssuedLicense.LicenseKey,
			IssuedLicenseStartDate = o.IssuedLicense == null ? (DateTime?)null : o.IssuedLicense.StartDate,
			IssuedLicenseExpireDate = o.IssuedLicense == null ? (DateTime?)null : o.IssuedLicense.ExpireDate
		}).FirstOrDefault() ?? throw new NotFoundException("订单不存在！");
		return ResultData(new
		{
			row.Id,
			row.OrderNo,
			row.UserInfoId,
			row.UserName,
			row.UserNickName,
			row.UserEmail,
			row.ApplicationInfoId,
			row.ApplicationName,
			row.ApplicationAppKey,
			row.PricePlanId,
			row.PlanName,
			row.PlanPrice,
			row.PlanValidityDays,
			row.Amount,
			OrderStatus = (int)row.OrderStatus,
			OrderStatusText = StatusText(row.OrderStatus),
			row.CreatedAt,
			row.PaidAt,
			row.AlipayTradeNo,
			row.IssuedLicenseId,
			row.IssuedLicenseKey,
			row.IssuedLicenseStartDate,
			row.IssuedLicenseExpireDate
		});
	}

	/// <summary>
	/// 关闭订单：仅待支付订单可关闭；已支付订单不能关，已关闭的重复关闭直接报错说明
	/// </summary>
	/// <param name="cmd"></param>
	/// <returns></returns>
	[HttpPost, MyAuthorize]
	public IActionResult CloseOrder([FromBodyOrDefault] LicenseIdCommand cmd)
	{
		var order = AppOrderService.GetById(cmd.Id) ?? throw new NotFoundException("订单不存在！");
		if (order.OrderStatus == AppOrderStatus.Paid)
		{
			return ResultData(null, false, "已支付订单不能关闭！");
		}

		if (order.OrderStatus == AppOrderStatus.Closed)
		{
			return ResultData(null, false, "订单已是关闭状态！");
		}

		order.OrderStatus = AppOrderStatus.Closed;
		var b = AppOrderService.SaveChanges() > 0;
		return ResultData(null, b, b ? "订单已关闭" : "关闭失败");
	}

	/// <summary>
	/// 订单状态中文文案（与后台 Vue 的状态筛选、标签保持一致）
	/// </summary>
	/// <param name="status">订单状态</param>
	/// <returns></returns>
	internal static string StatusText(AppOrderStatus status)
	{
		return status switch
		{
			AppOrderStatus.Paid => "已支付",
			AppOrderStatus.Closed => "已关闭",
			_ => "待支付"
		};
	}
}
