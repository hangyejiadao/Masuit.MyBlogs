namespace Masuit.MyBlogs.Core.Common;

/// <summary>
/// 授权到期提醒邮件内容构建，供定时任务和后台手动提醒共用
/// </summary>
public static class LicenseReminderMail
{
/// <summary>
/// 构建提醒邮件的标题和正文
/// </summary>
/// <param name="license">授权记录，需已加载 Application 和 Customer</param>
/// <param name="daysRemaining">距离到期的天数，0 表示今天到期</param>
/// <returns>邮件标题和 HTML 正文</returns>
public static (string Title, string Content) Build(AppLicense license, int daysRemaining)
{
var site = CommonHelper.SystemSettings.GetOrAdd("Title", "博客");
var app = license.Application?.Name?? string.Empty;
var customer = license.Customer?.Name?? string.Empty;
var title = daysRemaining > 0? $"授权即将到期提醒（剩余 {daysRemaining} 天）": $"授权今日到期提醒";
var when = daysRemaining > 0? $"将于 <b>{license.ExpireDate:yyyy-MM-dd}</b> 到期（剩余 {daysRemaining} 天）": $"已于 <b>{license.ExpireDate:yyyy-MM-dd}</b> 到期";
var content = $"""
<p>尊敬的 {customer}，您好：</p>
<p>您的应用 <b>{app}</b> 授权{when}。</p>
<p>授权码：{license.LicenseKey}<br />授权期限：{license.StartDate:yyyy-MM-dd} 至 {license.ExpireDate:yyyy-MM-dd}</p>
<p>为避免授权到期后影响应用正常使用，请及时联系我们办理续期。</p>
<p>—— {site}</p>
""";
return (title, content);
}
}
