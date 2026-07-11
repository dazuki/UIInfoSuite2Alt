using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Infrastructure.Helpers;
using UIInfoSuite2Alt.Options;
using UIInfoSuite2Alt.UIElements;
using xTile.Layers;
using Object = StardewValley.Object;

namespace UIInfoSuite2Alt.Infrastructure;

/// <summary>Registers the "uiis" console command family for debugging.</summary>
internal static class DebugCommandHandler
{
  private static IModHelper _helper = null!;
  private static IMonitor _monitor = null!;
  private static string _harmonyId = null!;
  private static string _modVersion = null!;

  public static void Register(IModHelper helper, IMonitor monitor, IManifest manifest)
  {
    _helper = helper;
    _monitor = monitor;
    _harmonyId = manifest.UniqueID;
    _modVersion = manifest.Version.ToString();
    helper.ConsoleCommands.Add(
      "uiis",
      "UIInfoSuite2Alt debug commands. Run 'uiis help' for a list of subcommands.",
      HandleCommand
    );
  }

  private static void HandleCommand(string command, string[] args)
  {
    string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
    try
    {
      switch (sub)
      {
        case "help":
          ShowHelp();
          break;
        case "config":
          ShowConfig();
          break;
        case "predict":
          ShowPrediction();
          break;
        default:
          _monitor.Log(
            $"DebugCommandHandler: unknown subcommand '{sub}', run 'uiis help' for a list",
            LogLevel.Info
          );
          break;
      }
    }
    catch (Exception ex)
    {
      _monitor.Log($"DebugCommandHandler: subcommand '{sub}' failed, {ex}", LogLevel.Error);
    }
  }

  private static void Output(string sub, StringBuilder sb)
  {
    _monitor.Log($"DebugCommandHandler: {sub}\n{sb.ToString().TrimEnd()}", LogLevel.Info);
  }

  /// <summary>Logs the output and also writes it to debug/{sub}_{timestamp}.json in the mod folder.</summary>
  private static void OutputWithFile(string sub, StringBuilder sb)
  {
    string content = sb.ToString().TrimEnd();
    string? path = WriteDebugFile(sub, content);
    string suffix = path != null ? $"\n({sub} debug file created: {path})" : "";
    _monitor.Log($"DebugCommandHandler: {sub}\n{content}{suffix}", LogLevel.Info);
  }

  /// <summary>Opens the JSON root and writes the shared debug header comment block.</summary>
  private static void AppendHeader(StringBuilder sb, string sub)
  {
    sb.AppendLine("{");
    sb.AppendLine($"  // '{sub}' command debug output");
    sb.AppendLine($"  // ");
    sb.AppendLine($"  // uiis2a version: {_modVersion}");
    sb.AppendLine($"  // ");
    sb.AppendLine($"  // game: {Game1.version}");
    sb.AppendLine($"  // smapi: {Constants.ApiVersion}");
    sb.AppendLine($"  // contentpatcher: {GetModVersion(ModCompat.ContentPatcher)}");
    sb.AppendLine($"  // spacecore: {GetModVersion(ModCompat.SpaceCore)}");
    sb.AppendLine($"  // gmcm: {GetModVersion(ModCompat.Gmcm)}");
    if (sub == "predict")
    {
      sb.AppendLine($"  // farmtypemanager: {GetModVersion(ModCompat.FarmTypeManager)}");
      sb.AppendLine($"  // archaeologyskill: {GetModVersion(ModCompat.ArchaeologySkill)}");
      AppendSkillInfo(
        sb,
        ModCompat.ArchaeologySkill,
        ShowArtifactSpotTooltip.ArchaeologySkillId,
        showAntiquarian: true
      );
      sb.AppendLine($"  // binningskill: {GetModVersion(ModCompat.BinningSkill)}");
      AppendSkillInfo(
        sb,
        ModCompat.BinningSkill,
        GarbageCanPredictor.BinningSkillId,
        showAntiquarian: false
      );
    }

    if (sub == "config")
    {
      sb.AppendLine($"  // ");
      sb.AppendLine($"  // (this debug file can also replace the existing config.json file)");
    }

    sb.AppendLine();
  }

  private static string GetModVersion(string modId)
  {
    return _helper.ModRegistry.Get(modId)?.Manifest.Version.ToString() ?? "(not found)";
  }

  /// <summary>Writes level (and optionally Antiquarian state) sub-lines for a SpaceCore skill mod.</summary>
  private static void AppendSkillInfo(
    StringBuilder sb,
    string modId,
    string skillId,
    bool showAntiquarian
  )
  {
    if (
      !_helper.ModRegistry.IsLoaded(modId)
      || !ApiManager.GetApi(ModCompat.SpaceCore, out ISpaceCoreApi? spaceCore)
    )
    {
      return;
    }

    try
    {
      sb.AppendLine($"  //   - level: {spaceCore.GetLevelForCustomSkill(Game1.player, skillId)}");
    }
    catch (Exception ex)
    {
      sb.AppendLine($"  //   - level: (lookup failed: {ex.Message})");
    }

    if (showAntiquarian)
    {
      int? antiquarianId = ShowArtifactSpotTooltip.ResolveAntiquarianProfessionId(_helper);
      bool active =
        antiquarianId.HasValue && Game1.player.professions.Contains(antiquarianId.Value);
      sb.AppendLine($"  //   - antiquarian: {(active ? "true" : "false")}");
    }
  }

  /// <summary>Strips invalid filename characters, falling back to "unknown" when empty.</summary>
  private static string SanitizeFileName(string? name)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return "unknown";
    }

    string cleaned = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
    return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
  }

  private static string? WriteDebugFile(string sub, string content)
  {
    try
    {
      string dir = Path.Combine(_helper.DirectoryPath, "debug");
      Directory.CreateDirectory(dir);
      // Predictions are unique per farmer, so tag the file with the farmer name.
      string farmer = sub == "predict" ? $"_{SanitizeFileName(Game1.player?.Name)}" : "";
      string path = Path.Combine(dir, $"{sub}{farmer}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
      File.WriteAllText(path, content);
      return path;
    }
    catch (Exception ex)
    {
      _monitor.Log(
        $"DebugCommandHandler: failed to write debug file for '{sub}', {ex.Message}",
        LogLevel.Warn
      );
      return null;
    }
  }

  private static void ShowHelp()
  {
    var sb = new StringBuilder();
    sb.AppendLine("Available subcommands:");
    sb.AppendLine("  uiis config  - current config values");
    sb.AppendLine(
      "  uiis predict - predictions for the current location (garbage cans, artifact spots, shafts)"
    );
    Output("help", sb);
  }

  // Mirrors the GMCM section grouping; unmapped properties land in "Other".
  private static readonly (string Group, string[] Props)[] _configGroups =
  [
    ("General", ["ShowOptionsTabInMenu"]),
    (
      "Keybinds",
      [
        "OpenCalendarKeybind",
        "OpenQuestBoardKeybind",
        "OpenSpecialOrdersBoardKeybind",
        "OpenQiSpecialOrdersBoardKeybind",
        "HideTreesKeybind",
        "ShowHideTreesBanner",
        "OpenModOptionsKeybind",
        "OpenMonsterEradicationKeybind",
        "ToggleMachineProcessingIcons",
        "ShowOneRange",
        "ShowAllRange",
        "AnimalBuildingTooltipKeybind",
        "ExpandBirthdayLovesKeybind",
      ]
    ),
    (
      "HUD Icons",
      [
        "UseVerticalIconLayout",
        "IconsPerRow",
        "ShowLuckIcon",
        "LuckIconStyle",
        "ShowExactValue",
        "RequireTvForLuck",
        "ShowRainyDay",
        "RequireTvForWeather",
        "ShowBirthdayIcon",
        "HideBirthdayIfFullFriendShip",
        "UseStackedBirthdayIcons",
        "ShowBirthdaysForUnmetVillagers",
        "ShowUnrevealedBirthdayLoves",
        "ShowTravelingMerchant",
        "HideMerchantWhenVisited",
        "ShowMerchantBundleIcon",
        "ShowMerchantBundleItemNames",
        "ShowBookseller",
        "HideBooksellerWhenVisited",
        "ShowFestivalIcon",
        "ShowCraneGameIcon",
        "ShowWhenNewRecipesAreAvailable",
        "ShowRecipeItemIcon",
        "ShowToolUpgradeStatus",
        "ShowRobinBuildingStatusIcon",
        "ShowSeasonalBerry",
        "ShowSeasonalBerryHazelnut",
        "ShowQuestCount",
        "ShowQuestLastDayReminder",
        "ShowGoldenWalnutCount",
        "ShowGoldenWalnutAnywhere",
        "GoldenWalnutFadeOut",
        "BuffIconSize",
        "ShowBuffTimers",
        "PlayBuffExpireSound",
        "ShowCustomIcons",
      ]
    ),
    (
      "Farm and Field",
      [
        "ShowAnimalsNeedPets",
        "HideAnimalPetOnMaxFriendship",
        "ShowWorldTooltip",
        "ShowCropTooltip",
        "ShowTreeTooltip",
        "ShowBarrelTooltip",
        "ShowFishPondTooltip",
        "ShowAnimalBuildingTooltip",
        "ShowForageableTooltip",
        "ShowChestTooltip",
        "ShowArtifactSpotTooltip",
        "ShowGarbageCanTooltip",
        "ShowShaftDestination",
        "ShowHarvestQuality",
        "MachineProcessingIconsMode",
        "MachineProcessingIconsVisible",
        "ShowFishPondIcons",
        "ShowItemEffectRanges",
        "ShowPlacedItemRanges",
        "ShowBombRange",
        "ButtonControlShow",
        "ShowRangeTooltip",
      ]
    ),
    (
      "Experience and Skills",
      [
        "ShowLevelUpAnimation",
        "ShowExperienceBar",
        "ShowExperienceGain",
        "AllowExperienceBarToFadeOut",
        "ShowFishOnCatch",
        "ShowFishQualityStar",
      ]
    ),
    (
      "Items and Shopping",
      [
        "ShowItemQualityOnPickup",
        "ShowExtraItemInformation",
        "ShowInventoryItemSellPrice",
        "GatePricesByPriceCatalogue",
        "ShowInventoryItemArtisanPrices",
        "OnlyShowKnownArtisanMachines",
        "MaxArtisanRows",
        "ShowInventoryItemBundleBanner",
        "ShowInventoryItemDonationStatus",
        "ShowInventoryItemShippingStatus",
        "UseShippingBinIcon",
        "ShowHarvestPricesInShop",
        "ShowLockedBundleItems",
        "ShowGrangeScore",
        "ShowGrangePrize",
      ]
    ),
    ("NPC and Social", ["ShowMailboxCount", "ShowHeartFills", "DisplayCalendarAndBillboard"]),
    ("Icon Order", ["IconOrder"]),
  ];

  private static void ShowConfig()
  {
    Dictionary<string, PropertyInfo> remaining = typeof(ModConfig)
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .ToDictionary(p => p.Name);

    var defaults = new ModConfig();
    var entries = new List<(string Group, string Name, string ValueJson, string DefaultJson)>();
    foreach ((string group, string[] props) in _configGroups)
    {
      foreach (string name in props)
      {
        if (remaining.Remove(name, out PropertyInfo? prop))
        {
          entries.Add(
            (
              group,
              name,
              FormatJsonValue(prop.GetValue(ModEntry.ModConfig)),
              FormatJsonValue(prop.GetValue(defaults))
            )
          );
        }
      }
    }

    foreach (PropertyInfo prop in remaining.Values)
    {
      entries.Add(
        (
          "Other",
          prop.Name,
          FormatJsonValue(prop.GetValue(ModEntry.ModConfig)),
          FormatJsonValue(prop.GetValue(defaults))
        )
      );
    }

    var sb = new StringBuilder();
    AppendHeader(sb, "config");
    string? currentGroup = null;
    for (int i = 0; i < entries.Count; i++)
    {
      (string group, string name, string valueJson, string defaultJson) = entries[i];
      if (group != currentGroup)
      {
        if (currentGroup != null)
        {
          sb.AppendLine();
        }

        sb.AppendLine($"  // {group}");
        currentGroup = group;
      }

      string comma = i < entries.Count - 1 ? "," : "";
      string defaultNote =
        valueJson == defaultJson ? "" : $" // default: {CompactJson(defaultJson)}";
      sb.AppendLine($"  \"{name}\": {valueJson}{comma}{defaultNote}");
    }

    sb.AppendLine("}");
    OutputWithFile("config", sb);
  }

  /// <summary>Collapses a multi-line JSON value (e.g. IconOrder) to one line for trailing comments.</summary>
  private static string CompactJson(string json)
  {
    return json.Contains('\n') ? string.Join(" ", json.Split('\n').Select(l => l.Trim())) : json;
  }

  private static string FormatJsonValue(object? value)
  {
    switch (value)
    {
      case null:
        return "null";
      case bool b:
        return b ? "true" : "false";
      case int i:
        return i.ToString();
      case Dictionary<string, int> dict:
        if (dict.Count == 0)
        {
          return "{}";
        }

        var lines = dict.Select(p => $"    \"{p.Key}\": {p.Value}");
        return "{\n" + string.Join(",\n", lines) + "\n  }";
      default:
        return $"\"{value.ToString()?.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }
  }

  private static void ShowPrediction()
  {
    if (!Context.IsWorldReady || Game1.currentLocation == null)
    {
      _monitor.Log("DebugCommandHandler: 'uiis predict' requires a loaded save", LogLevel.Info);
      return;
    }

    GameLocation location = Game1.currentLocation;
    var sb = new StringBuilder();
    AppendHeader(sb, "predict");
    sb.AppendLine("  // Context");
    sb.AppendLine($"  \"Location\": {J(location.NameOrUniqueName)},");
    sb.AppendLine($"  \"Farmer\": {J(Game1.player.Name)},");
    sb.AppendLine(
      $"  \"GameDate\": {J($"{Game1.Date.DayOfWeek} {Game1.dayOfMonth} {Game1.currentSeason}, year {Game1.year}")},"
    );
    sb.AppendLine($"  \"DaysPlayed\": {Game1.stats.DaysPlayed},");
    sb.AppendLine($"  \"TimeOfDay\": {Game1.timeOfDay},");
    sb.AppendLine(
      $"  \"DailyLuck\": {Game1.player.DailyLuck.ToString("0.####", CultureInfo.InvariantCulture)},"
    );
    sb.AppendLine($"  \"UniqueSaveId\": {Game1.uniqueIDForThisGame},");

    sb.AppendLine();
    sb.AppendLine("  // Garbage cans");
    AppendGarbageCansJson(sb, location);

    sb.AppendLine();
    sb.AppendLine("  // Artifact and seed spots");
    int? antiquarianId = ShowArtifactSpotTooltip.ResolveAntiquarianProfessionId(_helper);
    if (antiquarianId.HasValue && Game1.player.professions.Contains(antiquarianId.Value))
    {
      sb.AppendLine(
        "  // (antiquarian bonus active: each dig also yields a bonus artifact not listed in Drops)"
      );
    }

    AppendArtifactSpotsJson(sb, location);

    if (location is MineShaft mine)
    {
      int fall = ShaftPredictor.PredictFallDistance(mine.mineLevel);
      sb.AppendLine();
      sb.AppendLine("  // Shaft fall (same for every shaft on this floor today)");
      sb.AppendLine(
        $"  \"Shaft\": {{ \"MineLevel\": {mine.mineLevel}, \"PredictedFallFloors\": {fall} }},"
      );
    }

    sb.AppendLine();
    sb.AppendLine("  // Harmony patches by other mods on vanilla methods used by predictions");
    AppendPredictionPatchesJson(sb);
    sb.AppendLine("}");
    OutputWithFile("predict", sb);
  }

  private static void AppendGarbageCansJson(StringBuilder sb, GameLocation location)
  {
    var cans = new List<(string Id, int X, int Y)>();
    Layer? layer = location.map?.GetLayer("Buildings");
    if (layer != null)
    {
      var seenIds = new HashSet<string>();
      for (int x = 0; x < layer.LayerWidth; x++)
      {
        for (int y = 0; y < layer.LayerHeight; y++)
        {
          if (
            ShowGarbageCanTooltip.TryResolveGarbageCanId(location, new Vector2(x, y), out string id)
            && seenIds.Add(id)
          )
          {
            cans.Add((id, x, y));
          }
        }
      }
    }

    if (cans.Count == 0)
    {
      sb.AppendLine("  \"GarbageCans\": {},");
      return;
    }

    sb.AppendLine("  \"GarbageCans\": {");
    for (int i = 0; i < cans.Count; i++)
    {
      (string id, int x, int y) = cans[i];
      GarbageCanPredictor.Predict(
        location,
        id,
        new Vector2(x, y),
        Game1.player,
        out List<Item> items,
        out bool alreadyChecked,
        out int? lockedMinLevel,
        out int? requiredBinningLevel,
        out bool fromGarbageDayChest
      );

      sb.AppendLine($"    {J(id)}: {{");
      sb.AppendLine($"      \"Tile\": \"{x}, {y}\",");
      AppendJsonArray(
        sb,
        "      ",
        "Items",
        items.Select(item => J(FormatItem(item))).ToList(),
        trailingComma: true
      );

      var props = new List<string> { $"\"AlreadyChecked\": {(alreadyChecked ? "true" : "false")}" };
      if (requiredBinningLevel != null)
      {
        props.Add($"\"BinningSkillRequired\": {requiredBinningLevel}");
        props.Add($"\"BinningSkillUnlocked\": {(lockedMinLevel == null ? "true" : "false")}");
      }

      if (fromGarbageDayChest)
      {
        props.Add("\"FromGarbageDayChest\": true");
      }

      for (int p = 0; p < props.Count; p++)
      {
        sb.AppendLine($"      {props[p]}{(p < props.Count - 1 ? "," : "")}");
      }

      sb.AppendLine(i < cans.Count - 1 ? "    }," : "    }");
    }

    sb.AppendLine("  },");
  }

  private static void AppendArtifactSpotsJson(StringBuilder sb, GameLocation location)
  {
    var spots = new List<(string Type, int X, int Y, List<PredictedDrop> Drops)>();
    foreach ((Vector2 tile, Object obj) in location.Objects.Pairs)
    {
      bool isSeedSpot = obj.QualifiedItemId == "(O)SeedSpot";
      if (obj.QualifiedItemId != "(O)590" && !isSeedSpot)
      {
        continue;
      }

      List<PredictedDrop> drops = isSeedSpot
        ? ArtifactSpotPredictor.PredictSeedSpotDrop(Game1.player, (int)tile.X, (int)tile.Y)
        : ArtifactSpotPredictor.PredictArtifactSpotDrop(
          location,
          (int)tile.X,
          (int)tile.Y,
          Game1.player
        );
      spots.Add((isSeedSpot ? "SeedSpot" : "ArtifactSpot", (int)tile.X, (int)tile.Y, drops));
    }

    if (spots.Count == 0)
    {
      sb.AppendLine("  \"ArtifactSpots\": [],");
      return;
    }

    sb.AppendLine("  \"ArtifactSpots\": [");
    for (int i = 0; i < spots.Count; i++)
    {
      (string type, int x, int y, List<PredictedDrop> drops) = spots[i];
      sb.AppendLine("    {");
      sb.AppendLine($"      \"Type\": \"{type}\",");
      sb.AppendLine($"      \"Tile\": \"{x}, {y}\",");
      AppendJsonArray(
        sb,
        "      ",
        "Drops",
        drops.Select(d => J(FormatDrop(d))).ToList(),
        trailingComma: false
      );
      sb.AppendLine(i < spots.Count - 1 ? "    }," : "    }");
    }

    sb.AppendLine("  ],");
  }

  private static void AppendPredictionPatchesJson(StringBuilder sb)
  {
    var checks = new (string Label, MethodBase? Method)[]
    {
      (
        "GameLocation.TryGetGarbageItem",
        AccessTools.Method(typeof(GameLocation), nameof(GameLocation.TryGetGarbageItem))
      ),
      (
        "GameLocation.CheckGarbage",
        AccessTools.Method(typeof(GameLocation), nameof(GameLocation.CheckGarbage))
      ),
      (
        "GameLocation.digUpArtifactSpot",
        AccessTools.Method(typeof(GameLocation), "digUpArtifactSpot")
      ),
      (
        "GameLocation.tryToCreateUnseenSecretNote",
        AccessTools.Method(typeof(GameLocation), nameof(GameLocation.tryToCreateUnseenSecretNote))
      ),
      (
        "Object.performToolAction",
        AccessTools.Method(typeof(Object), nameof(Object.performToolAction))
      ),
      (
        "Utility.GetUnseenSecretNotes",
        AccessTools.Method(typeof(Utility), nameof(Utility.GetUnseenSecretNotes))
      ),
      (
        "IslandLocation.digUpArtifactSpot",
        AccessTools.DeclaredMethod(typeof(IslandLocation), "digUpArtifactSpot")
      ),
      (
        "DesertFestival.digUpArtifactSpot",
        AccessTools.DeclaredMethod(typeof(DesertFestival), "digUpArtifactSpot")
      ),
      ("MineShaft.enterMineShaft", AccessTools.Method(typeof(MineShaft), "enterMineShaft")),
    };

    sb.AppendLine("  \"PatchedVanillaMethods\": {");
    for (int i = 0; i < checks.Length; i++)
    {
      (string label, MethodBase? method) = checks[i];
      bool trailingComma = i < checks.Length - 1;
      if (method == null)
      {
        sb.AppendLine(
          $"    {J(label)}: \"method not found (game update?)\"{(trailingComma ? "," : "")}"
        );
        continue;
      }

      List<string> owners = GetForeignPatchOwners(method);
      AppendJsonArray(sb, "    ", label, owners.Select(J).ToList(), trailingComma);
    }

    sb.AppendLine("  }");
  }

  private static List<string> GetForeignPatchOwners(MethodBase method)
  {
    var owners = new List<string>();
    HarmonyLib.Patches? patches = Harmony.GetPatchInfo(method);
    if (patches == null)
    {
      return owners;
    }

    AddOwners(owners, patches.Prefixes, "prefix");
    AddOwners(owners, patches.Postfixes, "postfix");
    AddOwners(owners, patches.Transpilers, "transpiler");
    AddOwners(owners, patches.Finalizers, "finalizer");
    return owners;
  }

  private static void AddOwners(List<string> owners, IReadOnlyList<Patch> patches, string type)
  {
    foreach (Patch patch in patches)
    {
      if (patch.owner != _harmonyId)
      {
        MethodInfo method = patch.PatchMethod;
        owners.Add($"{patch.owner} ({type}: {method.DeclaringType?.Name}.{method.Name})");
      }
    }
  }

  /// <summary>Appends "name": [ ... ] with one value per line; empty arrays render as [].</summary>
  private static void AppendJsonArray(
    StringBuilder sb,
    string indent,
    string name,
    List<string> jsonValues,
    bool trailingComma
  )
  {
    string comma = trailingComma ? "," : "";
    if (jsonValues.Count == 0)
    {
      sb.AppendLine($"{indent}\"{name}\": []{comma}");
      return;
    }

    sb.AppendLine($"{indent}\"{name}\": [");
    for (int i = 0; i < jsonValues.Count; i++)
    {
      sb.AppendLine($"{indent}  {jsonValues[i]}{(i < jsonValues.Count - 1 ? "," : "")}");
    }

    sb.AppendLine($"{indent}]{comma}");
  }

  private static string J(string value)
  {
    return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
  }

  private static string FormatItem(Item item)
  {
    string stack = item.Stack > 1 ? $" x{item.Stack}" : "";
    return $"{item.DisplayName}{stack} [{item.QualifiedItemId}]";
  }

  private static string FormatDrop(PredictedDrop drop)
  {
    string text = FormatItem(drop.Item);
    if (drop.SecretNoteChance > 0f && drop.SecretNoteDisplayName != null)
    {
      string noteId = drop.SecretNoteItemId != null ? $" [{drop.SecretNoteItemId}]" : "";
      text += $" ({drop.SecretNoteChance:P0} chance of {drop.SecretNoteDisplayName}{noteId})";
    }

    return text;
  }
}
