namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 应用版本：一个应用可发布多个版本，每个版本对应一个自己的安装包，并带一份版本日志
/// </summary>
[Table("AppVersion")]
public class AppVersion : BaseEntity
{
	/// <summary>
	/// 所属应用id
	/// </summary>
	public int ApplicationInfoId { get; set; }

	/// <summary>
	/// 所属应用
	/// </summary>
	public virtual ApplicationInfo Application { get; set; }

	/// <summary>
	/// 版本号，如 1.2.0，同一应用下不重复
	/// </summary>
	[Required(ErrorMessage = "版本号不能为空！"), MaxLength(32)]
	public string Version { get; set; }

	/// <summary>
	/// 该版本的安装包下载地址，每个版本必须对应一个安装包（先只存地址，安装包文件由外部托管）
	/// </summary>
	[Required(ErrorMessage = "安装包地址不能为空！"), MaxLength(1024)]
	public string PackageUrl { get; set; }

	/// <summary>
	/// 版本日志（更新日志），多行文本
	/// </summary>
	[MaxLength(4000)]
	public string ChangeLog { get; set; }

	/// <summary>
	/// 排序值，越小越靠前
	/// </summary>
	public int Sort { get; set; }
}
