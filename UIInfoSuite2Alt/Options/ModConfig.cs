using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace UIInfoSuite2Alt.Options;

public class ModConfig
{
  private static readonly PropertyInfo[] ToggleProperties = typeof(ModConfig)
    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
    .Where(p => p.PropertyType == typeof(bool) || p.PropertyType == typeof(int))
    .ToArray();

  /// <summary>Snapshots all bool/int toggle properties as name=value pairs.</summary>
  public Dictionary<string, string> SnapshotToggles()
  {
    var snapshot = new Dictionary<string, string>();
    foreach (PropertyInfo prop in ToggleProperties)
    {
      snapshot[prop.Name] = prop.GetValue(this)?.ToString() ?? "";
    }

    return snapshot;
  }

  /// <summary>Returns list of "Name: old -> new" strings for changed toggles.</summary>
  public static List<string> DiffToggles(
    Dictionary<string, string> before,
    Dictionary<string, string> after
  )
  {
    List<string> changes = [];
    foreach ((string key, string newVal) in after)
    {
      if (before.TryGetValue(key, out string? oldVal) && oldVal != newVal)
      {
        changes.Add($"{key}: {oldVal} > {newVal}");
      }
    }

    return changes;
  }

  // --- Global settings ---
  public bool ShowOptionsTabInMenu { get; set; } = true;
  public KeybindList OpenCalendarKeybind { get; set; } = KeybindList.ForSingle(SButton.B);
  public KeybindList OpenQuestBoardKeybind { get; set; } = KeybindList.ForSingle(SButton.H);
  public KeybindList OpenSpecialOrdersBoardKeybind { get; set; } = KeybindList.ForSingle(SButton.J);
  public KeybindList OpenQiSpecialOrdersBoardKeybind { get; set; } =
    KeybindList.Parse("LeftControl + Q");
  public KeybindList HideTreesKeybind { get; set; } = KeybindList.ForSingle(SButton.F7);
  public bool ShowHideTreesBanner { get; set; } = true;
  public KeybindList ShowOneRange { get; set; } = KeybindList.ForSingle(SButton.LeftControl);
  public KeybindList ShowAllRange { get; set; } = KeybindList.Parse("LeftControl + LeftAlt");
  public KeybindList OpenModOptionsKeybind { get; set; } = KeybindList.ForSingle(SButton.F8);
  public KeybindList OpenMonsterEradicationKeybind { get; set; } =
    KeybindList.ForSingle(SButton.F9);

  // --- Feature toggles (migrated from per-save ModOptions) ---
  public bool AllowExperienceBarToFadeOut { get; set; } = true;
  public bool ShowExperienceBar { get; set; } = true;
  public bool ShowExperienceGain { get; set; } = true;
  public bool ShowLevelUpAnimation { get; set; } = true;
  public bool ShowHeartFills { get; set; } = true;
  public bool ShowExtraItemInformation { get; set; } = true;
  public bool ShowInventoryItemSellPrice { get; set; } = true;
  public bool GatePricesByPriceCatalogue { get; set; } = false;
  public bool ShowInventoryItemArtisanPrices { get; set; } = true;
  public bool OnlyShowKnownArtisanMachines { get; set; } = true;
  public int MaxArtisanRows { get; set; } = 10;
  public bool ShowInventoryItemBundleBanner { get; set; } = true;
  public bool ShowInventoryItemDonationStatus { get; set; } = true;
  public bool ShowInventoryItemShippingStatus { get; set; } = true;
  public bool UseShippingBinIcon { get; set; } = false;
  public bool ShowLuckIcon { get; set; } = true;
  public bool ShowTravelingMerchant { get; set; } = true;
  public bool ShowBookseller { get; set; } = true;
  public bool ShowRainyDay { get; set; } = true;
  public bool ShowWorldTooltip { get; set; } = true;
  public bool ShowCropTooltip { get; set; } = true;
  public bool ShowTreeTooltip { get; set; } = true;
  public bool ShowBarrelTooltip { get; set; } = true;
  public bool ShowFishPondTooltip { get; set; } = true;
  public bool ShowAnimalBuildingTooltip { get; set; } = true;
  public KeybindList AnimalBuildingTooltipKeybind { get; set; } =
    KeybindList.ForSingle(SButton.LeftControl);
  public bool ShowForageableTooltip { get; set; } = true;
  public bool ShowChestTooltip { get; set; } = true;
  public bool ShowArtifactSpotTooltip { get; set; } = true;
  public bool ShowGarbageCanTooltip { get; set; } = true;
  public bool ShowShaftDestination { get; set; } = true;
  public bool ShowHarvestQuality { get; set; } = true;
  public int MachineProcessingIconsMode { get; set; } = 1;
  public bool MachineProcessingIconsVisible { get; set; } = true;
  public KeybindList ToggleMachineProcessingIcons { get; set; } =
    KeybindList.ForSingle(SButton.F10);
  public bool ShowFishPondIcons { get; set; } = false;
  public bool ShowBirthdayIcon { get; set; } = true;
  public bool ShowAnimalsNeedPets { get; set; } = true;
  public bool HideAnimalPetOnMaxFriendship { get; set; } = true;
  public bool ShowItemEffectRanges { get; set; } = true;
  public bool ShowPlacedItemRanges { get; set; } = true;
  public bool ButtonControlShow { get; set; } = true;
  public bool ShowRangeTooltip { get; set; } = true;
  public bool ShowBombRange { get; set; } = true;
  public bool ShowHarvestPricesInShop { get; set; } = true;
  public bool ShowMailboxCount { get; set; } = true;
  public bool DisplayCalendarAndBillboard { get; set; } = true;
  public bool ShowWhenNewRecipesAreAvailable { get; set; } = true;
  public bool ShowRecipeItemIcon { get; set; } = true;
  public bool ShowToolUpgradeStatus { get; set; } = true;
  public bool HideMerchantWhenVisited { get; set; } = false;
  public bool ShowMerchantBundleIcon { get; set; } = true;
  public bool ShowMerchantBundleItemNames { get; set; } = false;
  public bool HideBooksellerWhenVisited { get; set; } = false;
  public int LuckIconStyle { get; set; } = 0;
  public bool ShowExactValue { get; set; } = false;
  public bool RequireTvForLuck { get; set; } = false;
  public bool RequireTvForWeather { get; set; } = false;
  public bool ShowRobinBuildingStatusIcon { get; set; } = true;
  public bool ShowSeasonalBerry { get; set; } = true;
  public bool ShowSeasonalBerryHazelnut { get; set; } = true;
  public bool HideBirthdayIfFullFriendShip { get; set; } = true;
  public bool UseStackedBirthdayIcons { get; set; } = false;
  public bool ShowBirthdaysForUnmetVillagers { get; set; } = true;
  public bool ShowUnrevealedBirthdayLoves { get; set; } = true;
  public KeybindList ExpandBirthdayLovesKeybind { get; set; } =
    KeybindList.ForSingle(SButton.LeftShift);
  public bool ShowQuestCount { get; set; } = true;
  public bool ShowQuestLastDayReminder { get; set; } = true;
  public bool ShowGoldenWalnutCount { get; set; } = true;
  public bool ShowGoldenWalnutAnywhere { get; set; } = false;
  public bool GoldenWalnutFadeOut { get; set; } = false;
  public bool UseVerticalIconLayout { get; set; } = false;
  public int IconsPerRow { get; set; } = 10;
  public bool ShowFestivalIcon { get; set; } = true;
  public bool ShowCraneGameIcon { get; set; } = true;
  public bool ShowFishOnCatch { get; set; } = false;
  public bool ShowFishQualityStar { get; set; } = false;
  public bool ShowBuffTimers { get; set; } = true;
  public bool PlayBuffExpireSound { get; set; } = true;
  public int BuffIconSize { get; set; } = 0; // 0 = Normal, 1 = Smaller, 2 = Hidden
  public bool ShowCustomIcons { get; set; } = true;
  public bool ShowItemQualityOnPickup { get; set; } = true;
  public bool ShowLockedBundleItems { get; set; } = false;
  public bool ShowGrangeScore { get; set; } = true;
  public bool ShowGrangePrize { get; set; } = true;
  public Dictionary<string, int> IconOrder { get; set; } =
    new()
    {
      ["Luck"] = 1,
      ["Weather"] = 2,
      ["Birthday"] = 3,
      ["Festival"] = 4,
      ["QueenOfSauce"] = 5,
      ["ToolUpgrade"] = 6,
      ["RobinBuilding"] = 7,
      ["SeasonalBerry"] = 8,
      ["TravelingMerchant"] = 9,
      ["Bookseller"] = 10,
      ["CustomIcons"] = 11,
    };
}
