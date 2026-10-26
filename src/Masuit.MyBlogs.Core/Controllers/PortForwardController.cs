using Masuit.MyBlogs.Core.Common;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.Tools.AspNetCore.ModelBinder;

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
r.ListenPort,
r.TargetHost,
r.TargetPort,
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

if (string.IsNullOrWhiteSpace(cmd.TargetHost))
{
return ResultData(null, false, "目标地址不能为空！");
}

if (cmd.ListenPort is < 1 or > 65535 || cmd.TargetPort is < 1 or > 65535)
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

bool ok;
if (cmd.Id > 0)
{
var rule = PortForwardRuleService.GetById(cmd.Id)?? throw new NotFoundException("端口转发规则不存在！");
rule.Name = cmd.Name.Trim();
rule.ListenPort = cmd.ListenPort;
rule.TargetHost = cmd.TargetHost.Trim();
rule.TargetPort = cmd.TargetPort;
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
ListenPort = cmd.ListenPort,
TargetHost = cmd.TargetHost.Trim(),
TargetPort = cmd.TargetPort,
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
