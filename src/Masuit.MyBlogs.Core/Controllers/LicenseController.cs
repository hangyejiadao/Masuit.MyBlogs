using Hangfire;
using Masuit.MyBlogs.Core.Common;
using Masuit.MyBlogs.Core.Common.Mails;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.Tools.AspNetCore.ModelBinder;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 应用与授权（License）管理，仅管理员可用：
/// 维护应用，给用户管理中的普通用户签发授权并约定使用期限，支持续期和手动发送到期提醒。
/// </summary>
public sealed class LicenseController: BaseController
{
/// <summary>
/// 应用服务
/// </summary>
public IApplicationInfoService ApplicationInfoService { get; set;}

/// <summary>
/// 博客用户服务，被授权的客户即用户管理中的普通用户
/// </summary>
public IUserInfoService UserInfoService { get; set;}

/// <summary>
/// 授权服务
/// </summary>
public IAppLicenseService AppLicenseService { get; set;}

/// <summary>
/// 授权管理首页：全部授权按到期时间排序
/// </summary>
/// <returns></returns>
[MyAuthorize]
public IActionResult Index()
{
var licenses = AppLicenseService.GetQuery(l => true).Include(l => l.Application).Include(l => l.User).OrderBy(l => l.ExpireDate).ToList();
ViewBag.Applications = ApplicationInfoService.GetQuery(a => true).OrderBy(a => a.Name).ToList();
ViewBag.Users = UserInfoService.GetQuery(u => !u.IsAdmin).OrderBy(u => u.NickName).ToList();
return View(licenses);
}

/// <summary>
/// 应用管理页
/// </summary>
/// <returns></returns>
[MyAuthorize]
public IActionResult Applications()
{
var apps = ApplicationInfoService.GetQuery(a => true).Include(a => a.Licenses).OrderBy(a => a.Name).ToList();
return View(apps);
}

/// <summary>
/// 应用分页查询（供后台 Vue 页面使用），可按名称或标识关键字筛选
/// </summary>
/// <param name="page">页码，从 1 开始</param>
/// <param name="size">每页条数，最大 100</param>
/// <param name="keyword">名称或标识关键字，可空</param>
/// <returns></returns>
[HttpGet, MyAuthorize]
public IActionResult ApplicationPage(int page = 1, int size = 10, string keyword = null)
{
page = Math.Max(1, page);
size = Math.Clamp(size, 1, 100);
var query = ApplicationInfoService.GetQuery(a => true);
if (!string.IsNullOrWhiteSpace(keyword))
{
var kw = keyword.Trim();
query = query.Where(a => a.Name.Contains(kw) || a.AppKey.Contains(kw));
}

var total = query.Count();
var items = query.OrderBy(a => a.Name).Skip((page - 1) * size).Take(size).Select(a => new
{
a.Id,
a.Name,
a.AppKey,
a.Description,
LicenseCount = a.Licenses.Count
}).ToList();
return ResultData(new { Total = total, Items = items });
}

/// <summary>
/// 授权分页查询（供后台 Vue 页面使用），按到期时间升序，可按授权码、应用名或被授权用户关键字筛选
/// </summary>
/// <param name="page">页码，从 1 开始</param>
/// <param name="size">每页条数，最大 100</param>
/// <param name="keyword">授权码、应用名或客户名关键字，可空</param>
/// <returns></returns>
[HttpGet, MyAuthorize]
public IActionResult LicensePage(int page = 1, int size = 10, string keyword = null)
{
page = Math.Max(1, page);
size = Math.Clamp(size, 1, 100);
var query = AppLicenseService.GetQuery(l => true);
if (!string.IsNullOrWhiteSpace(keyword))
{
var kw = keyword.Trim();
query = query.Where(l => l.LicenseKey.Contains(kw) || l.Application.Name.Contains(kw) || l.User.Username.Contains(kw) || l.User.NickName.Contains(kw) || (l.User.Email != null && l.User.Email.Contains(kw)));
}

var total = query.Count();
var items = query.OrderBy(l => l.ExpireDate).Skip((page - 1) * size).Take(size).Select(l => new
{
l.Id,
l.ApplicationInfoId,
ApplicationName = l.Application.Name,
l.UserInfoId,
UserName = l.User.Username,
UserNickName = l.User.NickName,
UserEmail = l.User.Email,
l.LicenseKey,
l.StartDate,
l.ExpireDate,
l.Remark
}).ToList();
return ResultData(new { Total = total, Items = items });
}

/// <summary>
/// 授权编辑表单的下拉选项：全部应用与普通用户（供后台 Vue 页面使用）
/// </summary>
/// <returns></returns>
[HttpGet, MyAuthorize]
public IActionResult LicenseOptions()
{
var applications = ApplicationInfoService.GetQuery(a => true).OrderBy(a => a.Name).Select(a => new { a.Id, a.Name }).ToList();
var users = UserInfoService.GetQuery(u => !u.IsAdmin).OrderBy(u => u.NickName).Select(u => new { u.Id, u.Username, u.NickName, u.Email }).ToList();
return ResultData(new { Applications = applications, Users = users });
}

/// <summary>
/// 新建或修改应用
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult SaveApplication([FromBodyOrDefault] ApplicationInfoCommand cmd)
{
if (cmd == null || string.IsNullOrWhiteSpace(cmd.Name))
{
return ResultData(null, false, "应用名称不能为空！");
}

var appKey = string.IsNullOrWhiteSpace(cmd.AppKey)? "app-" + Guid.NewGuid().ToString("N")[..12]: cmd.AppKey.Trim();
if (ApplicationInfoService.Any(a => a.AppKey == appKey && a.Id!= cmd.Id))
{
return ResultData(null, false, $"应用标识已被占用，请更换！");
}

if (cmd.Id > 0)
{
var app = ApplicationInfoService.GetById(cmd.Id)?? throw new NotFoundException("应用不存在！");
app.Name = cmd.Name.Trim();
app.AppKey = appKey;
app.Description = cmd.Description?.Trim();
return ResultData(null, ApplicationInfoService.SaveChanges() > 0, "应用已更新");
}

var added = ApplicationInfoService.AddEntitySaved(new ApplicationInfo
{
Name = cmd.Name.Trim(),
AppKey = appKey,
Description = cmd.Description?.Trim(),
Status = Status.Available
});
return ResultData(null, added!= null, added!= null? "应用已创建": "应用创建失败");
}

/// <summary>
/// 删除应用，名下还有授权时拒绝删除
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult DeleteApplication([FromBodyOrDefault] LicenseIdCommand cmd)
{
if (AppLicenseService.Any(l => l.ApplicationInfoId == cmd.Id))
{
return ResultData(null, false, "该应用名下还有授权记录，请先删除相关授权！");
}

var b = ApplicationInfoService.DeleteById(cmd.Id);
return ResultData(null, b, b? "删除成功！": "删除失败！");
}

/// <summary>
/// 新建或修改授权；修改到期日会重置到期提醒档位，以便按新期限重新提醒
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult SaveLicense([FromBodyOrDefault] AppLicenseCommand cmd)
{
if (cmd == null || cmd.ApplicationInfoId <= 0 || cmd.UserInfoId <= 0)
{
return ResultData(null, false, "请选择应用和用户！");
}

if (cmd.ExpireDate.Date < cmd.StartDate.Date)
{
return ResultData(null, false, "到期日期不能早于生效日期！");
}

if (!ApplicationInfoService.Any(a => a.Id == cmd.ApplicationInfoId) ||!UserInfoService.Any(u => u.Id == cmd.UserInfoId))
{
return ResultData(null, false, "所选应用或用户不存在！");
}

var licenseKey = string.IsNullOrWhiteSpace(cmd.LicenseKey)? GenerateLicenseKey(): cmd.LicenseKey.Trim();
if (AppLicenseService.Any(l => l.LicenseKey == licenseKey && l.Id!= cmd.Id))
{
return ResultData(null, false, $"授权码已被占用，请更换！");
}

if (cmd.Id > 0)
{
var license = AppLicenseService.GetById(cmd.Id)?? throw new NotFoundException("授权不存在！");
if (license.ExpireDate.Date!= cmd.ExpireDate.Date)
{
license.LastReminderStage = -1;
}

license.ApplicationInfoId = cmd.ApplicationInfoId;
license.UserInfoId = cmd.UserInfoId;
license.LicenseKey = licenseKey;
license.StartDate = cmd.StartDate.Date;
license.ExpireDate = cmd.ExpireDate.Date;
license.Remark = cmd.Remark?.Trim();
return ResultData(null, AppLicenseService.SaveChanges() > 0, "授权已更新");
}

var added = AppLicenseService.AddEntitySaved(new AppLicense
{
ApplicationInfoId = cmd.ApplicationInfoId,
UserInfoId = cmd.UserInfoId,
LicenseKey = licenseKey,
StartDate = cmd.StartDate.Date,
ExpireDate = cmd.ExpireDate.Date,
Remark = cmd.Remark?.Trim(),
Status = Status.Available,
LastReminderStage = -1
});
return ResultData(null, added!= null, added!= null? "授权已创建": "授权创建失败");
}

/// <summary>
/// 删除授权
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult DeleteLicense([FromBodyOrDefault] LicenseIdCommand cmd)
{
var b = AppLicenseService.DeleteById(cmd.Id);
return ResultData(null, b, b? "删除成功！": "删除失败！");
}

/// <summary>
/// 续期：从当前到期日（已过期则从今天）起顺延指定月数，并重置到期提醒档位
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult RenewLicense([FromBodyOrDefault] LicenseIdCommand cmd)
{
var license = AppLicenseService.GetById(cmd.Id)?? throw new NotFoundException("授权不存在！");
var months = cmd.Months > 0? cmd.Months: 12;
var baseDate = license.ExpireDate.Date > DateTime.Today? license.ExpireDate.Date: DateTime.Today;
license.ExpireDate = baseDate.AddMonths(months);
license.LastReminderStage = -1;
var b = AppLicenseService.SaveChanges() > 0;
return ResultData(null, b, b? $"已续期 {months} 个月，新到期日：{license.ExpireDate:yyyy-MM-dd}": "续期失败");
}

/// <summary>
/// 立即给被授权用户发送一封到期提醒邮件（用于测试提醒是否可达）
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult SendReminderNow([FromBodyOrDefault] LicenseIdCommand cmd)
{
var license = AppLicenseService.GetQuery(l => l.Id == cmd.Id).Include(l => l.Application).Include(l => l.User).FirstOrDefault()?? throw new NotFoundException("授权不存在！");
if (string.IsNullOrWhiteSpace(license.User?.Email))
{
return ResultData(null, false, "该用户未填写邮箱，无法发送提醒！");
}

var days = Math.Max(0, (license.ExpireDate.Date - DateTime.Today).Days);
var (title, content) = LicenseReminderMail.Build(license, days);
var email = license.User.Email;
BackgroundJob.Enqueue<IMailSender>(sender => sender.Send(title, content, email, ClientIP.ToString()));
license.LastReminderAt = DateTime.Now;
AppLicenseService.SaveChanges();
return ResultData(null, true, $"提醒邮件已发送到 {email}");
}

/// <summary>
/// 生成形如 LIC-XXXXXX-XXXXXX-XXXXXX-XXXXXX 的授权码
/// </summary>
/// <returns></returns>
private static string GenerateLicenseKey()
{
var guid = Guid.NewGuid().ToString("N").ToUpperInvariant();
return $"LIC-{guid[..6]}-{guid[6..12]}-{guid[12..18]}-{guid[18..24]}";
}
}
