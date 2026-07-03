using System;
using System.IO;
using System.Text.Json;
using StardewModdingAPI;

namespace UIInfoSuite2Alt.Compatibility.Helpers;

/// <summary>Reads Walk of Life prestige config from disk.</summary>
internal static class WalkOfLifeHelper
{
  private static bool _initialized;
  private static bool _prestigeEnabled;
  private static uint _expPerPrestigeLevel = 5000;

  /// <summary>Whether WoL prestige levels (11-20) are enabled.</summary>
  public static bool PrestigeEnabled => _prestigeEnabled;

  /// <summary>XP required per prestige level above 10. Default 5000.</summary>
  public static uint ExpPerPrestigeLevel => _expPerPrestigeLevel;

  /// <summary>Reads prestige config from WoL's config.json. Call once on GameLaunched.</summary>
  public static void Initialize(IModHelper helper)
  {
    _initialized = false;
    _prestigeEnabled = false;
    _expPerPrestigeLevel = 5000;

    if (!helper.ModRegistry.IsLoaded(ModCompat.WalkOfLife))
    {
      return;
    }

    try
    {
      string? configPath = GetModConfigPath(helper, ModCompat.WalkOfLife);

      if (configPath == null)
      {
        ModEntry.MonitorObject.Log(
          "WalkOfLifeHelper: could not locate mod directory, using defaults",
          LogLevel.Warn
        );
        _prestigeEnabled = true;
        _initialized = true;
        return;
      }

      if (!File.Exists(configPath))
      {
        ModEntry.MonitorObject.Log(
          "WalkOfLifeHelper: no config.json found, using defaults",
          LogLevel.Trace
        );
        _prestigeEnabled = true;
        _initialized = true;
        return;
      }

      ModEntry.MonitorObject.Log(
        $"WalkOfLifeHelper: reading config from {configPath}",
        LogLevel.Trace
      );

      string json = File.ReadAllText(configPath);
      using var doc = JsonDocument.Parse(json);
      JsonElement root = doc.RootElement;

      if (root.TryGetProperty("Masteries", out JsonElement masteries))
      {
        _prestigeEnabled = masteries.TryGetProperty("EnablePrestigeLevels", out JsonElement ep)
          ? ep.GetBoolean()
          : true;
        _expPerPrestigeLevel = masteries.TryGetProperty("ExpPerPrestigeLevel", out JsonElement xp)
          ? xp.GetUInt32()
          : 5000;
      }
      else
      {
        // Masteries section missing means defaults (prestige enabled, 5000 per level)
        _prestigeEnabled = true;
      }

      _initialized = true;
      ModEntry.MonitorObject.Log(
        $"WalkOfLifeHelper: prestige={_prestigeEnabled}, expPerLevel={_expPerPrestigeLevel}",
        LogLevel.Trace
      );
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"WalkOfLifeHelper: failed to read config.json, {ex.Message}",
        LogLevel.Warn
      );
    }
  }

  /// <summary>Cumulative XP for a prestige level (11-20), or -1 if unavailable.</summary>
  public static int GetExperienceRequiredForPrestigeLevel(int currentLevel)
  {
    if (!_initialized || !_prestigeEnabled)
    {
      return -1;
    }

    // Maps to WoL's ExperienceCurve[currentLevel + 1]:
    //   currentLevel 10 -> curve[11] = 15000 + exp*1 = 20000
    //   currentLevel 19 -> curve[20] = 15000 + exp*10 = 65000
    int multiplier = currentLevel - 9; // level 10 -> 1, level 19 -> 10
    if (multiplier < 1 || multiplier > 10)
    {
      return -1;
    }

    return 15000 + (int)(_expPerPrestigeLevel * multiplier);
  }

  /// <summary>Resolves a mod's config.json path via SMAPI internals, with recursive fallback.</summary>
  private static string? GetModConfigPath(IModHelper helper, string modId)
  {
    IModInfo? modInfo = helper.ModRegistry.Get(modId);
    if (modInfo == null)
    {
      return null;
    }

    // SMAPI's IModInfo implementation has a DirectoryPath property (not on the public interface)
    string? dirPath = modInfo.GetType().GetProperty("DirectoryPath")?.GetValue(modInfo)?.ToString();

    if (!string.IsNullOrEmpty(dirPath))
    {
      return Path.Combine(dirPath, "config.json");
    }

    // Fallback: search Mods folder recursively
    string modsDir = Path.GetDirectoryName(helper.DirectoryPath)!;
    foreach (
      string manifestPath in Directory.EnumerateFiles(
        modsDir,
        "manifest.json",
        SearchOption.AllDirectories
      )
    )
    {
      try
      {
        string manifestJson = File.ReadAllText(manifestPath);
        using var doc = JsonDocument.Parse(manifestJson);
        if (
          doc.RootElement.TryGetProperty("UniqueID", out JsonElement idProp)
          && string.Equals(idProp.GetString(), modId, StringComparison.OrdinalIgnoreCase)
        )
        {
          return Path.Combine(Path.GetDirectoryName(manifestPath)!, "config.json");
        }
      }
      catch
      {
        // Skip unreadable manifests
      }
    }

    return null;
  }
}
