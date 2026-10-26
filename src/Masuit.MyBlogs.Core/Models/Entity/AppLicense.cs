namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 应用授权（License），绑定一个应用和一个博客用户（即用户管理中的普通用户），约定授权使用期限
/// </summary>
[Table("AppLicense")]
public class AppLicense : BaseEntity
{
	/// <summary>
	/// 被授权的应用id
	/// </summary>
	public int ApplicationInfoId { get; set; }

	/// <summary>
	/// 被授权的应用
	/// </summary>
	public virtual ApplicationInfo Application { get; set; }

	/// <summary>
	/// 被授权的博客用户id
	/// </summary>
	public int UserInfoId { get; set; }

	/// <summary>
	/// 被授权的博客用户，到期提醒发到该用户的邮箱
	/// </summary>
	public virtual UserInfo User { get; set; }

	/// <summary>
	/// 购买的价格方案id，可空（手工签发、未选方案时为空）
	/// </summary>
	public int? PricePlanId { get; set; }

	/// <summary>
	/// 购买的价格方案
	/// </summary>
	public virtual AppPricePlan PricePlan { get; set; }

	/// <summary>
	/// 成交价快照：签发时的方案价格，方案事后改价不影响已签发授权；未选方案时为空
	/// </summary>
	public decimal? PaidPrice { get; set; }

	/// <summary>
	/// 授权码，创建时不填则自动生成
	/// </summary>
	[Required, MaxLength(64)]
	public string LicenseKey { get; set; }

	/// <summary>
	/// 授权生效日期
	/// </summary>
	public DateTime StartDate { get; set; }

	/// <summary>
	/// 授权到期日期
	/// </summary>
	public DateTime ExpireDate { get; set; }

	/// <summary>
	/// 备注
	/// </summary>
	[MaxLength(512)]
	public string Remark { get; set; }

	/// <summary>
	/// 最近一次到期提醒的剩余天数档位（30/15/7/1/0），-1 表示尚未提醒过；续期后重置为 -1
	/// </summary>
	public int LastReminderStage { get; set; } = -1;

	/// <summary>
	/// 最近一次到期提醒的发送时间
	/// </summary>
	public DateTime? LastReminderAt { get; set; }
}
