namespace Masuit.MyBlogs.Core.Models.Enum;

/// <summary>
/// 文章内容类型
/// </summary>
public enum ArticleContentType
{
	/// <summary>
	/// Word 富文本
	/// </summary>
	[Display(Name = "Word 富文本")]
	Word,

	/// <summary>
	/// Markdown
	/// </summary>
	[Display(Name = "Markdown")]
	Markdown
}
