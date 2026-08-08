using System;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Compatibility.Helpers;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Infrastructure.Extensions;
using UIInfoSuite2Alt.Infrastructure.Helpers;
using UIInfoSuite2Alt.Patches;

namespace UIInfoSuite2Alt;

public partial class ModEntry
{
  private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
  {
    SoundHelper.Instance.Initialize(Helper);

    // Register mod compatibility APIs
    var configMenu = ApiManager.TryRegisterApi<IGenericModConfigMenuApi>(Helper, ModCompat.Gmcm);

    ApiManager.TryRegisterApi<ISpaceCoreApi>(Helper, ModCompat.SpaceCore);
    ApiManager.TryRegisterApi<ICustomBushApi>(Helper, ModCompat.CustomBush, "1.5.2");
    ApiManager.TryRegisterApi<ICloudySkiesApi>(Helper, ModCompat.CloudySkies);
    ApiManager.TryRegisterApi<IBetterGameMenuApi>(Helper, ModCompat.BetterGameMenu);
    ApiManager.TryRegisterApi<IFerngillSimpleEconomyApi>(Helper, ModCompat.FerngillEconomy);
    ApiManager.TryRegisterApi<IBetterJunimosApi>(Helper, ModCompat.BetterJunimos);
    ApiManager.TryRegisterApi<IWalkOfLifeApi>(Helper, ModCompat.WalkOfLife);
    ApiManager.TryRegisterApi<IExtraMachineConfigApi>(Helper, ModCompat.ExtraMachineConfig);

    WalkOfLifeHelper.Initialize(Helper);
    FerngillEconomyHelper.Initialize(Helper);

    UnlockableBundleHelper.Initialize(Helper);
    BundleHelper.ShowLockedBundles = ModConfig.ShowLockedBundleItems;

    InformantHelper.Initialize(Helper);

    if (Helper.ModRegistry.IsLoaded(ModCompat.LauncherDrawer))
    {
      LauncherDrawerHelper.Initialize(Monitor);
    }

    LogModRecommendations(Helper);

    if (configMenu is null)
    {
      return;
    }

    // Register GMCM
    configMenu.Register(
      ModManifest,
      reset: () => ModConfig = new Options.ModConfig(),
      save: () =>
      {
        Helper.WriteConfig(ModConfig);
        ApplyFeatures();
      }
    );

    // Global settings
    configMenu.AddBoolOption(
      ModManifest,
      name: () => I18n.Bool_ShowOptionsTabInMenu_DisplayedName(),
      tooltip: () => I18n.Bool_ShowOptionsTabInMenu_Tooltip(),
      getValue: () => ModConfig.ShowOptionsTabInMenu,
      setValue: value => ModConfig.ShowOptionsTabInMenu = value
    );

    // Keybinds
    configMenu.AddSectionTitle(ModManifest, text: () => I18n.Section_Keybinds());

    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_OpenCalendarKeybind_DisplayedName(),
      tooltip: () => I18n.Keybinds_OpenCalendarKeybind_Tooltip(),
      getValue: () => ModConfig.OpenCalendarKeybind,
      setValue: value => ModConfig.OpenCalendarKeybind = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_OpenQuestBoardKeybind_DisplayedName(),
      tooltip: () => I18n.Keybinds_OpenQuestBoardKeybind_Tooltip(),
      getValue: () => ModConfig.OpenQuestBoardKeybind,
      setValue: value => ModConfig.OpenQuestBoardKeybind = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_OpenSpecialOrdersBoardKeybind_DisplayedName(),
      tooltip: () => I18n.Keybinds_OpenSpecialOrdersBoardKeybind_Tooltip(),
      getValue: () => ModConfig.OpenSpecialOrdersBoardKeybind,
      setValue: value => ModConfig.OpenSpecialOrdersBoardKeybind = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_OpenQiSpecialOrdersBoardKeybind_DisplayedName(),
      tooltip: () => I18n.Keybinds_OpenQiSpecialOrdersBoardKeybind_Tooltip(),
      getValue: () => ModConfig.OpenQiSpecialOrdersBoardKeybind,
      setValue: value => ModConfig.OpenQiSpecialOrdersBoardKeybind = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_HideTreesKeybind_DisplayedName(),
      tooltip: () => I18n.Keybinds_HideTreesKeybind_Tooltip(),
      getValue: () => ModConfig.HideTreesKeybind,
      setValue: value => ModConfig.HideTreesKeybind = value
    );
    configMenu.AddBoolOption(
      ModManifest,
      name: () => "  > " + I18n.Bool_ShowHideTreesBanner_DisplayedName(),
      tooltip: () => I18n.Bool_ShowHideTreesBanner_Tooltip(),
      getValue: () => ModConfig.ShowHideTreesBanner,
      setValue: value => ModConfig.ShowHideTreesBanner = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_OpenModOptionsKeybind_DisplayedName(),
      tooltip: () => I18n.Keybinds_OpenModOptionsKeybind_Tooltip(),
      getValue: () => ModConfig.OpenModOptionsKeybind,
      setValue: value => ModConfig.OpenModOptionsKeybind = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_OpenMonsterEradicationKeybind_DisplayedName(),
      tooltip: () => I18n.Keybinds_OpenMonsterEradicationKeybind_Tooltip(),
      getValue: () => ModConfig.OpenMonsterEradicationKeybind,
      setValue: value => ModConfig.OpenMonsterEradicationKeybind = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_ToggleMachineProcessingIcons_DisplayedName(),
      tooltip: () => I18n.Keybinds_ToggleMachineProcessingIcons_Tooltip(),
      getValue: () => ModConfig.ToggleMachineProcessingIcons,
      setValue: value => ModConfig.ToggleMachineProcessingIcons = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_ShowOneRange_DisplayedName(),
      tooltip: () => I18n.Keybinds_ShowOneRange_Tooltip(),
      getValue: () => ModConfig.ShowOneRange,
      setValue: value => ModConfig.ShowOneRange = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_ShowAllRange_DisplayedName(),
      tooltip: () => I18n.Keybinds_ShowAllRange_Tooltip(),
      getValue: () => ModConfig.ShowAllRange,
      setValue: value => ModConfig.ShowAllRange = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_AnimalBuildingTooltipKeybind_DisplayedName(),
      tooltip: () => I18n.Keybinds_AnimalBuildingTooltipKeybind_Tooltip(),
      getValue: () => ModConfig.AnimalBuildingTooltipKeybind,
      setValue: value => ModConfig.AnimalBuildingTooltipKeybind = value
    );
    configMenu.AddKeybindList(
      ModManifest,
      name: () => I18n.Keybinds_ExpandBirthdayLovesKeybind_DisplayedName(),
      tooltip: () => I18n.Keybinds_ExpandBirthdayLovesKeybind_Tooltip(),
      getValue: () => ModConfig.ExpandBirthdayLovesKeybind,
      setValue: value => ModConfig.ExpandBirthdayLovesKeybind = value
    );
    RegisterGmcmFeatureToggles(configMenu);
  }

  private void RegisterGmcmFeatureToggles(IGenericModConfigMenuApi configMenu)
  {
    // Helpers to reduce boilerplate
    void AddBool(string key, Func<bool> get, Action<bool> set) =>
      configMenu.AddBoolOption(
        ModManifest,
        name: () => Helper.SafeGetString(key),
        getValue: get,
        setValue: set
      );

    void AddSubBool(string key, Func<bool> get, Action<bool> set) =>
      configMenu.AddBoolOption(
        ModManifest,
        name: () => "  > " + Helper.SafeGetString(key),
        getValue: get,
        setValue: set
      );

    void Spacer(int count = 1)
    {
      for (int i = 0; i < count; i++)
        configMenu.AddParagraph(ModManifest, text: () => "");
    }

    // Cache banner textures (fallback to 1px transparent if missing)
    Texture2D bannerHud = AssetHelper.TryLoadTexture(Helper, "assets/banner_hud.png");
    Texture2D bannerFfield = AssetHelper.TryLoadTexture(Helper, "assets/banner_ffield.png");
    Texture2D bannerExp = AssetHelper.TryLoadTexture(Helper, "assets/banner_exp.png");
    Texture2D bannerItems = AssetHelper.TryLoadTexture(Helper, "assets/banner_items.png");
    Texture2D bannerNpc = AssetHelper.TryLoadTexture(Helper, "assets/banner_npc.png");

    Spacer(3);
    // --- Main page: page links with banners ---
    configMenu.AddPageLink(ModManifest, "hud-icons", () => I18n.Section_HudIcons());
    configMenu.AddImage(ModManifest, () => bannerHud, scale: 1);
    configMenu.AddPageLink(ModManifest, "farm-field", () => I18n.Section_FarmAndField());
    configMenu.AddImage(ModManifest, () => bannerFfield, scale: 1);
    configMenu.AddPageLink(
      ModManifest,
      "experience-skills",
      () => I18n.Section_ExperienceAndSkills()
    );
    configMenu.AddImage(ModManifest, () => bannerExp, scale: 1);
    configMenu.AddPageLink(ModManifest, "items-shopping", () => I18n.Section_ItemsAndShopping());
    configMenu.AddImage(ModManifest, () => bannerItems, scale: 1);
    configMenu.AddPageLink(ModManifest, "npc-social", () => I18n.Section_NpcAndSocial());
    configMenu.AddImage(ModManifest, () => bannerNpc, scale: 1);
    // =====================
    // HUD Icons page
    // =====================
    configMenu.AddPage(ModManifest, "hud-icons", () => I18n.Section_HudIcons());
    configMenu.AddSectionTitle(ModManifest, text: () => I18n.Section_HudIcons());
    configMenu.AddImage(ModManifest, () => bannerHud, scale: 1);

    AddBool(
      nameof(ModConfig.UseVerticalIconLayout),
      () => ModConfig.UseVerticalIconLayout,
      v => ModConfig.UseVerticalIconLayout = v
    );

    string[] iconsPerRowValues = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
    configMenu.AddTextOption(
      ModManifest,
      name: () => Helper.SafeGetString(nameof(ModConfig.IconsPerRow)),
      getValue: () => ModConfig.IconsPerRow.ToString(),
      setValue: v => ModConfig.IconsPerRow = int.Parse(v),
      allowedValues: iconsPerRowValues
    );

    AddBool(
      nameof(ModConfig.ShowLuckIcon),
      () => ModConfig.ShowLuckIcon,
      v => ModConfig.ShowLuckIcon = v
    );

    string[] luckIconStyles = { "0", "1", "2" };
    configMenu.AddTextOption(
      ModManifest,
      name: () => "  > " + Helper.SafeGetString(nameof(ModConfig.LuckIconStyle)),
      getValue: () => ModConfig.LuckIconStyle.ToString(),
      setValue: v => ModConfig.LuckIconStyle = int.Parse(v),
      allowedValues: luckIconStyles,
      formatAllowedValue: v =>
        int.Parse(v) switch
        {
          0 => I18n.LuckIconStyle_Clover(),
          1 => I18n.LuckIconStyle_Dice(),
          2 => I18n.LuckIconStyle_TvFortune(),
          _ => v,
        }
    );
    AddSubBool(
      nameof(ModConfig.ShowExactValue),
      () => ModConfig.ShowExactValue,
      v => ModConfig.ShowExactValue = v
    );
    AddSubBool(
      nameof(ModConfig.RequireTvForLuck),
      () => ModConfig.RequireTvForLuck,
      v => ModConfig.RequireTvForLuck = v
    );
    Spacer();
    AddBool(
      nameof(ModConfig.ShowRainyDay),
      () => ModConfig.ShowRainyDay,
      v => ModConfig.ShowRainyDay = v
    );
    AddSubBool(
      nameof(ModConfig.RequireTvForWeather),
      () => ModConfig.RequireTvForWeather,
      v => ModConfig.RequireTvForWeather = v
    );
    Spacer();
    AddBool(
      nameof(ModConfig.ShowBirthdayIcon),
      () => ModConfig.ShowBirthdayIcon,
      v => ModConfig.ShowBirthdayIcon = v
    );
    AddSubBool(
      nameof(ModConfig.HideBirthdayIfFullFriendShip),
      () => ModConfig.HideBirthdayIfFullFriendShip,
      v => ModConfig.HideBirthdayIfFullFriendShip = v
    );
    AddSubBool(
      nameof(ModConfig.UseStackedBirthdayIcons),
      () => ModConfig.UseStackedBirthdayIcons,
      v => ModConfig.UseStackedBirthdayIcons = v
    );
    AddSubBool(
      nameof(ModConfig.ShowBirthdaysForUnmetVillagers),
      () => ModConfig.ShowBirthdaysForUnmetVillagers,
      v => ModConfig.ShowBirthdaysForUnmetVillagers = v
    );
    AddSubBool(
      nameof(ModConfig.ShowUnrevealedBirthdayLoves),
      () => ModConfig.ShowUnrevealedBirthdayLoves,
      v => ModConfig.ShowUnrevealedBirthdayLoves = v
    );
    configMenu.AddParagraph(ModManifest, text: () => I18n.ShowUnrevealedBirthdayLoves_Note());
    Spacer();
    AddBool(
      nameof(ModConfig.ShowTravelingMerchant),
      () => ModConfig.ShowTravelingMerchant,
      v => ModConfig.ShowTravelingMerchant = v
    );
    AddSubBool(
      nameof(ModConfig.HideMerchantWhenVisited),
      () => ModConfig.HideMerchantWhenVisited,
      v => ModConfig.HideMerchantWhenVisited = v
    );
    AddSubBool(
      nameof(ModConfig.ShowMerchantBundleIcon),
      () => ModConfig.ShowMerchantBundleIcon,
      v => ModConfig.ShowMerchantBundleIcon = v
    );
    AddSubBool(
      nameof(ModConfig.ShowMerchantBundleItemNames),
      () => ModConfig.ShowMerchantBundleItemNames,
      v => ModConfig.ShowMerchantBundleItemNames = v
    );
    Spacer();
    AddBool(
      nameof(ModConfig.ShowBookseller),
      () => ModConfig.ShowBookseller,
      v => ModConfig.ShowBookseller = v
    );
    AddSubBool(
      nameof(ModConfig.HideBooksellerWhenVisited),
      () => ModConfig.HideBooksellerWhenVisited,
      v => ModConfig.HideBooksellerWhenVisited = v
    );
    Spacer();
    AddBool(
      nameof(ModConfig.ShowFestivalIcon),
      () => ModConfig.ShowFestivalIcon,
      v => ModConfig.ShowFestivalIcon = v
    );
    AddBool(
      nameof(ModConfig.ShowCraneGameIcon),
      () => ModConfig.ShowCraneGameIcon,
      v => ModConfig.ShowCraneGameIcon = v
    );
    AddBool(
      nameof(ModConfig.ShowWhenNewRecipesAreAvailable),
      () => ModConfig.ShowWhenNewRecipesAreAvailable,
      v => ModConfig.ShowWhenNewRecipesAreAvailable = v
    );
    AddSubBool(
      nameof(ModConfig.ShowRecipeItemIcon),
      () => ModConfig.ShowRecipeItemIcon,
      v => ModConfig.ShowRecipeItemIcon = v
    );
    Spacer();
    AddBool(
      nameof(ModConfig.ShowToolUpgradeStatus),
      () => ModConfig.ShowToolUpgradeStatus,
      v => ModConfig.ShowToolUpgradeStatus = v
    );
    AddBool(
      nameof(ModConfig.ShowRobinBuildingStatusIcon),
      () => ModConfig.ShowRobinBuildingStatusIcon,
      v => ModConfig.ShowRobinBuildingStatusIcon = v
    );
    AddBool(
      nameof(ModConfig.ShowSeasonalBerry),
      () => ModConfig.ShowSeasonalBerry,
      v => ModConfig.ShowSeasonalBerry = v
    );
    AddSubBool(
      nameof(ModConfig.ShowSeasonalBerryHazelnut),
      () => ModConfig.ShowSeasonalBerryHazelnut,
      v => ModConfig.ShowSeasonalBerryHazelnut = v
    );
    Spacer();
    AddBool(
      nameof(ModConfig.ShowQuestCount),
      () => ModConfig.ShowQuestCount,
      v => ModConfig.ShowQuestCount = v
    );
    AddBool(
      nameof(ModConfig.ShowQuestLastDayReminder),
      () => ModConfig.ShowQuestLastDayReminder,
      v => ModConfig.ShowQuestLastDayReminder = v
    );
    AddBool(
      nameof(ModConfig.ShowWeddingReminder),
      () => ModConfig.ShowWeddingReminder,
      v => ModConfig.ShowWeddingReminder = v
    );
    AddBool(
      nameof(ModConfig.ShowGoldenWalnutCount),
      () => ModConfig.ShowGoldenWalnutCount,
      v => ModConfig.ShowGoldenWalnutCount = v
    );
    AddSubBool(
      nameof(ModConfig.ShowGoldenWalnutAnywhere),
      () => ModConfig.ShowGoldenWalnutAnywhere,
      v => ModConfig.ShowGoldenWalnutAnywhere = v
    );
    AddSubBool(
      nameof(ModConfig.GoldenWalnutFadeOut),
      () => ModConfig.GoldenWalnutFadeOut,
      v => ModConfig.GoldenWalnutFadeOut = v
    );
    Spacer();
    string[] buffIconSizes = { "0", "1", "2" };
    configMenu.AddTextOption(
      ModManifest,
      name: () => Helper.SafeGetString(nameof(ModConfig.BuffIconSize)),
      getValue: () => ModConfig.BuffIconSize.ToString(),
      setValue: v => ModConfig.BuffIconSize = int.Parse(v),
      allowedValues: buffIconSizes,
      formatAllowedValue: v =>
        int.Parse(v) switch
        {
          0 => I18n.BuffIconSize_Normal(),
          1 => I18n.BuffIconSize_Smaller(),
          2 => I18n.BuffIconSize_Hidden(),
          _ => v,
        }
    );
    Spacer();
    AddBool(
      nameof(ModConfig.ShowBuffTimers),
      () => ModConfig.ShowBuffTimers,
      v => ModConfig.ShowBuffTimers = v
    );
    if (Constants.TargetPlatform != GamePlatform.Android)
    {
      AddSubBool(
        nameof(ModConfig.PlayBuffExpireSound),
        () => ModConfig.PlayBuffExpireSound,
        v => ModConfig.PlayBuffExpireSound = v
      );
    }
    Spacer();
    AddBool(
      nameof(ModConfig.ShowCustomIcons),
      () => ModConfig.ShowCustomIcons,
      v => ModConfig.ShowCustomIcons = v
    );

    // =====================
    // Farm & Field page
    // =====================
    configMenu.AddPage(ModManifest, "farm-field", () => I18n.Section_FarmAndField());
    configMenu.AddSectionTitle(ModManifest, text: () => I18n.Section_FarmAndField());
    configMenu.AddImage(ModManifest, () => bannerFfield, scale: 1);

    AddBool(
      nameof(ModConfig.ShowAnimalsNeedPets),
      () => ModConfig.ShowAnimalsNeedPets,
      v => ModConfig.ShowAnimalsNeedPets = v
    );
    AddSubBool(
      nameof(ModConfig.HideAnimalPetOnMaxFriendship),
      () => ModConfig.HideAnimalPetOnMaxFriendship,
      v => ModConfig.HideAnimalPetOnMaxFriendship = v
    );
    Spacer();
    AddBool(
      nameof(ModConfig.ShowWorldTooltip),
      () => ModConfig.ShowWorldTooltip,
      v => ModConfig.ShowWorldTooltip = v
    );
    AddSubBool(
      nameof(ModConfig.ShowCropTooltip),
      () => ModConfig.ShowCropTooltip,
      v => ModConfig.ShowCropTooltip = v
    );
    AddSubBool(
      nameof(ModConfig.ShowTreeTooltip),
      () => ModConfig.ShowTreeTooltip,
      v => ModConfig.ShowTreeTooltip = v
    );
    AddSubBool(
      nameof(ModConfig.ShowBarrelTooltip),
      () => ModConfig.ShowBarrelTooltip,
      v => ModConfig.ShowBarrelTooltip = v
    );
    AddSubBool(
      nameof(ModConfig.ShowFishPondTooltip),
      () => ModConfig.ShowFishPondTooltip,
      v => ModConfig.ShowFishPondTooltip = v
    );
    AddSubBool(
      nameof(ModConfig.ShowAnimalBuildingTooltip),
      () => ModConfig.ShowAnimalBuildingTooltip,
      v => ModConfig.ShowAnimalBuildingTooltip = v
    );
    AddSubBool(
      nameof(ModConfig.ShowForageableTooltip),
      () => ModConfig.ShowForageableTooltip,
      v => ModConfig.ShowForageableTooltip = v
    );
    AddSubBool(
      nameof(ModConfig.ShowChestTooltip),
      () => ModConfig.ShowChestTooltip,
      v => ModConfig.ShowChestTooltip = v
    );
    AddSubBool(
      nameof(ModConfig.ShowArtifactSpotTooltip),
      () => ModConfig.ShowArtifactSpotTooltip,
      v => ModConfig.ShowArtifactSpotTooltip = v
    );
    AddSubBool(
      nameof(ModConfig.ShowGarbageCanTooltip),
      () => ModConfig.ShowGarbageCanTooltip,
      v => ModConfig.ShowGarbageCanTooltip = v
    );
    AddSubBool(
      nameof(ModConfig.ShowShaftDestination),
      () => ModConfig.ShowShaftDestination,
      v => ModConfig.ShowShaftDestination = v
    );
    AddSubBool(
      nameof(ModConfig.ShowHarvestQuality),
      () => ModConfig.ShowHarvestQuality,
      v => ModConfig.ShowHarvestQuality = v
    );
    configMenu.AddParagraph(ModManifest, text: () => I18n.ShowHarvestQuality_Note());
    Spacer();
    string[] machineIconModes = { "0", "1", "2" };
    configMenu.AddTextOption(
      ModManifest,
      name: () => Helper.SafeGetString(nameof(ModConfig.MachineProcessingIconsMode)),
      getValue: () => ModConfig.MachineProcessingIconsMode.ToString(),
      setValue: v => ModConfig.MachineProcessingIconsMode = int.Parse(v),
      allowedValues: machineIconModes,
      formatAllowedValue: v =>
        int.Parse(v) switch
        {
          0 => I18n.MachineProcessingMode_Off(),
          1 => I18n.MachineProcessingMode_Toggle(),
          2 => I18n.MachineProcessingMode_Hold(),
          _ => v,
        }
    );
    AddBool(
      nameof(ModConfig.ShowFishPondIcons),
      () => ModConfig.ShowFishPondIcons,
      v => ModConfig.ShowFishPondIcons = v
    );
    configMenu.AddBoolOption(
      ModManifest,
      name: () => I18n.ShowItemEffectRanges(),
      getValue: () => ModConfig.ShowItemEffectRanges,
      setValue: v => ModConfig.ShowItemEffectRanges = v
    );
    AddSubBool(
      nameof(ModConfig.ShowPlacedItemRanges),
      () => ModConfig.ShowPlacedItemRanges,
      v => ModConfig.ShowPlacedItemRanges = v
    );
    Spacer();
    configMenu.AddBoolOption(
      ModManifest,
      name: () => I18n.ShowBombRange(),
      getValue: () => ModConfig.ShowBombRange,
      setValue: v => ModConfig.ShowBombRange = v
    );
    Spacer();
    configMenu.AddBoolOption(
      ModManifest,
      name: () => I18n.EnableItemRangeKeybinds(),
      getValue: () => ModConfig.ButtonControlShow,
      setValue: v => ModConfig.ButtonControlShow = v
    );
    AddSubBool(
      nameof(ModConfig.ShowRangeTooltip),
      () => ModConfig.ShowRangeTooltip,
      v => ModConfig.ShowRangeTooltip = v
    );
    configMenu.AddParagraph(
      ModManifest,
      text: () =>
        $"{I18n.Keybinds_ShowOneRange_DisplayedName()}:\n"
        + $"> {ModConfig.ShowOneRange}\n"
        + $"{I18n.Keybinds_ShowAllRange_DisplayedName()}:\n"
        + $"> {ModConfig.ShowAllRange}"
    );

    // =====================
    // Experience & Skills page
    // =====================
    configMenu.AddPage(ModManifest, "experience-skills", () => I18n.Section_ExperienceAndSkills());
    configMenu.AddSectionTitle(ModManifest, text: () => I18n.Section_ExperienceAndSkills());
    configMenu.AddImage(ModManifest, () => bannerExp, scale: 1);

    AddBool(
      nameof(ModConfig.ShowLevelUpAnimation),
      () => ModConfig.ShowLevelUpAnimation,
      v => ModConfig.ShowLevelUpAnimation = v
    );
    AddBool(
      nameof(ModConfig.ShowExperienceBar),
      () => ModConfig.ShowExperienceBar,
      v => ModConfig.ShowExperienceBar = v
    );
    AddBool(
      nameof(ModConfig.ShowExperienceGain),
      () => ModConfig.ShowExperienceGain,
      v => ModConfig.ShowExperienceGain = v
    );
    AddBool(
      nameof(ModConfig.AllowExperienceBarToFadeOut),
      () => ModConfig.AllowExperienceBarToFadeOut,
      v => ModConfig.AllowExperienceBarToFadeOut = v
    );
    AddBool(
      nameof(ModConfig.ShowFishOnCatch),
      () => ModConfig.ShowFishOnCatch,
      v => ModConfig.ShowFishOnCatch = v
    );
    AddSubBool(
      nameof(ModConfig.ShowFishQualityStar),
      () => ModConfig.ShowFishQualityStar,
      v => ModConfig.ShowFishQualityStar = v
    );

    // =====================
    // Items & Shopping page
    // =====================
    configMenu.AddPage(ModManifest, "items-shopping", () => I18n.Section_ItemsAndShopping());
    configMenu.AddSectionTitle(ModManifest, text: () => I18n.Section_ItemsAndShopping());
    configMenu.AddImage(ModManifest, () => bannerItems, scale: 1);

    if (!ShowItemQualityPatch.ExternalModLoaded)
    {
      AddBool(
        nameof(ModConfig.ShowItemQualityOnPickup),
        () => ModConfig.ShowItemQualityOnPickup,
        v => ModConfig.ShowItemQualityOnPickup = v
      );
    }

    AddBool(
      nameof(ModConfig.ShowExtraItemInformation),
      () => ModConfig.ShowExtraItemInformation,
      v => ModConfig.ShowExtraItemInformation = v
    );
    AddSubBool(
      nameof(ModConfig.ShowInventoryItemSellPrice),
      () => ModConfig.ShowInventoryItemSellPrice,
      v => ModConfig.ShowInventoryItemSellPrice = v
    );
    configMenu.AddParagraph(ModManifest, text: () => I18n.ShowInventoryItemSellPrice_Note());
    configMenu.AddBoolOption(
      ModManifest,
      name: () => "    > " + Helper.SafeGetString(nameof(ModConfig.GatePricesByPriceCatalogue)),
      getValue: () => ModConfig.GatePricesByPriceCatalogue,
      setValue: v => ModConfig.GatePricesByPriceCatalogue = v
    );
    configMenu.AddBoolOption(
      ModManifest,
      name: () => "    > " + Helper.SafeGetString(nameof(ModConfig.ShowInventoryItemArtisanPrices)),
      getValue: () => ModConfig.ShowInventoryItemArtisanPrices,
      setValue: v => ModConfig.ShowInventoryItemArtisanPrices = v
    );
    configMenu.AddBoolOption(
      ModManifest,
      name: () => "      " + Helper.SafeGetString(nameof(ModConfig.OnlyShowKnownArtisanMachines)),
      getValue: () => ModConfig.OnlyShowKnownArtisanMachines,
      setValue: v => ModConfig.OnlyShowKnownArtisanMachines = v
    );
    string[] maxArtisanRowValues = { "5", "10", "15", "20", "30", "50", "100" };
    configMenu.AddTextOption(
      ModManifest,
      name: () => "      " + Helper.SafeGetString(nameof(ModConfig.MaxArtisanRows)),
      getValue: () => ModConfig.MaxArtisanRows.ToString(),
      setValue: v => ModConfig.MaxArtisanRows = int.Parse(v),
      allowedValues: maxArtisanRowValues
    );
    AddSubBool(
      nameof(ModConfig.ShowInventoryItemBundleBanner),
      () => ModConfig.ShowInventoryItemBundleBanner,
      v => ModConfig.ShowInventoryItemBundleBanner = v
    );
    AddSubBool(
      nameof(ModConfig.ShowInventoryItemDonationStatus),
      () => ModConfig.ShowInventoryItemDonationStatus,
      v => ModConfig.ShowInventoryItemDonationStatus = v
    );
    AddSubBool(
      nameof(ModConfig.ShowInventoryItemShippingStatus),
      () => ModConfig.ShowInventoryItemShippingStatus,
      v => ModConfig.ShowInventoryItemShippingStatus = v
    );
    AddSubBool(
      nameof(ModConfig.UseShippingBinIcon),
      () => ModConfig.UseShippingBinIcon,
      v => ModConfig.UseShippingBinIcon = v
    );
    Spacer();
    AddBool(
      nameof(ModConfig.ShowHarvestPricesInShop),
      () => ModConfig.ShowHarvestPricesInShop,
      v => ModConfig.ShowHarvestPricesInShop = v
    );
    AddBool(
      nameof(ModConfig.ShowLockedBundleItems),
      () => ModConfig.ShowLockedBundleItems,
      v =>
      {
        ModConfig.ShowLockedBundleItems = v;
        BundleHelper.ShowLockedBundles = v;
      }
    );
    AddBool(
      nameof(ModConfig.ShowGrangeScore),
      () => ModConfig.ShowGrangeScore,
      v => ModConfig.ShowGrangeScore = v
    );
    AddSubBool(
      nameof(ModConfig.ShowGrangePrize),
      () => ModConfig.ShowGrangePrize,
      v => ModConfig.ShowGrangePrize = v
    );
    Spacer();

    // =====================
    // NPC & Social page
    // =====================
    configMenu.AddPage(ModManifest, "npc-social", () => I18n.Section_NpcAndSocial());
    configMenu.AddSectionTitle(ModManifest, text: () => I18n.Section_NpcAndSocial());
    configMenu.AddImage(ModManifest, () => bannerNpc, scale: 1);

    AddBool(
      nameof(ModConfig.ShowMailboxCount),
      () => ModConfig.ShowMailboxCount,
      v => ModConfig.ShowMailboxCount = v
    );
    AddBool(
      nameof(ModConfig.ShowHeartFills),
      () => ModConfig.ShowHeartFills,
      v => ModConfig.ShowHeartFills = v
    );
    AddBool(
      nameof(ModConfig.DisplayCalendarAndBillboard),
      () => ModConfig.DisplayCalendarAndBillboard,
      v => ModConfig.DisplayCalendarAndBillboard = v
    );

    // --- Icon Order (main page) ---
    configMenu.AddPage(ModManifest, "");
    Spacer(3);
    configMenu.AddSectionTitle(ModManifest, text: () => I18n.Section_IconOrder());
    configMenu.AddParagraph(ModManifest, text: () => I18n.Section_IconOrder_Subtitle());

    foreach (string key in IconHandler.IconKeys)
    {
      string capturedKey = key;

      configMenu.AddNumberOption(
        ModManifest,
        name: () =>
          capturedKey switch
          {
            "Luck" => I18n.IconOrder_Luck(),
            "Weather" => I18n.IconOrder_Weather(),
            "Birthday" => I18n.IconOrder_Birthday(),
            "Festival" => I18n.IconOrder_Festival(),
            "QueenOfSauce" => I18n.IconOrder_QueenOfSauce(),
            "ToolUpgrade" => I18n.IconOrder_ToolUpgrade(),
            "RobinBuilding" => I18n.IconOrder_RobinBuilding(),
            "SeasonalBerry" => I18n.IconOrder_SeasonalBerry(),
            "TravelingMerchant" => I18n.IconOrder_TravelingMerchant(),
            "Bookseller" => I18n.IconOrder_Bookseller(),
            "CustomIcons" => I18n.IconOrder_CustomIcons(),
            _ => capturedKey,
          },
        getValue: () => ModConfig.IconOrder.TryGetValue(capturedKey, out int v) ? v : 99,
        setValue: v => ModConfig.IconOrder[capturedKey] = v,
        min: 1,
        max: 20
      );
    }
  }
}
