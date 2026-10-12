using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.RegularExpressions;

namespace Masuit.MyBlogs.Core.Common;

public static partial class TmdbRequestPolicy
{
    // A fixed metadata-only allowlist prevents this endpoint becoming an account API or open proxy.
    [GeneratedRegex(@"^(configuration|trending/(movie|tv)/(day|week)|search/multi|(movie|tv)/top_rated|movie/[1-9][0-9]*(/(videos|credits|recommendations|similar|release_dates))?|tv/[1-9][0-9]*(/(videos|credits|recommendations|similar|content_ratings|season/[0-9]+))?)$")]
    private static partial Regex AllowedPath();

    public static bool TryCreateUri(string path, IQueryCollection query, string language, out Uri uri)
    {
        uri = null;
        if (path is null || path.Length > 160 || path.Any(char.IsControl) || !AllowedPath().IsMatch(path)) return false;
        var parameters = new Dictionary<string, string> { ["language"] = language };
        foreach (var entry in query)
        {
            if (entry.Value.Count != 1) return false;
            var value = entry.Value[0];
            switch (entry.Key)
            {
                case "page" when int.TryParse(value, out var page) && page is >= 1 and <= 500:
                    parameters[entry.Key] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "query" when path == "search/multi" && !string.IsNullOrWhiteSpace(value) && value.Length <= 200:
                    parameters[entry.Key] = value;
                    break;
                case "append_to_response" when value is not null && value.Split(',').All(part => part is "videos" or "credits" or "recommendations"):
                    parameters[entry.Key] = value;
                    break;
                case "language": // The administrator selects metadata language for the site.
                    break;
                default:
                    return false;
            }
        }

        uri = new Uri(QueryHelpers.AddQueryString("https://api.themoviedb.org/3/" + path, parameters));
        return true;
    }
}
