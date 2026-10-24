namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 应用授权（License），绑定一个应用和一个客户，约定授权使用期限
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
	/// 被授权的客户id
	/// </summary>
	public int CustomerId { get; set; }

	/// <summary>
	/// 被授权的客户
	/// </summary>
	public virtual Customer Customer { get; set; }

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
