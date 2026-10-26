namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 受授权管理的应用
/// </summary>
[Table("ApplicationInfo")]
public class ApplicationInfo : BaseEntity
{
	/// <summary>
	/// 应用名称
	/// </summary>
	[Required(ErrorMessage = "应用名称不能为空！"), MaxLength(128)]
	public string Name { get; set; }

	/// <summary>
	/// 应用唯一标识，创建时不填则自动生成
	/// </summary>
	[Required, MaxLength(64)]
	public string AppKey { get; set; }

	/// <summary>
	/// 应用说明
	/// </summary>
	[MaxLength(512)]
	public string Description { get; set; }

	/// <summary>
	/// 当前版本号，如 1.2.0
	/// </summary>
	[MaxLength(32)]
	public string Version { get; set; }

	/// <summary>
	/// 安装包下载地址（先只存地址，安装包文件由外部托管）
	/// </summary>
	[MaxLength(1024)]
	public string PackageUrl { get; set; }

	/// <summary>
	/// 该应用的价格方案
	/// </summary>
	public virtual ICollection<AppPricePlan> PricePlans { get; set; }

	/// <summary>
	/// 该应用签发的授权
	/// </summary>
	public virtual ICollection<AppLicense> Licenses { get; set; }
}
