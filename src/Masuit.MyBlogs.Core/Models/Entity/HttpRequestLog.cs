using System.ComponentModel.DataAnnotations;

namespace Masuit.MyBlogs.Core.Models.Entity;

[Table(nameof(HttpRequestLog))]
public sealed class HttpRequestLog
{
    public long Id { get; set; }

    public DateTime Time { get; set; }

    [Required, StringLength(16)]
    public string Method { get; set; }

    [Required, StringLength(4096)]
    public string Path { get; set; }

    [Required]
    public string RequestParameters { get; set; }

    [Required]
    public string ResponseResult { get; set; }

    public int StatusCode { get; set; }

    public string ExceptionInfo { get; set; }

    public bool IsException { get; set; }

    [StringLength(128)]
    public string IP { get; set; }

    [StringLength(1024)]
    public string UserAgent { get; set; }

    [StringLength(128)]
    public string TraceId { get; set; }

    public long DurationMilliseconds { get; set; }
}
