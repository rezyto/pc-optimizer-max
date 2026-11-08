using PcMax.Core.Settings;
using PcMax.Core.Tweaks;
using Xunit;

namespace PcMax.Core.Tests;

/// <summary>settings.json is read by an elevated app: every malformed or hostile shape must end up as safe defaults.</summary>
public class SettingsNormalizationTests
{
    private static readonly AppSettings Defaults = new AppSettings().Normalize();

    [Fact]
    public void Null_temperatures_become_defaults()
    {
        var s = SettingsStore.Parse("""{"temperatures":null}""");
        Assert.Equal(Defaults.Temperatures, s.Temperatures);
    }

    [Fact]
    public void Null_game_entries_are_dropped()
    {
        var s = SettingsStore.Parse("""{"games":[null]}""");
        Assert.Empty(s.Games);
    }

    [Fact]
    public void Null_lists_become_empty_or_default()
    {
        var s = SettingsStore.Parse("""{"games":null,"backgroundAppsToSuspend":null,"networkTestHost":null}""");
        Assert.Empty(s.Games);
        Assert.Empty(s.BackgroundAppsToSuspend);
        Assert.Equal("1.1.1.1", s.NetworkTestHost);
    }

    [Fact]
    public void Json_null_document_gives_defaults()
    {
        var s = SettingsStore.Parse("null");
        Assert.Equal(Defaults.StartWithWindows, s.StartWithWindows);
        Assert.Equal(Defaults.Temperatures, s.Temperatures);
        Assert.True(s.StartWithWindows); // default stays on
    }

    [Fact]
    public void Background_apps_are_cleaned()
    {
        var s = SettingsStore.Parse("""{"backgroundAppsToSuspend":[null,"","..\\x","C:\\evil.exe","Spotify.exe","spotify","csrss","*","a\u0000b"]}""");
        Assert.Equal(new[] { "Spotify" }, s.BackgroundAppsToSuspend);
    }

    [Fact]
    public void Game_profiles_are_sanitized()
    {
        var s = SettingsStore.Parse("""
            {"games":[
              {"executableName":null},
              {"executableName":"..\\..\\Windows\\cmd.exe"},
              {"executableName":"cs2","displayName":null,"tweakIds":null},
              {"executableName":"CS2.exe","displayName":"duplicate"},
              {"executableName":"game.exe","tweakIds":[null,"", "power.high-performance","power.high-performance"]}
            ]}
            """);
        Assert.Equal(2, s.Games.Count);
        var cs2 = s.Games[0];
        Assert.Equal("cs2.exe", cs2.ExecutableName);
        Assert.Equal("", cs2.DisplayName);
        Assert.Empty(cs2.TweakIds);
        Assert.Equal(new[] { "power.high-performance" }, s.Games[1].TweakIds);
    }

    [Fact]
    public void Out_of_range_values_are_fixed()
    {
        var s = SettingsStore.Parse("""{"maxRisk":99,"temperatures":{"cpuWarning":-5,"cpuCritical":1000,"sustainSeconds":0},"networkTestHost":"   "}""");
        Assert.Equal(RiskLevel.Safe, s.MaxRisk);
        Assert.Equal(85, s.Temperatures.CpuWarning);
        Assert.Equal(95, s.Temperatures.CpuCritical);
        Assert.Equal(1, s.Temperatures.SustainSeconds);
        Assert.Equal("1.1.1.1", s.NetworkTestHost);
    }

    [Fact]
    public void Lists_are_capped()
    {
        var apps = string.Join(",", Enumerable.Range(0, 500).Select(i => $"\"app{i}\""));
        var s = SettingsStore.Parse($$"""{"backgroundAppsToSuspend":[{{apps}}]}""");
        Assert.Equal(AppSettings.MaxBackgroundApps, s.BackgroundAppsToSuspend.Count);
    }

    [Fact]
    public void Malformed_json_throws_from_parse_but_load_falls_back_to_defaults()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SettingsStore.Parse("{not json"));

        var dir = Path.Combine(Path.GetTempPath(), "pcmax-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file, """{"temperatures":null,"games":[null]}""");
            var loaded = new SettingsStore(file).Load();
            Assert.Equal(Defaults.Temperatures, loaded.Temperatures);
            Assert.Empty(loaded.Games);
            Assert.Empty(Directory.GetFiles(dir, "*.corrupt-*")); // valid JSON: no backup made

            File.WriteAllText(file, "{broken");
            Assert.Equal(Defaults.Temperatures, new SettingsStore(file).Load().Temperatures);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Snapshot_parse_drops_null_and_incomplete_entries()
    {
        var list = JsonSnapshotStore.Parse("""
            [null,
             {"tweakId":null,"appliedAt":"2026-01-01T00:00:00Z","previousValues":{}},
             {"tweakId":"x","appliedAt":"2026-01-01T00:00:00Z","previousValues":null},
             {"tweakId":"ok","appliedAt":"2026-01-01T00:00:00Z","previousValues":{"a":"1"}}]
            """);
        Assert.Single(list);
        Assert.Equal("ok", list[0].TweakId);
    }
}
