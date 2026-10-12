namespace Masuit.MyBlogs.Core.Common;

public sealed class VedioConfiguration
{
    public const string Prefix = "Vedio";
    public static readonly string[] Languages = ["zh-CN", "zh-TW", "en-US", "ja-JP", "de-DE"];
    public static readonly string[] PlayerSources = ["videasy", "vidsrc", "vidking"];
    public static readonly string[] Themes = ["dark", "amoled", "mocha", "slate", "light"];
    public static readonly string[] Accents = ["red", "blue", "purple", "green", "orange", "pink"];

    public bool Enabled { get; init; }
    public string Token { get; init; }
    public string Language { get; init; }
    public string PlayerSource { get; init; }
    public string Theme { get; init; }
    public string AccentColor { get; init; }
    public bool HistoryEnabled { get; init; }
    public bool HasToken => !string.IsNullOrWhiteSpace(Token);

    public static VedioConfiguration Read(Func<string, string> read)
    {
        string Option(string name, string fallback, string[] allowed)
        {
            var value = read(Prefix + name);
            return allowed.Contains(value) ? value : fallback;
        }

        bool Flag(string name) => !bool.TryParse(read(Prefix + name), out var value) || value;
        return new VedioConfiguration
        {
            Enabled = Flag("Enabled"),
            Token = read(Prefix + "TmdbToken")?.Trim(),
            Language = Option("Language", "en-US", Languages),
            PlayerSource = Option("PlayerSource", "videasy", PlayerSources),
            Theme = Option("Theme", "dark", Themes),
            AccentColor = Option("AccentColor", "red", Accents),
            HistoryEnabled = Flag("HistoryEnabled")
        };
    }

    public static string Validate(VedioSettingsCommand settings)
    {
        if (settings is null) return "配置不能为空。";
        if (!Languages.Contains(settings.Language)) return "影视资料语言无效。";
        if (!PlayerSources.Contains(settings.PlayerSource)) return "播放源无效。";
        if (!Themes.Contains(settings.Theme)) return "主题无效。";
        if (!Accents.Contains(settings.AccentColor)) return "强调色无效。";
        var token = settings.TmdbToken?.Trim();
        if (token?.Length > 4096 || token?.Any(char.IsWhiteSpace) == true) return "TMDB Token 格式无效。";
        if (settings.ClearToken && !string.IsNullOrEmpty(token)) return "清除 Token 时请先清空新 Token 输入框。";
        return null;
    }
}
