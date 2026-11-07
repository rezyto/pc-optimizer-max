namespace PcMax.Core;

public static class AppInfo
{
    public const string Name = "PC Max";
    public const string RepoOwner = "rezyto";
    public const string RepoName = "pc-optimizer-max";
    public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
