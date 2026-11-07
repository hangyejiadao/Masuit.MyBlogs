namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 授权与 MT5 交易账号的绑定：一条授权可绑定多个交易账号，被授权软件凭交易账号在线校验授权到期时间。
/// 同一交易账号可被不同应用的授权分别绑定；同一应用下也可绑到多条授权，校验时取到期最晚的一条。
/// </summary>
[Table("LicenseAccountBinding")]
public class LicenseAccountBinding : BaseEntity
{
	/// <summary>
	/// 所属授权id
	/// </summary>
	public int AppLicenseId { get; set; }

	/// <summary>
	/// 所属授权
	/// </summary>
	public virtual AppLicense License { get; set; }

	/// <summary>
	/// MT5 交易账号（登录名），以字符串保存避免大账号数值溢出，只允许数字
	/// </summary>
	[Required(ErrorMessage = "交易账号不能为空！"), MaxLength(64)]
	public string Account { get; set; }
}
