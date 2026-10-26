namespace Masuit.MyBlogs.Core.Models.Entity;

/// <summary>
/// 应用价格方案：一个应用可配置多个「价格 + 有效天数」方案，签发授权时选方案自动定价、自动算到期日
/// </summary>
[Table("AppPricePlan")]
public class AppPricePlan : BaseEntity
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
	/// 方案名称，如「月版」「年版」「永久版」
	/// </summary>
	[Required(ErrorMessage = "方案名称不能为空！"), MaxLength(64)]
	public string Name { get; set; }

	/// <summary>
	/// 方案价格（元）
	/// </summary>
	public decimal Price { get; set; }

	/// <summary>
	/// 有效天数，0 表示永久有效
	/// </summary>
	public int ValidityDays { get; set; }

	/// <summary>
	/// 排序值，越小越靠前
	/// </summary>
	public int Sort { get; set; }
}
