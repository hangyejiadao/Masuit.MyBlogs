using System.Globalization;
using System.Security.Cryptography;
using Essensoft.Paylinks.Alipay.Mvc.Extensions;
using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.MyBlogs.Core.Extensions.Firewall;
using Microsoft.AspNetCore.Authorization;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 前台打赏（支付宝当面付扫码支付）：访客无需登录，在文章页选金额后生成打赏订单，
/// 服务端调 alipay.trade.precreate 生成支付二维码，访客用支付宝 App 扫码付款；
/// 支付成功由支付宝异步通知确认（前端轮询查单接口兜底，通知延迟或丢失时靠主动查询补确认）。
/// 打赏不绑定博客用户，昵称与留言均为访客自填；付款成功后自动写入一条 Donate 记录用于打赏墙展示。
/// </summary>
public sealed class DonatePayController : BaseController
{
	/// <summary>
	/// 打赏订单服务
	/// </summary>
	public IDonateOrderService DonateOrderService { get; set; }

	/// <summary>
	/// 打赏记录服务（付款成功后写打赏墙）
	/// </summary>
	public IDonateService DonateService { get; set; }

	/// <summary>
	/// 支付宝支付服务
	/// </summary>
	public IAlipayPayService AlipayPayService { get; set; }

	/// <summary>
	/// 打赏金额上下限（元）：单笔最低 0.01 元，最高 10000 元；超过上限请分多笔或联系站长
	/// </summary>
	private const decimal MinAmount = 0.01m;

	/// <summary>
	/// 打赏金额上限（元）
	/// </summary>
	private const decimal MaxAmount = 10000m;

	/// <summary>
	/// 创建打赏订单并生成支付二维码：匿名可调；金额校验通过后先落库（待支付），
	/// 再调当面付预下单拿到 qr_code 回填；预下单失败时订单保持待支付（不删单，访客可重试或重新下单）。
	/// </summary>
	/// <param name="request">打赏请求：金额、昵称、留言</param>
	/// <returns>订单号、二维码内容、金额、二维码过期时间</returns>
	[HttpPost]
	public async Task<IActionResult> Create([FromBody] DonateCreateRequest request)
	{
		if (!AlipayPayService.Configured)
		{
			return ResultData(null, false, "打赏通道尚未配置，请联系站长。");
		}

		if (request == null || request.Amount < MinAmount || request.Amount > MaxAmount || Math.Round(request.Amount, 2) != request.Amount)
		{
			return ResultData(null, false, $"打赏金额须在 {MinAmount:0.00} 至 {MaxAmount:0.00} 元之间，且最多两位小数。");
		}

		var nickName = string.IsNullOrWhiteSpace(request.NickName) ? "热心网友" : request.NickName.Trim();
		if (nickName.Length > 32)
		{
			nickName = nickName[..32];
		}

		var message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim();
		if (message != null && message.Length > 200)
		{
			message = message[..200];
		}

		var order = DonateOrderService.AddEntitySaved(new DonateOrder
		{
			OrderNo = GenerateOrderNo(),
			NickName = nickName,
			Message = message,
			Amount = request.Amount,
			OrderStatus = AppOrderStatus.Pending,
			CreatedAt = DateTime.Now,
			Status = Status.Available
		});

		try
		{
			var precreate = await AlipayPayService.CreatePrecreateQrAsync(order.OrderNo, order.Amount, $"博客打赏-{order.OrderNo}", GetDonateNotifyUrl());
			order.QrCode = precreate.QrCode;
			DonateOrderService.SaveChanges();
		}
		catch (Exception ex)
		{
			// 预下单失败不删单：订单保持待支付，访客可在前端点“重新生成二维码”重试
			return ResultData(new { order.OrderNo, order.Amount }, false, $"生成支付二维码失败，请稍后重试。（{ex.Message}）");
		}

		return ResultData(new
		{
			order.OrderNo,
			order.Amount,
			order.QrCode,
			// 二维码有效期 2 小时，前端据此倒计时并提示过期后重新下单
			ExpiresAt = order.CreatedAt.AddHours(2)
		});
	}

	/// <summary>
	/// 查询打赏订单状态（供前端轮询）：先读库；仍待支付且二维码已生成超过 5 秒时，
	/// 主动调支付宝交易查询同步一次状态——异步通知延迟或丢失时靠这次查询补确认，
	/// 避免访客已付款页面却一直显示待支付。已支付/已关闭直接返回库中状态，不再调支付宝。
	/// </summary>
	/// <param name="orderNo">商户订单号</param>
	/// <returns>订单号、金额、状态、状态文案、支付时间、支付宝交易号</returns>
	[HttpGet]
	public async Task<IActionResult> QueryStatus(string orderNo)
	{
		if (string.IsNullOrWhiteSpace(orderNo))
		{
			return ResultData(null, false, "订单号不能为空。");
		}

		var order = DonateOrderService.GetQuery(o => o.OrderNo == orderNo.Trim()).FirstOrDefault();
		if (order == null)
		{
			return ResultData(null, false, "订单不存在。");
		}

		if (order.OrderStatus == AppOrderStatus.Pending && !string.IsNullOrWhiteSpace(order.QrCode) && DateTime.Now - order.CreatedAt > TimeSpan.FromSeconds(5))
		{
			try
			{
				var query = await AlipayPayService.QueryTradeAsync(order.OrderNo);
				if (query.IsPaid)
				{
					decimal.TryParse(query.TotalAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var paidAmount);
					await TryMarkPaidAsync(order, query.TradeNo, DateTime.Now, paidAmount > 0 ? paidAmount : order.Amount);
					order = DonateOrderService.GetById(order.Id);
				}
				else if (query.TradeStatus == "TRADE_CLOSED")
				{
					order.OrderStatus = AppOrderStatus.Closed;
					DonateOrderService.SaveChanges();
				}
			}
			catch
			{
				// 查询失败不影响返回库中状态，前端继续轮询；异步通知仍是主确认通道
			}
		}

		return ResultData(new
		{
			order.OrderNo,
			order.Amount,
			OrderStatus = (int)order.OrderStatus,
			OrderStatusText = StatusText(order.OrderStatus),
			order.PaidAt,
			order.AlipayTradeNo
		});
	}

	/// <summary>
	/// 支付宝异步通知（服务器回调）：验签 → 校验应用/订单/金额 → 订单置已支付 → 写一条 Donate 记录 → 回 success。
	/// 处理必须幂等：重复通知只确认不重复写打赏记录；任何校验失败都回 failure 让支付宝重试。
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

			var order = DonateOrderService.GetQuery(o => o.OrderNo == notify.OutTradeNo).FirstOrDefault();
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

			var marked = await TryMarkPaidAsync(order, notify.TradeNo, notify.GmtPayment.DateTime, paidAmount);
			return Content(marked ? "success" : "failure");
		}
		catch
		{
			return Content("failure");
		}
	}

	/// <summary>
	/// 把一笔待支付的打赏订单置为已支付并写一条 Donate 记录（打赏墙展示用）；
	/// Redis 分布式锁串行化并发/重试通知，保证同一订单只写一条打赏记录。
	/// 拿不到锁说明已有一次通知在处理，本次直接返回 false 让支付宝重试。
	/// </summary>
	/// <param name="order">待支付的打赏订单</param>
	/// <param name="tradeNo">支付宝交易号</param>
	/// <param name="paidAt">付款时间</param>
	/// <param name="paidAmount">实付金额</param>
	/// <returns>是否成功标记为已支付</returns>
	private async Task<bool> TryMarkPaidAsync(DonateOrder order, string tradeNo, DateTime paidAt, decimal paidAmount)
	{
		var lockKey = $"alipay:donate-notify:{order.OrderNo}";
		if (!await RedisHelper.SetNxAsync(lockKey, "1", TimeSpan.FromSeconds(60)))
		{
			return false;
		}

		try
		{
			var fresh = DonateOrderService.GetById(order.Id);
			if (fresh == null || fresh.OrderStatus == AppOrderStatus.Paid)
			{
				return true;
			}

			if (fresh.OrderStatus != AppOrderStatus.Pending)
			{
				return false;
			}

			fresh.OrderStatus = AppOrderStatus.Paid;
			fresh.PaidAt = paidAt;
			fresh.AlipayTradeNo = tradeNo;
			DonateOrderService.SaveChanges();
			DonateService.AddEntitySaved(new Donate
			{
				NickName = string.IsNullOrWhiteSpace(fresh.NickName) ? "热心网友" : fresh.NickName,
				Email = string.Empty,
				QQorWechat = string.Empty,
				Amount = paidAmount.ToString("0.00", CultureInfo.InvariantCulture),
				Via = "支付宝当面付",
				DonateTime = paidAt,
				Status = Status.Available
			});
			return true;
		}
		finally
		{
			await RedisHelper.DelAsync(lockKey);
		}
	}

	/// <summary>
	/// 打赏异步通知地址：优先配置值，留空时按本次请求的域名拼 /DonatePay/AlipayNotify
	/// </summary>
	/// <returns></returns>
	private string GetDonateNotifyUrl()
	{
		return string.IsNullOrWhiteSpace(AppConfig.Alipay.DonateNotifyUrl)
			? $"{Request.Scheme}://{Request.Host}/DonatePay/AlipayNotify"
			: AppConfig.Alipay.DonateNotifyUrl.Trim();
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
		} while (DonateOrderService.Any(o => o.OrderNo == orderNo));
		return orderNo;
	}

	/// <summary>
	/// 订单状态中文文案
	/// </summary>
	/// <param name="status">订单状态</param>
	/// <returns></returns>
	private static string StatusText(AppOrderStatus status)
	{
		return status switch
		{
			AppOrderStatus.Paid => "已支付",
			AppOrderStatus.Closed => "已关闭",
			_ => "待支付"
		};
	}
}

/// <summary>
/// 创建打赏订单的请求体
/// </summary>
public sealed class DonateCreateRequest
{
	/// <summary>
	/// 打赏金额（元），0.01 至 10000，最多两位小数
	/// </summary>
	public decimal Amount { get; set; }

	/// <summary>
	/// 打赏人昵称（可选，默认“热心网友”）
	/// </summary>
	public string NickName { get; set; }

	/// <summary>
	/// 打赏留言（可选，最长 200 字）
	/// </summary>
	public string Message { get; set; }
}
