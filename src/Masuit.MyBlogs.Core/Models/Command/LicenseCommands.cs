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
/// 价格方案保存参数（Id 为 0 表示新建）
/// </summary>
public class AppPricePlanCommand
{
	/// <summary>
	/// 方案id，0 表示新建
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// 所属应用id
	/// </summary>
	public int ApplicationInfoId { get; set; }

	/// <summary>
	/// 方案名称
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// 方案价格（元）
	/// </summary>
	public decimal Price { get; set; }

	/// <summary>
	/// 有效天数，0 表示永久
	/// </summary>
	public int ValidityDays { get; set; }

	/// <summary>
	/// 排序值，越小越靠前
	/// </summary>
	public int Sort { get; set; }
}

/// <summary>
/// 应用版本保存参数（Id 为 0 表示新建）
/// </summary>
public class AppVersionCommand
{
	/// <summary>
	/// 版本id，0 表示新建
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// 所属应用id
	/// </summary>
	public int ApplicationInfoId { get; set; }

	/// <summary>
	/// 版本号，同一应用下不重复
	/// </summary>
	public string Version { get; set; }

	/// <summary>
	/// 该版本的安装包下载地址
	/// </summary>
	public string PackageUrl { get; set; }

	/// <summary>
	/// 版本日志（更新日志），多行文本
	/// </summary>
	public string ChangeLog { get; set; }

	/// <summary>
	/// 排序值，越小越靠前
	/// </summary>
	public int Sort { get; set; }
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
	/// 被授权的博客用户id（用户管理中的普通用户）
	/// </summary>
	public int UserInfoId { get; set; }

	/// <summary>
	/// 购买的价格方案id，可空；选了方案时成交价取方案价、到期日按方案天数从生效日起算
	/// </summary>
	public int? PricePlanId { get; set; }

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

/// <summary>
/// 授权在线校验参数（供被授权的软件客户端调用）
/// </summary>
public class LicenseVerifyCommand
{
	/// <summary>
	/// 待校验的授权码
	/// </summary>
	public string LicenseKey { get; set; }
}
