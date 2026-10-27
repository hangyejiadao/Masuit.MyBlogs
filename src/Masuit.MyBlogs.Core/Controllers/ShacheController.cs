using System.Net;
using Masuit.MyBlogs.Core.Extensions.Firewall;
using Microsoft.AspNetCore.Authorization;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 刹车游戏（REDLINE，静态页位于 /shache/）全球排行榜接口，匿名可用：
/// 每个 IP 只保留一条最好成绩，榜单按刹停总用时升序取前 20 名。
/// </summary>
[AllowAnonymous]
public sealed class ShacheController(IBrakeLeaderboardEntryService leaderboardService) : Controller
{
	/// <summary>
	/// 排行榜展示条数
	/// </summary>
	private const int TopSize = 20;

	/// <summary>
	/// 获取排行榜前 20 名，并附带请求方自己的 IP 供前端高亮
	/// </summary>
	/// <returns></returns>
	[HttpGet, AllowAccessFirewall]
	public IActionResult Top()
	{
		var entries = leaderboardService.GetQuery(e => true).OrderBy(e => e.Time).ThenBy(e => e.UpdatedAt).Take(TopSize).ToList();
		return Json(new
		{
			clientIp = GetClientIpString(),
			entries = entries.Select((e, i) => new
			{
				rank = i + 1,
				ip = e.Ip,
				region = e.Region,
				time = e.Time,
				reaction = e.Reaction,
				distance = e.Distance,
				createdAt = e.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
			}).ToList()
		});
	}

	/// <summary>
	/// 提交一局成绩：成绩合法才入库；同一 IP 已有更好成绩时不更新，
	/// 返回该 IP 的当前全体名次与其最好成绩
	/// </summary>
	/// <param name="input">本局成绩</param>
	/// <returns></returns>
	[HttpPost, AllowAccessFirewall]
	public IActionResult Submit([FromBody] BrakeScoreInput input)
	{
		if (input == null || !IsValidScore(input))
		{
			return BadRequest(new { error = "成绩不合法" });
		}

		var ip = GetClientIpString();
		var now = DateTime.Now;
		var existing = leaderboardService.GetQuery(e => e.Ip == ip).FirstOrDefault();
		try
		{
			if (existing == null)
			{
				existing = leaderboardService.AddEntitySaved(new BrakeLeaderboardEntry
				{
					Ip = ip,
					Region = ResolveRegion(ip),
					Time = input.Time,
					Reaction = input.Reaction,
					Distance = input.Distance,
					PeakForce = input.PeakForce,
					CreatedAt = now,
					UpdatedAt = now
				});
			}
			else if (input.Time < existing.Time)
			{
				existing.Time = input.Time;
				existing.Reaction = input.Reaction;
				existing.Distance = input.Distance;
				existing.PeakForce = input.PeakForce;
				existing.Region = ResolveRegion(ip);
				existing.UpdatedAt = now;
				leaderboardService.SaveChanges();
			}
		}
		catch (DbUpdateException)
		{
			// 同一 IP 并发提交撞上唯一索引时，以库中已有记录为准计算名次，不再重复写
			existing = leaderboardService.GetQuery(e => e.Ip == ip).FirstOrDefault() ?? existing;
			if (existing == null)
			{
				return StatusCode(StatusCodes.Status500InternalServerError, new { error = "成绩保存失败，请稍后再试" });
			}
		}

		var rank = leaderboardService.GetQuery(e => e.Time < existing.Time).Count() + 1;
		return Json(new { rank, time = existing.Time });
	}

	/// <summary>
	/// 与客户端同款成绩合法性校验
	/// </summary>
	/// <param name="input">待校验成绩</param>
	/// <returns></returns>
	private static bool IsValidScore(BrakeScoreInput input)
	{
		return double.IsFinite(input.Time) && double.IsFinite(input.Reaction) && double.IsFinite(input.Distance) && double.IsFinite(input.PeakForce)
			&& input.Time > 0 && input.Time <= 30
			&& input.Reaction >= 0 && input.Reaction <= input.Time
			&& input.Distance >= 0 && input.Distance <= 1000
			&& input.PeakForce >= 0 && input.PeakForce < 1600;
	}

	/// <summary>
	/// 取请求方真实 IP：站点在 Cloudflare 与 nginx 之后，优先 CF-Connecting-IP，
	/// 其次 X-Forwarded-For 第一段，最后回退到连接远端地址；IPv4 映射的 IPv6 统一转回 IPv4 形式
	/// </summary>
	/// <returns></returns>
	private string GetClientIpString()
	{
		string candidate = null;
		if (Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp) && !string.IsNullOrWhiteSpace(cfIp))
		{
			candidate = cfIp.ToString().Split(',')[0].Trim();
		}
		else if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
		{
			candidate = forwardedFor.ToString().Split(',')[0].Trim();
		}

		if (!IPAddress.TryParse(candidate, out var ip))
		{
			ip = HttpContext.Connection.RemoteIpAddress;
		}

		if (ip != null && ip.IsIPv4MappedToIPv6)
		{
			ip = ip.MapToIPv4();
		}

		return ip?.ToString() ?? "未知";
	}

	/// <summary>
	/// 解析 IP 归属区域：优先 MaxMind 的省份+城市，回退 QQWry 的区域串，
	/// 内网显示「内网」，其余拿不到时为「未知」
	/// </summary>
	/// <param name="ip">IP 字符串</param>
	/// <returns></returns>
	private static string ResolveRegion(string ip)
	{
		if (!IPAddress.TryParse(ip, out var address))
		{
			return "未知";
		}

		if (address.IsPrivateIP())
		{
			return "内网";
		}

		try
		{
			var location = address.GetIPLocation();
			var state = location.State?.Trim();
			var city = location.City?.Trim();
			string region = null;
			if (!string.IsNullOrEmpty(state) && !string.IsNullOrEmpty(city))
			{
				region = city.StartsWith(state) ? city : state + city;
			}
			else if (!string.IsNullOrEmpty(city))
			{
				region = city;
			}
			else if (!string.IsNullOrWhiteSpace(location.Address2))
			{
				region = location.Address2.Trim();
			}

			if (string.IsNullOrEmpty(region))
			{
				return "未知";
			}

			return region.Length > 128 ? region[..128] : region;
		}
		catch
		{
			return "未知";
		}
	}

	/// <summary>
	/// 刹车游戏成绩提交参数
	/// </summary>
	public sealed class BrakeScoreInput
	{
		/// <summary>
		/// 刹停总用时（秒）
		/// </summary>
		public double Time { get; set; }

		/// <summary>
		/// 反应时间（秒）
		/// </summary>
		public double Reaction { get; set; }

		/// <summary>
		/// 制动距离（米）
		/// </summary>
		public double Distance { get; set; }

		/// <summary>
		/// 峰值刹车力度（N）
		/// </summary>
		public double PeakForce { get; set; }
	}
}
