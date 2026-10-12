using Microsoft.Extensions.Caching.Memory;

namespace Masuit.MyBlogs.Core.Common;

public sealed class VedioMetadataCache : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 128 });
    public bool TryGet(string key, out string json) => _cache.TryGetValue(key, out json);
    public void Set(string key, string json) => _cache.Set(key, json, new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        Size = 1
    });
    public void Dispose() => _cache.Dispose();
}
