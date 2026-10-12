global using Masuit.MyBlogs.Core.Common;
global using Masuit.MyBlogs.Core.Models.Command;
global using Microsoft.AspNetCore.Mvc;
global using System.Net.Http;
using Masuit.MyBlogs.Core.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.Net;
using System.Text.Json;

var count = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
IQueryCollection Query(params (string Key, string Value)[] values) => new QueryCollection(values.ToDictionary(x => x.Key, x => new StringValues(x.Value)));
var defaults = VedioConfiguration.Read(_ => null);
Check(defaults.Enabled && defaults.HistoryEnabled && !defaults.HasToken && defaults.Theme == "dark", "defaults");
Check(VedioConfiguration.Validate(new()) == null, "valid defaults");
Check(VedioConfiguration.Validate(new() { Theme = "bad" }) != null, "invalid theme");
Check(VedioConfiguration.Validate(new() { Language = null }) != null, "null language");
Check(VedioConfiguration.Validate(new() { TmdbToken = "token with spaces" }) != null, "invalid token");
Check(VedioConfiguration.Validate(new() { TmdbToken = "token", ClearToken = true }) != null, "conflicting clear");
Check(VedioConfiguration.Validate(new() { TmdbToken = new string('a', 4097) }) != null, "token limit");
Check(VedioConfiguration.Read(name => name == "VedioTheme" ? "bad" : name == "VedioEnabled" ? "false" : null).Theme == "dark", "invalid persisted option");
foreach (var path in new[] { "configuration", "trending/movie/week", "trending/tv/day", "search/multi", "movie/top_rated", "tv/top_rated", "movie/123", "movie/123/release_dates", "tv/456/content_ratings", "tv/456/season/0" })
  Check(TmdbRequestPolicy.TryCreateUri(path, Query(), "zh-CN", out _), "allowed " + path);
foreach (var path in new[] { "account", "authentication/token/new", "https://evil.test/", "../account", "movie/0", "movie/123/../../account", "movie/123\n", "tv/1/season/-1" })
  Check(!TmdbRequestPolicy.TryCreateUri(path, Query(), "en-US", out _), "blocked " + path);
foreach (var pair in new[] { ("api_key", "secret"), ("page", "0"), ("page", "501"), ("append_to_response", "account"), ("query", "unexpected") })
  Check(!TmdbRequestPolicy.TryCreateUri("movie/123", Query(pair), "en-US", out _), "blocked query " + pair.Item1);
Check(!TmdbRequestPolicy.TryCreateUri("search/multi", new QueryCollection(new Dictionary<string, StringValues> { ["query"] = new StringValues(["a", "b"]) }), "en-US", out _), "duplicates");
Check(TmdbRequestPolicy.TryCreateUri("search/multi", Query(("query", "中文 & x"), ("language", "de-DE")), "zh-CN", out var uri) && uri.Host == "api.themoviedb.org" && uri.Query.Contains("language=zh-CN") && !uri.Query.Contains("de-DE"), "fixed host and language");

using var cache = new VedioMetadataCache();
var handler = new FakeHandler();
VedioController Controller() => new(new Factory(handler), cache) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
var controller = Controller();
Check((await controller.Metadata("movie/123", default) as ObjectResult)?.StatusCode == 503, "unconfigured metadata");
CommonHelper.SystemSettings["VedioTmdbToken"] = "server-only-secret";
var configJson = JsonSerializer.Serialize((controller.GetConfiguration() as OkObjectResult)?.Value);
Check(configJson.Contains("configured\":true") && !configJson.Contains("secret") && !configJson.Contains("Token"), "public config excludes token");
Check((await controller.Metadata("account", default) as BadRequestObjectResult) != null && handler.Calls == 0, "proxy blocks account before request");
Check((await controller.Metadata("movie/123", default) as ContentResult)?.Content == "{\"id\":123}", "proxy success");
Check(handler.Token == "server-only-secret" && handler.Uri.Host == "api.themoviedb.org", "server authorization");
await controller.Metadata("movie/123", default);
Check(handler.Calls == 1, "cache hit");
CommonHelper.SystemSettings["VedioTmdbToken"] = "rotated-secret";
handler.Status = HttpStatusCode.Unauthorized;
Check((await controller.Metadata("movie/123", default) as ObjectResult)?.StatusCode == 502 && handler.Calls == 2, "rotation invalidates cache and auth failure maps");
handler.Status = HttpStatusCode.NotFound;
Check((await controller.Metadata("movie/999", default) as ObjectResult)?.StatusCode == 404, "not found");
CommonHelper.SystemSettings["VedioEnabled"] = "false";
Check((await controller.Metadata("movie/123", default) as ObjectResult)?.StatusCode == 503, "disabled service");
Console.WriteLine($"Passed {count} API/configuration checks.");

sealed class Factory(FakeHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
sealed class FakeHandler : HttpMessageHandler
{
  public int Calls; public string Token; public Uri Uri; public HttpStatusCode Status = HttpStatusCode.OK;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
  { Calls++; Token = request.Headers.Authorization?.Parameter; Uri = request.RequestUri; return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("{\"id\":123}") }); }
}

namespace Masuit.MyBlogs.Core.Common
{
  // Isolate the real controller/policy from the production database and startup.
  public static class CommonHelper
  {
    public static SettingsStore SystemSettings { get; } = new();
  }
  public sealed class SettingsStore
  {
    private readonly Dictionary<string, string> values = new();
    public string this[string key] { get => values.GetValueOrDefault(key); set => values[key] = value; }
  }
}
