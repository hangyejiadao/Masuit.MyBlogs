using Masuit.MyBlogs.Core.Common;
using Masuit.MyBlogs.Core.Common.ReverseTunnel;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.Tools.AspNetCore.ModelBinder;
using System.Text.RegularExpressions;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 端口转发管理，仅管理员可用：
/// 维护 TCP 转发规则，由转发引擎把监听端口的连接双向转发到目标主机端口，增删改即时生效。
/// 博客运行在 Docker 容器中，只有 10000-10100 端口段会发布到宿主机，监听端口只能落在该区间内。
/// </summary>
public sealed class PortForwardController: BaseController
{
/// <summary>
/// 允许的监听端口下限（Docker 宿主机发布端口段）
/// </summary>
private const int MinListenPort = 10000;

/// <summary>
/// 允许的监听端口上限（Docker 宿主机发布端口段）
/// </summary>
private const int MaxListenPort = 10100;

/// <summary>
/// 路径转发的保留前缀：与博客自身路由冲突，规则路径不得等于这些前缀或落在它们之下。
/// 除后台/登录/博客内容等基础路由外，还包含第三方登录回调（/signin-github、/signin-google）、
/// 短链（/short）、Blazor 连接（/_blazor）、健康检查（/health）、Hangfire 面板（/taskcenter）与本管理接口自身（/portforward）
/// </summary>
private static readonly string[] ReservedPathPrefixes =
{
"/dashboard", "/passport", "/home", "/api", "/account", "/static", "/wwwroot",
"/post", "/comment", "/search", "/category", "/tag", "/author", "/login", "/register",
"/short", "/signin-github", "/signin-google", "/_blazor", "/health", "/taskcenter",
"/portforward", "/upload", "/file", "/share", "/subscribe", "/vedio", "/serviceunavailable"
};

/// <summary>
/// 端口转发规则服务
/// </summary>
public IPortForwardRuleService PortForwardRuleService { get; set;}

/// <summary>
/// 端口转发引擎（单例），提供运行时状态并在规则变更后同步监听
/// </summary>
public PortForwardRuntime PortForwardRuntime { get; set;}

/// <summary>
/// 端口转发规则分页查询（供后台 Vue 页面使用），可按名称或目标关键字筛选，
/// 返回规则字段并合并转发引擎的运行时状态（是否监听、状态说明、活跃连接、累计流量）
/// </summary>
/// <param name="page">页码，从 1 开始</param>
/// <param name="size">每页条数，最大 100</param>
/// <param name="keyword">名称或目标关键字，可空</param>
/// <returns></returns>
[HttpGet, MyAuthorize]
public IActionResult PortForwardPage(int page = 1, int size = 10, string keyword = null)
{
page = Math.Max(1, page);
size = Math.Clamp(size, 1, 100);
var query = PortForwardRuleService.GetQuery(r => true);
if (!string.IsNullOrWhiteSpace(keyword))
{
var kw = keyword.Trim();
query = query.Where(r => r.Name.Contains(kw) || r.TargetHost.Contains(kw) || r.Remark.Contains(kw));
}

var total = query.Count();
var rules = query.OrderBy(r => r.ListenPort).Skip((page - 1) * size).Take(size).ToList();
var items = rules.Select(r =>
{
var status = PortForwardRuntime.GetStatus(r);
return new
{
r.Id,
r.Name,
			r.Mode,
r.ListenPort,
r.TargetHost,
r.TargetPort,
r.Path,
			r.TunnelId,
			TunnelTokenConfigured = !string.IsNullOrWhiteSpace(r.TunnelTokenHash),
r.Enabled,
r.Remark,
status.IsListening,
status.StatusMessage,
status.ActiveConnections,
status.TotalConnections,
status.TotalBytes
};
}).ToList();
return ResultData(new { Total = total, Items = items });
}

/// <summary>
/// 查询一条规则最近收到的 TCP 连接记录，来源地址只对管理员开放，服务重启后记录会清空。
/// </summary>
/// <param name="ruleId">规则 id。</param>
/// <param name="limit">最多返回条数，最大 500。</param>
/// <returns></returns>
[HttpGet, MyAuthorize]
public IActionResult RecentRequests(int ruleId, int limit = 100)
{
	var rule = PortForwardRuleService.GetById(ruleId);
	if (rule == null)
	{
		return ResultData(null, false, "端口转发规则不存在！");
	}

	return ResultData(new
	{
		RuleId = rule.Id,
		RuleName = rule.Name,
		Items = PortForwardRuntime.GetRecentRequests(rule.Id, Math.Clamp(limit, 1, 500))
	});
}

/// <summary>
/// 新建或修改端口转发规则，保存成功后同步转发引擎使新规则即时生效。
/// 监听端口必须落在 10000-10100：博客运行在 Docker 容器中，只有该端口段会发布到宿主机，
/// 区间外的端口即使在容器内监听成功，外部也连接不进来
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public async Task<IActionResult> SavePortForward([FromBodyOrDefault] PortForwardRuleCommand cmd)
{
if (cmd == null || string.IsNullOrWhiteSpace(cmd.Name))
{
return ResultData(null, false, "规则名称不能为空！");
}

		if (!Enum.IsDefined(cmd.Mode))
		{
			return ResultData(null, false, "转发模式不正确！");
		}

if (cmd.ListenPort is < 1 or > 65535 || cmd.Mode == PortForwardMode.Direct && cmd.TargetPort is < 1 or > 65535)
{
return ResultData(null, false, "监听端口和目标端口必须在 1-65535 之间！");
}

if (cmd.ListenPort is 5000 or 465)
{
return ResultData(null, false, "监听端口不能与博客自身端口（5000/465）相同！");
}

if (cmd.ListenPort is < MinListenPort or > MaxListenPort)
{
return ResultData(null, false, $"监听端口必须在 {MinListenPort}-{MaxListenPort} 之间：博客运行在 Docker 容器中，只有该端口段会发布到宿主机，区间外的端口即使监听成功，外部也无法连接进来！");
}

if (PortForwardRuleService.Any(r => r.ListenPort == cmd.ListenPort && r.Id!= cmd.Id))
{
return ResultData(null, false, $"监听端口 {cmd.ListenPort} 已被其他规则占用，请更换！");
}
		var path = cmd.Path?.Trim();
		if (string.IsNullOrEmpty(path))
		{
			path = null;
		}
		var tunnelId = cmd.TunnelId?.Trim().ToLowerInvariant();
		var tunnelToken = cmd.TunnelToken?.Trim();
		string tunnelTokenHash = null;
		if (cmd.Mode == PortForwardMode.Direct)
		{
			if (string.IsNullOrWhiteSpace(cmd.TargetHost))
			{
				return ResultData(null, false, "普通直连模式的目标地址不能为空！");
			}
			if (cmd.TargetPort is < 1 or > 65535)
			{
				return ResultData(null, false, "目标端口必须在 1-65535 之间！");
			}
			tunnelId = null;
		}
		else
		{
			if (string.IsNullOrWhiteSpace(tunnelId) || tunnelId.Length > 64 || !Regex.IsMatch(tunnelId, "^[A-Za-z0-9_-]+$"))
			{
				return ResultData(null, false, "内网穿透编号只能包含字母、数字、连字符和下划线，长度不超过 64！");
			}
			if (PortForwardRuleService.Any(r => r.Mode == PortForwardMode.ReverseTunnel && r.TunnelId == tunnelId && r.Id != cmd.Id))
			{
				return ResultData(null, false, $"内网穿透编号 {tunnelId} 已被其他规则使用！");
			}
			if (cmd.Id == 0 && string.IsNullOrWhiteSpace(tunnelToken))
			{
				return ResultData(null, false, "新建内网穿透规则必须设置令牌！");
			}
			if (!string.IsNullOrWhiteSpace(tunnelToken) && (tunnelToken.Length < 16 || tunnelToken.Length > 256))
			{
				return ResultData(null, false, "内网穿透令牌长度必须在 16-256 个字符之间！");
			}
			if (!string.IsNullOrWhiteSpace(tunnelToken))
			{
				tunnelTokenHash = ReverseTunnelHub.HashToken(tunnelToken);
			}
			if (!string.IsNullOrWhiteSpace(path))
			{
				return ResultData(null, false, "内网穿透模式不支持路径转发，请清空路径！");
			}
			cmd.TargetHost = string.Empty;
			cmd.TargetPort = 0;
		}

if (path!= null)
{
if (!path.StartsWith('/') || path.Length < 2 || path.Length > 128 || path.EndsWith('/') || path.Contains(' ') || path.Contains(".."))
{
return ResultData(null, false, "路径格式不正确：必须以 / 开头、长度 2-128、不能以 / 结尾、不能包含空格或 ..（如 /mcp）！");
}

var reserved = ReservedPathPrefixes.FirstOrDefault(p => path.Equals(p, StringComparison.OrdinalIgnoreCase) || path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));
if (reserved!= null)
{
return ResultData(null, false, $"路径 {path} 与博客自身路由 {reserved} 冲突，属于保留前缀，不能用作转发路径！");
}

var usedPaths = PortForwardRuleService.GetQuery(r => r.Path!= null && r.Id!= cmd.Id).Select(r => r.Path).ToList();
if (usedPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
{
return ResultData(null, false, $"路径 {path} 已被其他规则使用，请更换！");
}
}

bool ok;
if (cmd.Id > 0)
{
var rule = PortForwardRuleService.GetById(cmd.Id)?? throw new NotFoundException("端口转发规则不存在！");
rule.Name = cmd.Name.Trim();
rule.ListenPort = cmd.ListenPort;
rule.TargetHost = cmd.TargetHost.Trim();
rule.TargetPort = cmd.TargetPort;
		rule.Mode = cmd.Mode;
		rule.TunnelId = tunnelId;
		if (cmd.Mode == PortForwardMode.ReverseTunnel && tunnelTokenHash != null)
		{
			rule.TunnelTokenHash = tunnelTokenHash;
		}
		if (cmd.Mode == PortForwardMode.Direct)
		{
			rule.TunnelTokenHash = null;
		}
rule.Path = path;
rule.Enabled = cmd.Enabled;
rule.Remark = cmd.Remark?.Trim();
ok = PortForwardRuleService.SaveChanges() > 0;
if (ok)
{
await PortForwardRuntime.ReloadAsync();
}

return ResultData(null, ok, ok? "规则已更新，转发已同步生效": "规则更新失败");
}

var added = PortForwardRuleService.AddEntitySaved(new PortForwardRule
{
Name = cmd.Name.Trim(),
			Mode = cmd.Mode,
ListenPort = cmd.ListenPort,
TargetHost = cmd.TargetHost.Trim(),
TargetPort = cmd.TargetPort,
Path = path,
			TunnelId = tunnelId,
			TunnelTokenHash = tunnelTokenHash,
Enabled = cmd.Enabled,
Remark = cmd.Remark?.Trim(),
Status = Status.Available
});
ok = added!= null;
if (ok)
{
await PortForwardRuntime.ReloadAsync();
}

return ResultData(null, ok, ok? "规则已创建，转发已同步生效": "规则创建失败");
}

/// <summary>
/// 删除端口转发规则，并同步转发引擎卸载该监听
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public async Task<IActionResult> DeletePortForward([FromBodyOrDefault] PortForwardIdCommand cmd)
{
var b = PortForwardRuleService.DeleteById(cmd.Id);
if (b)
{
await PortForwardRuntime.ReloadAsync();
}

return ResultData(null, b, b? "删除成功！": "删除失败！");
}
}
