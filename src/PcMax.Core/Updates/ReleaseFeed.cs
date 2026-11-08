using System.Text.Json;

namespace PcMax.Core.Updates;

/// <summary>A release that offers an installer newer than the running app.</summary>
public sealed record ReleaseCandidate(ReleaseVersion Version, string Tag, string InstallerName, Uri InstallerUrl, string ChecksumName, Uri ChecksumUrl, long InstallerSize);

/// <summary>
/// Reads the GitHub Releases API answer for "latest release" and picks out the installer and its checksum.
/// Everything in the answer is untrusted: the tag must be a plain version, drafts and pre-releases are ignored,
/// both assets must exist under their exact names, and the download URLs are built by us
/// (https://github.com/{owner}/{repo}/releases/download/{tag}/{name}), never taken from the JSON.
/// </summary>
public static class ReleaseFeed
{
    /// <summary>Releases API answers are a few KB; anything bigger is not parsed.</summary>
    public const int MaxJsonBytes = 1024 * 1024;
    /// <summary>The installer is ~60–90 MB; refuse anything absurd.</summary>
    public const long MaxInstallerBytes = 400L * 1024 * 1024;

    public static Uri LatestApiUrl(string owner, string repo) =>
        new($"https://api.github.com/repos/{owner}/{repo}/releases/latest");

    /// <summary>
    /// Returns the release if it is valid and newer than <paramref name="current"/>; null otherwise
    /// (with <paramref name="reason"/> saying why, for the log).
    /// </summary>
    public static ReleaseCandidate? TryParseLatest(string? json, string owner, string repo, ReleaseVersion current, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonBytes) { reason = "resposta vazia ou demasiado grande"; return null; }
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { reason = "resposta inválida"; return null; }
            if (GetBool(root, "draft") || GetBool(root, "prerelease")) { reason = "versão de teste ignorada"; return null; }

            var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (!ReleaseVersion.TryParse(tag, out var version)) { reason = "etiqueta de versão inválida"; return null; }
            if (version <= current) { reason = "sem versão nova"; return null; }

            var installer = ReleaseAssets.InstallerName(version);
            var checksum = ReleaseAssets.ChecksumName(version);
            long? installerSize = null;
            var hasChecksum = false;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                    var name = n.GetString();
                    if (string.Equals(name, installer, StringComparison.Ordinal))
                        installerSize = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var sz) ? sz : 0;
                    else if (string.Equals(name, checksum, StringComparison.Ordinal))
                        hasChecksum = true;
                }
            }
            if (installerSize is null || !hasChecksum) { reason = "a versão não tem instalador ou ficheiro .sha256"; return null; }
            if (installerSize > MaxInstallerBytes) { reason = "instalador demasiado grande"; return null; }

            // The tag passed ReleaseVersion.TryParse, so it is "v1.2.3" or "1.2.3": safe in a URL path.
            var tagText = tag!.Trim();
            var baseUrl = $"https://github.com/{owner}/{repo}/releases/download/{tagText}/";
            return new ReleaseCandidate(version, tagText, installer, new Uri(baseUrl + installer), checksum, new Uri(baseUrl + checksum), installerSize.Value);
        }
        catch (JsonException)
        {
            reason = "resposta não é JSON válido";
            return null;
        }
    }

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
