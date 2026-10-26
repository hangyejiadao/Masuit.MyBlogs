using Hangfire;
using Masuit.MyBlogs.Core.Common;
using Masuit.MyBlogs.Core.Common.Mails;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.Tools.AspNetCore.ModelBinder;

namespace Masuit.MyBlogs.Core.Controllers;

/// <summary>
/// 应用与授权（License）管理，仅管理员可用：
/// 维护应用和客户，签发绑定客户的授权并约定使用期限，支持续期和手动发送到期提醒。
/// </summary>
public sealed class LicenseController: BaseController
{
/// <summary>
/// 应用服务
/// </summary>
public IApplicationInfoService ApplicationInfoService { get; set;}

/// <summary>
/// 客户服务
/// </summary>
public ICustomerService CustomerService { get; set;}

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
var licenses = AppLicenseService.GetQuery(l => true).Include(l => l.Application).Include(l => l.Customer).OrderBy(l => l.ExpireDate).ToList();
ViewBag.Applications = ApplicationInfoService.GetQuery(a => true).OrderBy(a => a.Name).ToList();
ViewBag.Customers = CustomerService.GetQuery(c => true).OrderBy(c => c.Name).ToList();
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
/// 客户管理页
/// </summary>
/// <returns></returns>
[MyAuthorize]
public IActionResult Customers()
{
var customers = CustomerService.GetQuery(c => true).Include(c => c.Licenses).OrderBy(c => c.Name).ToList();
return View(customers);
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
/// 客户分页查询（供后台 Vue 页面使用），可按名称、联系人或邮箱关键字筛选
/// </summary>
/// <param name="page">页码，从 1 开始</param>
/// <param name="size">每页条数，最大 100</param>
/// <param name="keyword">名称、联系人或邮箱关键字，可空</param>
/// <returns></returns>
[HttpGet, MyAuthorize]
public IActionResult CustomerPage(int page = 1, int size = 10, string keyword = null)
{
page = Math.Max(1, page);
size = Math.Clamp(size, 1, 100);
var query = CustomerService.GetQuery(c => true);
if (!string.IsNullOrWhiteSpace(keyword))
{
var kw = keyword.Trim();
query = query.Where(c => c.Name.Contains(kw) || (c.ContactName != null && c.ContactName.Contains(kw)) || (c.Email != null && c.Email.Contains(kw)));
}

var total = query.Count();
var items = query.OrderBy(c => c.Name).Skip((page - 1) * size).Take(size).Select(c => new
{
c.Id,
c.Name,
c.ContactName,
c.Email,
c.Phone,
c.Remark,
LicenseCount = c.Licenses.Count
}).ToList();
return ResultData(new { Total = total, Items = items });
}

/// <summary>
/// 授权分页查询（供后台 Vue 页面使用），按到期时间升序，可按授权码、应用名或客户名关键字筛选
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
query = query.Where(l => l.LicenseKey.Contains(kw) || l.Application.Name.Contains(kw) || l.Customer.Name.Contains(kw));
}

var total = query.Count();
var items = query.OrderBy(l => l.ExpireDate).Skip((page - 1) * size).Take(size).Select(l => new
{
l.Id,
l.ApplicationInfoId,
ApplicationName = l.Application.Name,
l.CustomerId,
CustomerName = l.Customer.Name,
CustomerEmail = l.Customer.Email,
l.LicenseKey,
l.StartDate,
l.ExpireDate,
l.Remark
}).ToList();
return ResultData(new { Total = total, Items = items });
}

/// <summary>
/// 授权编辑表单的下拉选项：全部应用与客户（供后台 Vue 页面使用）
/// </summary>
/// <returns></returns>
[HttpGet, MyAuthorize]
public IActionResult LicenseOptions()
{
var applications = ApplicationInfoService.GetQuery(a => true).OrderBy(a => a.Name).Select(a => new { a.Id, a.Name }).ToList();
var customers = CustomerService.GetQuery(c => true).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.Email }).ToList();
return ResultData(new { Applications = applications, Customers = customers });
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
/// 新建或修改客户
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult SaveCustomer([FromBodyOrDefault] CustomerCommand cmd)
{
if (cmd == null || string.IsNullOrWhiteSpace(cmd.Name))
{
return ResultData(null, false, "客户名称不能为空！");
}

if (!string.IsNullOrWhiteSpace(cmd.Email) &&!cmd.Email.Contains('@'))
{
return ResultData(null, false, "客户邮箱格式不正确！");
}

if (cmd.Id > 0)
{
var customer = CustomerService.GetById(cmd.Id)?? throw new NotFoundException("客户不存在！");
customer.Name = cmd.Name.Trim();
customer.ContactName = cmd.ContactName?.Trim();
customer.Email = cmd.Email?.Trim();
customer.Phone = cmd.Phone?.Trim();
customer.Remark = cmd.Remark?.Trim();
return ResultData(null, CustomerService.SaveChanges() > 0, "客户已更新");
}

var added = CustomerService.AddEntitySaved(new Customer
{
Name = cmd.Name.Trim(),
ContactName = cmd.ContactName?.Trim(),
Email = cmd.Email?.Trim(),
Phone = cmd.Phone?.Trim(),
Remark = cmd.Remark?.Trim(),
Status = Status.Available
});
return ResultData(null, added!= null, added!= null? "客户已创建": "客户创建失败");
}

/// <summary>
/// 删除客户，名下还有授权时拒绝删除
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult DeleteCustomer([FromBodyOrDefault] LicenseIdCommand cmd)
{
if (AppLicenseService.Any(l => l.CustomerId == cmd.Id))
{
return ResultData(null, false, "该客户名下还有授权记录，请先删除相关授权！");
}

var b = CustomerService.DeleteById(cmd.Id);
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
if (cmd == null || cmd.ApplicationInfoId <= 0 || cmd.CustomerId <= 0)
{
return ResultData(null, false, "请选择应用和客户！");
}

if (cmd.ExpireDate.Date < cmd.StartDate.Date)
{
return ResultData(null, false, "到期日期不能早于生效日期！");
}

if (!ApplicationInfoService.Any(a => a.Id == cmd.ApplicationInfoId) ||!CustomerService.Any(c => c.Id == cmd.CustomerId))
{
return ResultData(null, false, "所选应用或客户不存在！");
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
license.CustomerId = cmd.CustomerId;
license.LicenseKey = licenseKey;
license.StartDate = cmd.StartDate.Date;
license.ExpireDate = cmd.ExpireDate.Date;
license.Remark = cmd.Remark?.Trim();
return ResultData(null, AppLicenseService.SaveChanges() > 0, "授权已更新");
}

var added = AppLicenseService.AddEntitySaved(new AppLicense
{
ApplicationInfoId = cmd.ApplicationInfoId,
CustomerId = cmd.CustomerId,
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
/// 立即给客户发送一封到期提醒邮件（用于测试提醒是否可达）
/// </summary>
/// <param name="cmd"></param>
/// <returns></returns>
[HttpPost, MyAuthorize]
public IActionResult SendReminderNow([FromBodyOrDefault] LicenseIdCommand cmd)
{
var license = AppLicenseService.GetQuery(l => l.Id == cmd.Id).Include(l => l.Application).Include(l => l.Customer).FirstOrDefault()?? throw new NotFoundException("授权不存在！");
if (string.IsNullOrWhiteSpace(license.Customer?.Email))
{
return ResultData(null, false, "该客户未填写邮箱，无法发送提醒！");
}

var days = Math.Max(0, (license.ExpireDate.Date - DateTime.Today).Days);
var (title, content) = LicenseReminderMail.Build(license, days);
var email = license.Customer.Email;
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
