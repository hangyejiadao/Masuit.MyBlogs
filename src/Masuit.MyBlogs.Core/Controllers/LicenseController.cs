using System.Security.Cryptography;
using System.Text;
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
/// 价格方案服务
/// </summary>
public IAppPricePlanService AppPricePlanService { get; set;}

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
a.Version,
a.PackageUrl,
LicenseCount = a.Licenses.Count,
PlanCount = a.PricePlans.Count
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
l.PricePlanId,
PlanName = l.PricePlan == null? null: l.PricePlan.Name,
PlanValidityDays = l.PricePlan == null? (int?)null: l.PricePlan.ValidityDays,
l.PaidPrice,
l.LicenseKey,
l.StartDate,
l.ExpireDate,
l.Remark
}).ToList();
return ResultData(new { Total = total, Items = items });
}

/// <summary>
/// 授权编辑表单的下拉选项：全部应用、普通用户与全部价格方案（方案带所属应用id，前端按应用过滤；供后台 Vue 页面使用）
/// </summary>
/// <returns></returns>
[HttpGet, MyAuthorize]
public IActionResult LicenseOptions()
{
var applications = ApplicationInfoService.GetQuery(a => true).OrderBy(a => a.Name).Select(a => new { a.Id, a.Name }).ToList();
var users = UserInfoService.GetQuery(u => !u.IsAdmin).OrderBy(u => u.NickName).Select(u => new { u.Id, u.Username, u.NickName, u.Email }).ToList();
var plans = AppPricePlanService.GetQuery(p => true).OrderBy(p => p.ApplicationInfoId).ThenBy(p => p.Sort).ThenBy(p => p.Id).Select(p => new { p.Id, p.ApplicationInfoId, p.Name, p.Price, p.ValidityDays }).ToList();
return ResultData(new { Applications = applications, Users = users, Plans = plans });
}

/// <summary>
/// 某应用的全部价格方案（供后台 Vue 的应用价格方案管理弹窗使用），按排序值升序
/// </summary>
/// <param name="applicationId">应用id</param>
/// <returns></returns>
[HttpGet, MyAuthorize]
public IActionResult PricePlanList(int applicationId)
{
var items = AppPricePlanService.GetQuery(p => p.ApplicationInfoId == applicationId).OrderBy(p => p.Sort).ThenBy(p => p.Id).Select(p => new
{
p.Id,
p.ApplicationInfoId,
p.Name,
p.Price,
p.ValidityDays,
p.Sort
}).ToList();
return ResultData(items);
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
app.Version = cmd.Version?.Trim();
app.PackageUrl = cmd.PackageUrl?.Trim();
return ResultData(null, ApplicationInfoService.SaveChanges() > 0, "应用已更新");
}

var added = ApplicationInfoService.AddEntitySaved(new ApplicationInfo
{
Name = cmd.Name.Trim(),
AppKey = appKey,
Description = cmd.Description?.Trim(),
Version = cmd.Version?.Trim(),
PackageUrl = cmd.PackageUrl?.Trim(),
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
/// 新建或修改价格方案；方案价格只影响之后签发的授权，已签发授权保存的是成交价快照
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult SavePricePlan([FromBodyOrDefault] AppPricePlanCommand cmd)
{
if (cmd == null || cmd.ApplicationInfoId <= 0 || string.IsNullOrWhiteSpace(cmd.Name))
{
return ResultData(null, false, "请选择应用并填写方案名称！");
}

if (cmd.Price < 0)
{
return ResultData(null, false, "方案价格不能为负数！");
}

if (cmd.ValidityDays < 0)
{
return ResultData(null, false, "有效天数不能为负数，0 表示永久！");
}

if (!ApplicationInfoService.Any(a => a.Id == cmd.ApplicationInfoId))
{
return ResultData(null, false, "所选应用不存在！");
}

if (cmd.Id > 0)
{
var plan = AppPricePlanService.GetById(cmd.Id)?? throw new NotFoundException("价格方案不存在！");
plan.ApplicationInfoId = cmd.ApplicationInfoId;
plan.Name = cmd.Name.Trim();
plan.Price = cmd.Price;
plan.ValidityDays = cmd.ValidityDays;
plan.Sort = cmd.Sort;
return ResultData(null, AppPricePlanService.SaveChanges() > 0, "价格方案已更新");
}

var added = AppPricePlanService.AddEntitySaved(new AppPricePlan
{
ApplicationInfoId = cmd.ApplicationInfoId,
Name = cmd.Name.Trim(),
Price = cmd.Price,
ValidityDays = cmd.ValidityDays,
Sort = cmd.Sort,
Status = Status.Available
});
return ResultData(null, added!= null, added!= null? "价格方案已创建": "价格方案创建失败");
}

/// <summary>
/// 删除价格方案，已有授权引用该方案时拒绝删除
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult DeletePricePlan([FromBodyOrDefault] LicenseIdCommand cmd)
{
if (AppLicenseService.Any(l => l.PricePlanId == cmd.Id))
{
return ResultData(null, false, "该方案已有授权在用，请先删除或改绑相关授权！");
}

var b = AppPricePlanService.DeleteById(cmd.Id);
return ResultData(null, b, b? "删除成功！": "删除失败！");
}

/// <summary>
/// 新建或修改授权；修改到期日会重置到期提醒档位，以便按新期限重新提醒。
/// 选了价格方案时：成交价取方案价快照，到期日由生效日加方案天数得出（永久方案按生效日加 100 年落库），提交的到期日被忽略
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

var owner = UserInfoService.GetById(cmd.UserInfoId);
if (!ApplicationInfoService.Any(a => a.Id == cmd.ApplicationInfoId) || owner == null)
{
return ResultData(null, false, "所选应用或用户不存在！");
}

AppPricePlan plan = null;
if (cmd.PricePlanId is > 0)
{
plan = AppPricePlanService.GetById(cmd.PricePlanId.Value);
if (plan == null || plan.ApplicationInfoId != cmd.ApplicationInfoId)
{
return ResultData(null, false, "所选价格方案不存在或不属于该应用！");
}
}

var startDate = cmd.StartDate.Date;
var expireDate = plan == null? cmd.ExpireDate.Date: plan.ValidityDays > 0? startDate.AddDays(plan.ValidityDays): startDate.AddYears(100);
if (expireDate < startDate)
{
return ResultData(null, false, "到期日期不能早于生效日期！");
}

var licenseKey = string.IsNullOrWhiteSpace(cmd.LicenseKey)? GenerateUniqueLicenseKey(owner): cmd.LicenseKey.Trim();
if (AppLicenseService.Any(l => l.LicenseKey == licenseKey && l.Id!= cmd.Id))
{
return ResultData(null, false, $"授权码已被占用，请更换！");
}

if (cmd.Id > 0)
{
var license = AppLicenseService.GetById(cmd.Id)?? throw new NotFoundException("授权不存在！");
if (license.ExpireDate.Date!= expireDate)
{
license.LastReminderStage = -1;
}

license.ApplicationInfoId = cmd.ApplicationInfoId;
license.UserInfoId = cmd.UserInfoId;
license.PricePlanId = plan?.Id;
license.PaidPrice = plan?.Price;
license.LicenseKey = licenseKey;
license.StartDate = startDate;
license.ExpireDate = expireDate;
license.Remark = cmd.Remark?.Trim();
return ResultData(null, AppLicenseService.SaveChanges() > 0, "授权已更新");
}

var added = AppLicenseService.AddEntitySaved(new AppLicense
{
ApplicationInfoId = cmd.ApplicationInfoId,
UserInfoId = cmd.UserInfoId,
PricePlanId = plan?.Id,
PaidPrice = plan?.Price,
LicenseKey = licenseKey,
StartDate = startDate,
ExpireDate = expireDate,
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
/// 为被授权用户生成授权码：以「用户邮箱（无邮箱时用用户名）+ 当前毫秒时间戳 + 随机数」为原文做 SHA256，
/// 得到 64 位十六进制串；毫秒时间戳与随机数保证并发下每次生成的原文都不同，
/// 生成后再查库确认未被占用，配合数据库 LicenseKey 唯一索引兜底，并发签发也不会重复
/// </summary>
/// <param name="user">被授权的博客用户</param>
/// <returns></returns>
private string GenerateUniqueLicenseKey(UserInfo user)
{
var identity = string.IsNullOrWhiteSpace(user.Email)? user.Username: user.Email.Trim();
string key;
do
{
var material = $"{identity}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|{Guid.NewGuid():N}";
key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
} while (AppLicenseService.Any(l => l.LicenseKey == key));
return key;
}
}
