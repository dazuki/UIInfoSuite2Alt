using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.FruitTrees;
using StardewValley.ItemTypeDefinitions;
using StardewValley.TerrainFeatures;
using StardewValley.TokenizableStrings;
using Object = StardewValley.Object;

namespace UIInfoSuite2Alt.Infrastructure.Helpers;

public record DropInfo(string? Condition, float Chance, string ItemId)
{
  public int? GetNextDay(bool includeToday)
  {
    return DropsHelper.GetNextDay(Condition, includeToday);
  }
}

public record PossibleDroppedItem(
  int NextDayToProduce,
  ParsedItemData Item,
  float Chance,
  string? CustomId = null
)
{
  public bool ReadyToPick => Game1.dayOfMonth == NextDayToProduce;
}

public record FruitTreeInfo(string TreeName, List<PossibleDroppedItem> Items);

public static class DropsHelper
{
  private static readonly Dictionary<string, string> CropNamesCache = [];

  public static int? GetNextDay(string? condition, bool includeToday)
  {
    return string.IsNullOrEmpty(condition)
      ? Game1.dayOfMonth + (includeToday ? 0 : 1)
      : Tools.GetNextDayFromCondition(condition, includeToday);
  }

  public static int? GetLastDay(string? condition)
  {
    return Tools.GetLastDayFromCondition(condition);
  }

  public static string GetCropHarvestName(Crop crop)
  {
    // Forage crops (Spring Onion, Ginger) don't set indexOfHarvest — map forage type to item ID
    if (crop.forageCrop.Value)
    {
      string forageCropItemId = crop.whichForageCrop.Value switch
      {
        "1" => "399", // Spring Onion
        "2" => "829", // Ginger
        _ => crop.whichForageCrop.Value,
      };
      return GetOrCacheCropName(forageCropItemId);
    }

    if (crop.indexOfHarvest.Value is null)
    {
      ModEntry.MonitorObject.LogOnce(
        $"DropsHelper: crop has no harvest item ID, seed={crop.netSeedIndex.Value}, forage={crop.forageCrop.Value}",
        LogLevel.Warn
      );
      return "Unknown Crop";
    }

    string itemId = crop.isWildSeedCrop() ? crop.whichForageCrop.Value : crop.indexOfHarvest.Value;
    return GetOrCacheCropName(itemId);
  }

  private static string GetOrCacheCropName(string itemId)
  {
    if (CropNamesCache.TryGetValue(itemId, out string? harvestName))
    {
      return harvestName;
    }

    // Technically has the best compatibility for looking up items vs ItemRegistry.
    harvestName = new Object(itemId, 1).DisplayName;
    CropNamesCache.Add(itemId, harvestName);

    return harvestName;
  }

  public static List<PossibleDroppedItem> GetFruitTreeDropItems(
    FruitTree tree,
    bool includeToday = false
  )
  {
    var treeData = tree.GetData();
    if (treeData?.Fruit is not { Count: > 0 })
    {
      ModEntry.MonitorObject.LogOnce(
        $"DropsHelper: fruit tree '{tree.treeId.Value}' has null data or no fruit entries",
        LogLevel.Warn
      );
      return new List<PossibleDroppedItem>();
    }

    return GetGenericDropItems(
      treeData.Fruit,
      null,
      includeToday,
      "Fruit Tree",
      FruitTreeDropConverter
    );

    DropInfo FruitTreeDropConverter(FruitTreeFruitData input)
    {
      return new DropInfo(input.Condition, input.Chance, input.ItemId);
    }
  }

  public static FruitTreeInfo GetFruitTreeInfo(FruitTree tree, bool harvestIncludeToday = false)
  {
    var treeData = tree.GetData();
    string? displayName = null;

    if (treeData?.DisplayName != null)
    {
      displayName = TokenParser.ParseText(treeData.DisplayName);

      // Work around Content Patcher mods with mismatched i18n keys. SDV returns either
      // "(no translation:KEY)" or the raw "Assets\\Path:key" when LocalizedText can't resolve.
      if (
        displayName.Contains("(no translation:")
        || displayName.StartsWith("Strings\\", StringComparison.Ordinal)
        || displayName.StartsWith("Strings/", StringComparison.Ordinal)
      )
      {
        displayName = null;
      }
    }

    if (string.IsNullOrEmpty(displayName))
    {
      var itemData = ItemRegistry.GetData(tree.treeId.Value);
      if (itemData != null)
        displayName = itemData.DisplayName;
    }

    List<PossibleDroppedItem> drops = GetFruitTreeDropItems(tree, harvestIncludeToday);

    if (drops.Count > 1)
    {
      drops = [drops[0]];
    }

    if (string.IsNullOrEmpty(displayName) && drops.Count > 0)
    {
      displayName = drops[0].Item.DisplayName;
    }

    if (string.IsNullOrEmpty(displayName))
    {
      displayName = tree.treeId.Value;
    }

    string cleanName = displayName.Replace(" Sapling", "");
    string finalName;

    bool isEnglish =
      LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.en;
    if (
      !isEnglish
      || cleanName.EndsWith(I18n.Tree().Trim(), StringComparison.OrdinalIgnoreCase)
      || cleanName.EndsWith("Tree", StringComparison.OrdinalIgnoreCase)
    )
    {
      finalName = cleanName;
    }
    else
    {
      finalName = $"{cleanName}{I18n.Tree()}";
    }

    return new FruitTreeInfo(finalName, drops);
  }

  public static List<PossibleDroppedItem> GetGenericDropItems<T>(
    IEnumerable<T> drops,
    string? customId,
    bool includeToday,
    string displayName,
    Func<T, DropInfo> extractDropInfo
  )
  {
    List<PossibleDroppedItem> items = new();

    foreach (T drop in drops)
    {
      DropInfo dropInfo = extractDropInfo(drop);
      int? nextDay = GetNextDay(dropInfo.Condition, includeToday);
      int? lastDay = GetLastDay(dropInfo.Condition);

      if (!nextDay.HasValue)
      {
        if (!lastDay.HasValue && !string.IsNullOrEmpty(dropInfo.Condition))
        {
          // Condition has no day-based queries (e.g. LOCATION_IS_OUTDOORS) — treat as "any day"
          nextDay = Game1.dayOfMonth + (includeToday ? 0 : 1);
        }
        else
        {
          continue;
        }
      }

      ParsedItemData? itemData = ItemRegistry.GetData(dropInfo.ItemId);
      if (itemData == null)
      {
        ModEntry.MonitorObject.LogOnce(
          $"DropsHelper: could not parse item '{displayName}', itemId={dropInfo.ItemId}",
          LogLevel.Debug
        );
        continue;
      }

      if (Game1.dayOfMonth == nextDay.Value && !includeToday)
      {
        continue;
      }

      items.Add(new PossibleDroppedItem(nextDay.Value, itemData, dropInfo.Chance, customId));
    }

    return items;
  }
}
