namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 第三方单点登录绑定关系，一个网站用户可以绑定多个第三方账号
/// </summary>
[Table("ExternalLogin")]
public class ExternalLogin : BaseEntity
{
	/// <summary>
	/// 关联的网站用户id
	/// </summary>
	public int UserInfoId { get; set; }

	/// <summary>
	/// 关联的网站用户
	/// </summary>
	public virtual UserInfo UserInfo { get; set; }

	/// <summary>
	/// 第三方平台：GitHub 或 Google
	/// </summary>
	[Required, MaxLength(32)]
	public string Provider { get; set; }

	/// <summary>
	/// 第三方平台内的用户唯一标识（NameIdentifier）
	/// </summary>
	[Required, MaxLength(128)]
	public string ProviderKey { get; set; }

	/// <summary>
	/// 第三方账号邮箱，平台未提供时为空
	/// </summary>
	[MaxLength(256)]
	public string Email { get; set; }
}
