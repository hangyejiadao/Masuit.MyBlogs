using Masuit.Tools.Core.Validator;

namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 客户，License 的授权对象，到期提醒邮件发送到客户邮箱
/// </summary>
[Table("Customer")]
public class Customer : BaseEntity
{
	/// <summary>
	/// 客户名称（公司或个人）
	/// </summary>
	[Required(ErrorMessage = "客户名称不能为空！"), MaxLength(128)]
	public string Name { get; set; }

	/// <summary>
	/// 联系人姓名
	/// </summary>
	[MaxLength(64)]
	public string ContactName { get; set; }

	/// <summary>
	/// 客户邮箱，用于接收授权到期提醒
	/// </summary>
	[IsEmail, MaxLength(256)]
	public string Email { get; set; }

	/// <summary>
	/// 联系电话
	/// </summary>
	[MaxLength(32)]
	public string Phone { get; set; }

	/// <summary>
	/// 备注
	/// </summary>
	[MaxLength(512)]
	public string Remark { get; set; }

	/// <summary>
	/// 该客户持有的授权
	/// </summary>
	public virtual ICollection<AppLicense> Licenses { get; set; }
}
