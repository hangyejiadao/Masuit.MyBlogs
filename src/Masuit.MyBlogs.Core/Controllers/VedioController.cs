using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Masuit.MyBlogs.Core.Controllers;

[Route("api/vedio")]
public sealed class VedioController(IHttpClientFactory httpClients, VedioMetadataCache cache) : Controller
{
    private static VedioConfiguration Configuration => VedioConfiguration.Read(name => CommonHelper.SystemSettings[name]);

    [HttpGet("config")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult GetConfiguration()
    {
        var config = Configuration;
        return Ok(new
        {
            enabled = config.Enabled,
            configured = config.Enabled && config.HasToken,
            language = config.Language,
            playerSource = config.PlayerSource,
            theme = config.Theme,
            accentColor = config.AccentColor,
            historyEnabled = config.HistoryEnabled
        });
    }

    [HttpGet("tmdb/{**path}")]
    public async Task<ActionResult> Metadata(string path, CancellationToken cancellationToken)
    {
        var config = Configuration;
        Response.Headers.CacheControl = "no-store";
        if (!config.Enabled || !config.HasToken)
            return StatusCode(503, new { error = "not_configured", message = "影视服务暂未配置。" });
        if (!TmdbRequestPolicy.TryCreateUri(path, Request.Query, config.Language, out var uri))
            return BadRequest(new { error = "invalid_request", message = "不支持的影视资料请求。" });

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(config.Token)));
        var key = tokenHash + ":" + uri.AbsoluteUri;
        if (cache.TryGet(key, out var cached)) return Content(cached, "application/json; charset=utf-8");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
        try
        {
            using var response = await httpClients.CreateClient("VedioTmdb").SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return StatusCode(502, new { error = "tmdb_token_invalid", message = "影视资料服务配置异常，请联系管理员。" });
            if (!response.IsSuccessStatusCode)
                return StatusCode(response.StatusCode == HttpStatusCode.NotFound ? 404 : 502,
                    new { error = "tmdb_unavailable", message = "影视资料服务暂不可用。" });
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            cache.Set(key, json);
            return Content(json, "application/json; charset=utf-8");
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "tmdb_unavailable", message = "无法连接影视资料服务。" });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, new { error = "tmdb_timeout", message = "影视资料请求超时。" });
        }
    }
}
