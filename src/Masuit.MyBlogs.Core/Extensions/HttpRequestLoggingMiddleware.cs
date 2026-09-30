using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Masuit.MyBlogs.Core.Extensions;

public sealed class HttpRequestLoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory, ILogger<HttpRequestLoggingMiddleware> logger)
{
    private const string LogStateItemKey = "HttpRequestLog.State";
    public const string ExceptionInfoItemKey = "HttpRequestLog.ExceptionInfo";

    private const int MaxRequestBodyBytes = 8192;
    private const int MaxResponseBodyBytes = 4096;
    private const int MaxExceptionInfoLength = 8192;
    private static readonly Regex SensitiveValuePattern = new(
        @"(?i)(password|passwd|pwd|token|secret|authorization|cookie|api[_-]?key|credential)([""']?\s*[:=]\s*[""']?)[^,\s""';&]+",
        RegexOptions.Compiled);

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items[LogStateItemKey] is not RequestLogState state)
        {
            var request = context.Request;
            state = new RequestLogState(
                Stopwatch.GetTimestamp(),
                DateTime.Now,
                request.Method,
                request.Path.Value ?? "/",
                await GetRequestParametersAsync(request),
                context.Connection.RemoteIpAddress?.ToString(),
                Limit(request.Headers.UserAgent.ToString(), 1024),
                Limit(context.TraceIdentifier, 128));
            context.Items[LogStateItemKey] = state;
            context.Response.OnCompleted(() => PersistAsync(context, state));
        }

        var originalResponseBody = context.Response.Body;
        await using var capture = new ResponseCaptureStream(originalResponseBody, MaxResponseBodyBytes);
        context.Response.Body = capture;

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            state.ExceptionInfo = exception.ToString();
            throw;
        }
        finally
        {
            context.Response.Body = originalResponseBody;
            state.ResponseResult = GetResponseResult(context.Response, capture);
            state.ExceptionInfo = context.Items[ExceptionInfoItemKey] as string ?? state.ExceptionInfo;
        }
    }

    private async Task PersistAsync(HttpContext context, RequestLogState state)
    {
        var statusCode = context.Response.StatusCode;
        var exceptionInfo = state.ExceptionInfo is null ? null : SensitiveValuePattern.Replace(state.ExceptionInfo, "$1$2[REDACTED]");
        var isException = exceptionInfo is not null || statusCode >= StatusCodes.Status400BadRequest;
        if (isException && string.IsNullOrWhiteSpace(exceptionInfo))
        {
            exceptionInfo = $"请求返回 HTTP {statusCode}。";
        }

        if (exceptionInfo?.Length > MaxExceptionInfoLength)
        {
            exceptionInfo = exceptionInfo[..MaxExceptionInfoLength];
        }

        var logEntry = new HttpRequestLog
        {
            Time = state.Time,
            Method = state.Method,
            Path = Limit(state.Path, 4096),
            RequestParameters = state.RequestParameters,
            ResponseResult = state.ResponseResult,
            StatusCode = statusCode,
            ExceptionInfo = exceptionInfo,
            IsException = isException,
            IP = state.IP,
            UserAgent = state.UserAgent,
            TraceId = state.TraceId,
            DurationMilliseconds = (long)Stopwatch.GetElapsedTime(state.Started).TotalMilliseconds
        };

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LoggerDbContext>();
            dbContext.Set<HttpRequestLog>().Add(logEntry);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to persist HTTP request log for {Method} {Path}", state.Method, logEntry.Path);
        }
    }

    private static async Task<string> GetRequestParametersAsync(HttpRequest request)
    {
        var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in request.Query)
        {
            parameters[pair.Key] = IsSensitive(pair.Key) ? "[REDACTED]" : pair.Value.ToArray();
        }

        var contentType = request.ContentType ?? "";
        if (request.ContentLength == 0 ||
            !(contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
              contentType.Contains("+json", StringComparison.OrdinalIgnoreCase) ||
              contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)))
        {
            if (request.ContentLength > 0)
            {
                parameters["body"] = "[omitted: unsupported content type]";
            }

            return JsonSerializer.Serialize(parameters);
        }

        request.EnableBuffering();
        var originalPosition = request.Body.CanSeek ? request.Body.Position : 0;
        try
        {
            var bytes = new byte[MaxRequestBodyBytes + 1];
            var totalRead = 0;
            while (totalRead < bytes.Length)
            {
                var read = await request.Body.ReadAsync(bytes.AsMemory(totalRead, bytes.Length - totalRead), CancellationToken.None);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead > MaxRequestBodyBytes)
            {
                parameters["body"] = "[omitted: request body exceeds 8 KB]";
            }
            else if (totalRead > 0)
            {
                var body = Encoding.UTF8.GetString(bytes, 0, totalRead);
                if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Contains("+json", StringComparison.OrdinalIgnoreCase))
                {
                    parameters["body"] = RedactJson(body);
                }
                else
                {
                    parameters["body"] = ParseForm(body);
                }
            }
        }
        finally
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = originalPosition;
            }
        }

        return JsonSerializer.Serialize(parameters);
    }

    private static Dictionary<string, string[]> ParseForm(string body)
    {
        return QueryHelpers.ParseQuery(body).ToDictionary(
            pair => pair.Key,
            pair => IsSensitive(pair.Key) ? ["[REDACTED]"] : pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string GetResponseResult(HttpResponse response, ResponseCaptureStream capture)
    {
        var statusCode = response.StatusCode;
        var contentType = response.ContentType ?? "";
        if (response.Headers.ContainsKey("Content-Encoding"))
        {
            return $"HTTP {statusCode}; response body omitted (compressed {contentType})";
        }

        if (capture.CapturedLength == 0)
        {
            return $"{statusCode} {contentType}".TrimEnd();
        }

        if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            return "[SSE stream]";
        }

        if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return $"HTTP {statusCode}; response body omitted ({contentType})";
        }

        var text = Encoding.UTF8.GetString(capture.GetCapturedBytes());
        if (capture.Truncated)
        {
            return "[omitted: JSON response exceeds 4 KB]";
        }

        return RedactJson(text);
    }

    private static string RedactJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject and not JsonArray)
            {
                return "[omitted: scalar JSON payload]";
            }

            RedactJsonNode(node);
            return node?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            return "[omitted: invalid JSON]";
        }
    }

    private static void RedactJsonNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (IsSensitive(property.Key))
                {
                    jsonObject[property.Key] = "[REDACTED]";
                }
                else if (property.Value is not null)
                {
                    RedactJsonNode(property.Value);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    RedactJsonNode(item);
                }
            }
        }
    }

    private static bool IsSensitive(string key)
    {
        return key.Contains("password", StringComparison.OrdinalIgnoreCase)
               || key.Contains("passwd", StringComparison.OrdinalIgnoreCase)
               || key.Contains("pwd", StringComparison.OrdinalIgnoreCase)
               || key.Contains("token", StringComparison.OrdinalIgnoreCase)
               || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
               || key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
               || key.Contains("cookie", StringComparison.OrdinalIgnoreCase)
               || key.Contains("apikey", StringComparison.OrdinalIgnoreCase)
               || key.Contains("api_key", StringComparison.OrdinalIgnoreCase);
    }

    private static string Limit(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed class RequestLogState(long started, DateTime time, string method, string path, string requestParameters, string ip, string userAgent, string traceId)
    {
        public long Started { get; } = started;
        public DateTime Time { get; } = time;
        public string Method { get; } = method;
        public string Path { get; } = path;
        public string RequestParameters { get; } = requestParameters;
        public string IP { get; } = ip;
        public string UserAgent { get; } = userAgent;
        public string TraceId { get; } = traceId;
        public string ResponseResult { get; set; } = "";
        public string ExceptionInfo { get; set; }
    }

    private sealed class ResponseCaptureStream(Stream inner, int maxBytes) : Stream
    {
        private readonly MemoryStream _capture = new();

        public int CapturedLength => (int)_capture.Length;
        public bool Truncated { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] GetCapturedBytes() => _capture.ToArray();

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            Capture(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            Capture(buffer);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            Capture(buffer.AsSpan(offset, count));
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            Capture(buffer.Span);
        }

        private void Capture(ReadOnlySpan<byte> bytes)
        {
            var remaining = maxBytes - (int)_capture.Length;
            if (remaining > 0)
            {
                _capture.Write(bytes[..Math.Min(bytes.Length, remaining)]);
            }

            Truncated |= bytes.Length > remaining;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _capture.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _capture.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
