namespace Masuit.MyBlogs.Core.Models.Command;

public sealed class VedioSettingsCommand
{
    public bool Enabled { get; set; } = true;
    public string TmdbToken { get; set; }
    public bool ClearToken { get; set; }
    public string Language { get; set; } = "en-US";
    public string PlayerSource { get; set; } = "videasy";
    public string Theme { get; set; } = "dark";
    public string AccentColor { get; set; } = "red";
    public bool HistoryEnabled { get; set; } = true;
}
