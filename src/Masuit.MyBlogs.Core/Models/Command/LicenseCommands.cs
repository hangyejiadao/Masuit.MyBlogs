namespace Masuit.MyBlogs.Core.Models.Command;

/// <summary>
/// 应用保存参数（Id 为 0 表示新建）
/// </summary>
public class ApplicationInfoCommand
{
	/// <summary>
	/// 应用id，0 表示新建
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// 应用名称
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// 应用唯一标识，可空，空时自动生成
	/// </summary>
	public string AppKey { get; set; }

	/// <summary>
	/// 应用说明
	/// </summary>
	public string Description { get; set; }
}

/// <summary>
/// 客户保存参数（Id 为 0 表示新建）
/// </summary>
public class CustomerCommand
{
	/// <summary>
	/// 客户id，0 表示新建
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// 客户名称
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// 联系人姓名
	/// </summary>
	public string ContactName { get; set; }

	/// <summary>
	/// 客户邮箱，用于接收授权到期提醒
	/// </summary>
	public string Email { get; set; }

	/// <summary>
	/// 联系电话
	/// </summary>
	public string Phone { get; set; }

	/// <summary>
	/// 备注
	/// </summary>
	public string Remark { get; set; }
}

/// <summary>
/// 授权保存参数（Id 为 0 表示新建）
/// </summary>
public class AppLicenseCommand
{
	/// <summary>
	/// 授权id，0 表示新建
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// 被授权的应用id
	/// </summary>
	public int ApplicationInfoId { get; set; }

	/// <summary>
	/// 被授权的客户id
	/// </summary>
	public int CustomerId { get; set; }

	/// <summary>
	/// 授权码，可空，空时自动生成
	/// </summary>
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
	public string Remark { get; set; }
}

/// <summary>
/// 按 id 操作的通用参数（删除、续期、立即提醒等）
/// </summary>
public class LicenseIdCommand
{
	/// <summary>
	/// 目标记录id
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// 续期月数，仅续期操作使用
	/// </summary>
	public int Months { get; set; }
}
