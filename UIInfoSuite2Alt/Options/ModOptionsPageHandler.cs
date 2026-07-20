using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Compatibility.Helpers;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Infrastructure.Extensions;
using UIInfoSuite2Alt.Infrastructure.Helpers;
using UIInfoSuite2Alt.Patches;
using UIInfoSuite2Alt.UIElements;
using UIInfoSuite2Alt.UIElements.Experience;

namespace UIInfoSuite2Alt.Options;

/// <summary>Manages mod options page injection into GameMenu (vanilla + BGM) and feature loading.</summary>
internal class ModOptionsPageHandler : IDisposable
{
  private const int downNeighborInInventory = 10; // above 11th inventory cell
  internal const int ModTabSnapId = 77780;
  private const string optionsTabName = "uiinfosuite2";

  private const int bgmTabOrder = 170; // after Options (160), before Exit (200)

  private readonly PerScreen<bool> _changeToOurTabAfterTick = new(); // map page workaround (vanilla)
  private readonly List<IDisposable> _elementsToDispose;

  private readonly IModHelper _helper;
  private readonly bool _hasBgm;

  private readonly List<int> _instancesWithOptionsPageOpen = []; // window resize workaround (vanilla)
  private readonly PerScreen<IClickableMenu?> _lastMenu = new();

  private readonly PerScreen<int?> _lastMenuTab = new();

  // Mod options page added to GameMenu.pages (vanilla only)
  private readonly PerScreen<ModOptionsPage?> _modOptionsPage = new();

  private readonly PerScreen<ModOptionsPageButton?> _modOptionsPageButton = new();

  // Gamepad nav component for our tab (not added to GameMenu.tabs — breaks game logic). Vanilla only.
  private readonly PerScreen<ClickableComponent?> _modOptionsTab = new();
  private readonly PerScreen<string> _suppressedTabHoverText = new(() => "");

  private readonly PerScreen<int?> _modOptionsTabPageNumber = new();

  /// <summary>The visible options list passed to ModOptionsPage. Rebuilt on section toggle.</summary>
  private readonly List<ModOptionsElement> _optionsElements = [];

  // Collapsible section infrastructure
  private readonly List<ModOptionsElement> _topElements = [];
  private readonly List<OptionsSection> _sections = [];
  private readonly List<ModOptionsElement> _bottomElements = [];

  /// <summary>Tracks expanded/collapsed state per section across menu open/close within a session.</summary>
  private static readonly PerScreen<Dictionary<string, bool>> _sectionExpandedState = new(() => []);

  private record OptionsSection(
    string Id,
    ModOptionsSectionHeader Header,
    ModOptionsImage? Banner,
    List<ModOptionsElement> Children
  );

  private List<ModOptionsElement> _currentTarget = null!;

  private void OptionsSpacer(int rows = 1)
  {
    for (int i = 0; i < rows; i++)
    {
      _currentTarget.Add(new ModOptionsElement(""));
    }
  }

  private void RebuildVisibleList()
  {
    _optionsElements.Clear();
    _optionsElements.AddRange(_topElements);

    foreach (OptionsSection section in _sections)
    {
      _optionsElements.Add(section.Header);
      if (section.Banner != null)
      {
        _optionsElements.Add(section.Banner);
      }

      if (section.Header.IsExpanded)
      {
        _optionsElements.AddRange(section.Children);
      }
    }

    _optionsElements.AddRange(_bottomElements);

    // Clamp scroll on any active ModOptionsPage instances
    _modOptionsPage.Value?.ClampScrollPosition();
  }

  private void ToggleSection(string sectionId)
  {
    OptionsSection? section = _sections.Find(s => s.Id == sectionId);
    if (section == null)
    {
      return;
    }

    section.Header.IsExpanded = !section.Header.IsExpanded;

    // Persist state for session
    _sectionExpandedState.Value[sectionId] = section.Header.IsExpanded;
    RebuildVisibleList();
  }

  private void BeginSection(string sectionId, Func<string> title, Func<Texture2D>? bannerTexture)
  {
    bool isExpanded = _sectionExpandedState.Value.TryGetValue(sectionId, out bool saved) && saved;

    List<ModOptionsElement> children = [];
    Action onToggle = () => ToggleSection(sectionId);

    var header = new ModOptionsSectionHeader(title(), onToggle, isExpanded);

    ModOptionsImage? banner =
      bannerTexture != null
        ? new ModOptionsImage(bannerTexture, scale: 1, onClick: onToggle)
        : null;

    var section = new OptionsSection(sectionId, header, banner, children);
    _sections.Add(section);

    // Direct subsequent adds into this section's children list
    _currentTarget = children;
  }

  private readonly PerScreen<ModOptionsPageState?> _savedPageState = new();
  private bool ShowPersonalConfigButton => ModEntry.ModConfig.ShowOptionsTabInMenu;

  private static bool IsAndroid => Constants.TargetPlatform == GamePlatform.Android;

  // Android draws the entry as an inventory-page button (next to organize) instead of a tab-bar tab.
  private readonly PerScreen<Rectangle> _androidButtonBounds = new(() => Rectangle.Empty);

  private bool _addOurTabBeforeTick;
  private readonly PerScreen<bool> _switchToOurTabNextTick = new();
  private bool _windowResizing;

  public ModOptionsPageHandler(IModHelper helper, ModConfig config, Action saveConfig)
  {
    _helper = helper;
    _hasBgm = GameMenuHelper.HasBetterGameMenu;
    ModEntry.MonitorObject.LogOnce(
      $"ModOptionsPageHandler: initializing, betterGameMenu={_hasBgm}",
      LogLevel.Trace
    );

    // Persist config.json on each change
    Action<bool> Set(Action<bool> setter) =>
      v =>
      {
        setter(v);
        saveConfig();
      };
    Action<int> SetInt(Action<int> setter) =>
      v =>
      {
        setter(v);
        saveConfig();
      };

    helper.Events.Input.ButtonsChanged += OnButtonsChanged;

    if (_hasBgm)
    {
      // BGM handles tab UI natively — minimal event wiring needed
    }
    else
    {
      helper.Events.Input.ButtonPressed += OnButtonPressed;
      helper.Events.GameLoop.UpdateTicking += OnUpdateTicking;
      helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
      helper.Events.Display.RenderingActiveMenu += OnRenderingMenu;
      helper.Events.Display.RenderedActiveMenu += OnRenderedMenu;
      GameRunner.instance.Window.ClientSizeChanged += OnWindowClientSizeChanged;
      helper.Events.Display.WindowResized += OnWindowResized;
    }

    var luckOfDay = new ShowLuckOfDay(helper);
    var showBirthdayIcon = new ShowBirthdayIcon(helper);
    showBirthdayIcon.InitDisableOnMaxFriendshipOption(config.HideBirthdayIfFullFriendShip);
    showBirthdayIcon.InitShowUnmetVillagersOption(config.ShowBirthdaysForUnmetVillagers);
    var showAccurateHearts = new ShowAccurateHearts();
    var showWhenAnimalNeedsPet = new ShowWhenAnimalNeedsPet(
      helper,
      helper.ModRegistry.IsLoaded(ModCompat.BetterRanching)
    );
    var showCalendarAndBillboardOnGameMenuButton = new ShowCalendarAndBillboardOnGameMenuButton(
      helper
    );
    var showScarecrowAndSprinklerRange = new ShowItemEffectRanges(helper);
    var experienceBar = new ExperienceBar(helper);
    var showItemHoverInformation = new ShowItemHoverInformation(helper);
    var shopHarvestPrices = new ShowShopHarvestPrices(helper);
    var showQueenOfSauceIcon = new ShowQueenOfSauceIcon(helper);
    var showTravelingMerchant = new ShowTravelingMerchant(helper);
    var showBookseller = new ShowBookseller(helper);
    var showRainyDayIcon = new ShowRainyDayIcon(helper);
    var showMachineProcessingItem = new ShowMachineProcessingItem(helper);
    var showTileTooltips = new ShowTileTooltips(helper, showScarecrowAndSprinklerRange);
    var showArtifactSpotTooltip = new ShowArtifactSpotTooltip(helper);
    var showGarbageCanTooltip = new ShowGarbageCanTooltip(helper);
    var showShaftDestination = new ShowShaftDestination(helper);
    var showToolUpgradeStatus = new ShowToolUpgradeStatus(helper);
    var showRobinBuildingStatusIcon = new ShowRobinBuildingStatusIcon(helper);
    var showSeasonalBerry = new ShowSeasonalBerry(helper);
    var showQuestCount = new ShowQuestCount(helper);
    var showQuestLastDayReminder = new ShowQuestLastDayReminder(helper);
    var showGoldenWalnutCount = new ShowGoldenWalnutCount(helper);
    var showFestivalIcon = new ShowFestivalIcon(helper);
    var showCraneGameAvailable = new ShowCraneGameAvailable(helper);
    var showBuffTimers = new ShowBuffTimers(helper);
    var showCustomIcons = new ShowCustomIcons(helper);
    var showFishOnCatch = new ShowFishOnCatch();
    var showGrangeScore = new ShowGrangeScore(helper);
    var showMailboxCount = new ShowMailboxCount(helper);

    _elementsToDispose =
    [
      luckOfDay,
      showBirthdayIcon,
      showAccurateHearts,
      showWhenAnimalNeedsPet,
      showCalendarAndBillboardOnGameMenuButton,
      showScarecrowAndSprinklerRange,
      showItemHoverInformation,
      shopHarvestPrices,
      showQueenOfSauceIcon,
      showTravelingMerchant,
      showBookseller,
      showRainyDayIcon,
      showMachineProcessingItem,
      showTileTooltips,
      showArtifactSpotTooltip,
      showGarbageCanTooltip,
      showShaftDestination,
      showToolUpgradeStatus,
      showRobinBuildingStatusIcon,
      showSeasonalBerry,
      showQuestCount,
      showQuestLastDayReminder,
      showGoldenWalnutCount,
      showBuffTimers,
      showFestivalIcon,
      showCraneGameAvailable,
      showCustomIcons,
      showFishOnCatch,
      showGrangeScore,
      showMailboxCount,
      experienceBar,
    ];

    var whichOption = 1;

    // --- Top section (always visible) ---
    _currentTarget = _topElements;
    _currentTarget.Add(
      new ModOptionsElement($"UI Info Suite 2 Alt. {GetVersionString(helper)}", isCentered: true)
    );
    _currentTarget.Add(
      new ModOptionsElement(
        I18n.Paragraph_KeybindsInGmcm(),
        isSmallText: true,
        isCentered: true,
        isVertCentered: true
      )
    );

    if (ApiManager.GetApi<IGenericModConfigMenuApi>(ModCompat.Gmcm, out var gmcm))
    {
      IModInfo? modInfo = helper.ModRegistry.Get(helper.ModRegistry.ModID);
      if (modInfo != null)
      {
        _currentTarget.Add(
          new ModOptionsSmallButton(
            I18n.Button_OpenGmcmOptions(),
            whichOption++,
            () => gmcm.OpenModMenuAsChildMenu(modInfo.Manifest),
            isCentered: true
          )
        );
      }
    }
    else
    {
      _currentTarget.Add(
        new ModOptionsElement(
          I18n.SmallText_GmcmMissing(),
          isSmallText: true,
          isCentered: true,
          isVertCentered: true,
          textColor: Color.Red
        )
      );
    }

    // Cache banner textures upfront
    Texture2D bannerHud = AssetHelper.TryLoadTexture(_helper, "assets/banner_hud.png");
    Texture2D bannerField = AssetHelper.TryLoadTexture(_helper, "assets/banner_ffield.png");
    Texture2D bannerExp = AssetHelper.TryLoadTexture(_helper, "assets/banner_exp.png");
    Texture2D bannerItems = AssetHelper.TryLoadTexture(_helper, "assets/banner_items.png");
    Texture2D bannerNpc = AssetHelper.TryLoadTexture(_helper, "assets/banner_npc.png");

    // --- HUD Icons ---
    BeginSection("hud-icons", () => I18n.Section_HudIcons(), () => bannerHud);

    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.UseVerticalIconLayout)),
        whichOption++,
        v => IconHandler.Handler.UseVerticalLayout = v,
        () => config.UseVerticalIconLayout,
        Set(v => config.UseVerticalIconLayout = v)
      )
    );
    _currentTarget.Add(
      new ModOptionsDropdown(
        _helper.SafeGetString(nameof(config.IconsPerRow)),
        whichOption++,
        ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"],
        () => config.IconsPerRow - 1,
        SetInt(v =>
        {
          config.IconsPerRow = v + 1;
          IconHandler.Handler.IconsPerRow = v + 1;
        })
      )
    );
    var luckIcon = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowLuckIcon)),
      whichOption++,
      luckOfDay.ToggleOption,
      () => config.ShowLuckIcon,
      Set(v => config.ShowLuckIcon = v)
    );
    _currentTarget.Add(luckIcon);
    _currentTarget.Add(
      new ModOptionsDropdown(
        _helper.SafeGetString(nameof(config.LuckIconStyle)),
        whichOption++,
        new List<string>
        {
          I18n.LuckIconStyle_Clover(),
          I18n.LuckIconStyle_Dice(),
          I18n.LuckIconStyle_TvFortune(),
        },
        () => config.LuckIconStyle,
        SetInt(v =>
        {
          config.LuckIconStyle = v;
          luckOfDay.SetIconStyle(v);
        }),
        luckIcon
      )
    );
    luckOfDay.SetIconStyle(config.LuckIconStyle);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowExactValue)),
        whichOption++,
        luckOfDay.ToggleShowExactValueOption,
        () => config.ShowExactValue,
        Set(v => config.ShowExactValue = v),
        luckIcon
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.RequireTvForLuck)),
        whichOption++,
        luckOfDay.ToggleRequireTvOption,
        () => config.RequireTvForLuck,
        Set(v => config.RequireTvForLuck = v),
        luckIcon
      )
    );
    var rainyDayIcon = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowRainyDay)),
      whichOption++,
      showRainyDayIcon.ToggleOption,
      () => config.ShowRainyDay,
      Set(v => config.ShowRainyDay = v)
    );
    _currentTarget.Add(rainyDayIcon);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.RequireTvForWeather)),
        whichOption++,
        showRainyDayIcon.ToggleRequireTvOption,
        () => config.RequireTvForWeather,
        Set(v => config.RequireTvForWeather = v),
        rainyDayIcon
      )
    );
    var birthdayIcon = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowBirthdayIcon)),
      whichOption++,
      showBirthdayIcon.ToggleOption,
      () => config.ShowBirthdayIcon,
      Set(v => config.ShowBirthdayIcon = v)
    );
    _currentTarget.Add(birthdayIcon);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.HideBirthdayIfFullFriendShip)),
        whichOption++,
        showBirthdayIcon.ToggleDisableOnMaxFriendshipOption,
        () => config.HideBirthdayIfFullFriendShip,
        Set(v => config.HideBirthdayIfFullFriendShip = v),
        birthdayIcon
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.UseStackedBirthdayIcons)),
        whichOption++,
        showBirthdayIcon.ToggleStackedOption,
        () => config.UseStackedBirthdayIcons,
        Set(v => config.UseStackedBirthdayIcons = v),
        birthdayIcon
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowBirthdaysForUnmetVillagers)),
        whichOption++,
        showBirthdayIcon.ToggleShowUnmetVillagersOption,
        () => config.ShowBirthdaysForUnmetVillagers,
        Set(v => config.ShowBirthdaysForUnmetVillagers = v),
        birthdayIcon
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowUnrevealedBirthdayLoves)),
        whichOption++,
        showBirthdayIcon.ToggleShowUnrevealedLovesOption,
        () => config.ShowUnrevealedBirthdayLoves,
        Set(v => config.ShowUnrevealedBirthdayLoves = v),
        birthdayIcon
      )
    );
    _currentTarget.Add(
      new ModOptionsElement(
        I18n.ShowUnrevealedBirthdayLoves_Note(),
        isSmallText: true,
        isIndented: true
      )
    );

    var travellingMerchantIcon = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowTravelingMerchant)),
      whichOption++,
      showTravelingMerchant.ToggleOption,
      () => config.ShowTravelingMerchant,
      Set(v => config.ShowTravelingMerchant = v)
    );
    _currentTarget.Add(travellingMerchantIcon);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.HideMerchantWhenVisited)),
        whichOption++,
        showTravelingMerchant.ToggleHideWhenVisitedOption,
        () => config.HideMerchantWhenVisited,
        Set(v => config.HideMerchantWhenVisited = v),
        travellingMerchantIcon
      )
    );
    var merchantBundleIcon = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowMerchantBundleIcon)),
      whichOption++,
      showTravelingMerchant.ToggleShowBundleIconOption,
      () => config.ShowMerchantBundleIcon,
      Set(v => config.ShowMerchantBundleIcon = v),
      travellingMerchantIcon
    );
    _currentTarget.Add(merchantBundleIcon);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowMerchantBundleItemNames)),
        whichOption++,
        showTravelingMerchant.ToggleShowBundleItemNamesOption,
        () => config.ShowMerchantBundleItemNames,
        Set(v => config.ShowMerchantBundleItemNames = v),
        merchantBundleIcon
      )
    );
    var booksellerIcon = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowBookseller)),
      whichOption++,
      showBookseller.ToggleOption,
      () => config.ShowBookseller,
      Set(v => config.ShowBookseller = v)
    );
    _currentTarget.Add(booksellerIcon);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.HideBooksellerWhenVisited)),
        whichOption++,
        showBookseller.ToggleHideWhenVisitedOption,
        () => config.HideBooksellerWhenVisited,
        Set(v => config.HideBooksellerWhenVisited = v),
        booksellerIcon
      )
    );
    var festivalIconCheckbox = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowFestivalIcon)),
      whichOption++,
      showFestivalIcon.ToggleOption,
      () => config.ShowFestivalIcon,
      Set(v => config.ShowFestivalIcon = v)
    );
    _currentTarget.Add(festivalIconCheckbox);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowCraneGameIcon)),
        whichOption++,
        showCraneGameAvailable.ToggleOption,
        () => config.ShowCraneGameIcon,
        Set(v => config.ShowCraneGameIcon = v)
      )
    );
    var queenOfSauceCheckbox = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowWhenNewRecipesAreAvailable)),
      whichOption++,
      showQueenOfSauceIcon.ToggleOption,
      () => config.ShowWhenNewRecipesAreAvailable,
      Set(v => config.ShowWhenNewRecipesAreAvailable = v)
    );
    _currentTarget.Add(queenOfSauceCheckbox);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowRecipeItemIcon)),
        whichOption++,
        showQueenOfSauceIcon.ToggleShowRecipeItemIcon,
        () => config.ShowRecipeItemIcon,
        Set(v => config.ShowRecipeItemIcon = v),
        queenOfSauceCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowToolUpgradeStatus)),
        whichOption++,
        showToolUpgradeStatus.ToggleOption,
        () => config.ShowToolUpgradeStatus,
        Set(v => config.ShowToolUpgradeStatus = v)
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowRobinBuildingStatusIcon)),
        whichOption++,
        showRobinBuildingStatusIcon.ToggleOption,
        () => config.ShowRobinBuildingStatusIcon,
        Set(v => config.ShowRobinBuildingStatusIcon = v)
      )
    );
    var seasonalBerryIcon = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowSeasonalBerry)),
      whichOption++,
      showSeasonalBerry.ToggleOption,
      () => config.ShowSeasonalBerry,
      Set(v => config.ShowSeasonalBerry = v)
    );
    _currentTarget.Add(seasonalBerryIcon);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowSeasonalBerryHazelnut)),
        whichOption++,
        showSeasonalBerry.ToggleHazelnutOption,
        () => config.ShowSeasonalBerryHazelnut,
        Set(v => config.ShowSeasonalBerryHazelnut = v),
        seasonalBerryIcon
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowQuestCount)),
        whichOption++,
        showQuestCount.ToggleOption,
        () => config.ShowQuestCount,
        Set(v =>
        {
          config.ShowQuestCount = v;
          IconHandler.Handler.ShowQuestCount = v;
        })
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowQuestLastDayReminder)),
        whichOption++,
        showQuestLastDayReminder.ToggleOption,
        () => config.ShowQuestLastDayReminder,
        Set(v => config.ShowQuestLastDayReminder = v)
      )
    );
    var walnutCheckbox = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowGoldenWalnutCount)),
      whichOption++,
      showGoldenWalnutCount.ToggleOption,
      () => config.ShowGoldenWalnutCount,
      Set(v => config.ShowGoldenWalnutCount = v)
    );
    _currentTarget.Add(walnutCheckbox);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowGoldenWalnutAnywhere)),
        whichOption++,
        showGoldenWalnutCount.ToggleShowAnywhere,
        () => config.ShowGoldenWalnutAnywhere,
        Set(v => config.ShowGoldenWalnutAnywhere = v),
        walnutCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.GoldenWalnutFadeOut)),
        whichOption++,
        showGoldenWalnutCount.ToggleFadeOut,
        () => config.GoldenWalnutFadeOut,
        Set(v => config.GoldenWalnutFadeOut = v),
        walnutCheckbox
      )
    );
    BuffIconSizePatch.SetMode(config.BuffIconSize);
    _currentTarget.Add(
      new ModOptionsDropdown(
        _helper.SafeGetString(nameof(config.BuffIconSize)),
        whichOption++,
        new List<string>
        {
          I18n.BuffIconSize_Normal(),
          I18n.BuffIconSize_Smaller(),
          I18n.BuffIconSize_Hidden(),
        },
        () => config.BuffIconSize,
        SetInt(v =>
        {
          config.BuffIconSize = v;
          BuffIconSizePatch.SetMode(v);
        })
      )
    );
    var buffTimersCheckbox = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowBuffTimers)),
      whichOption++,
      showBuffTimers.ToggleOption,
      () => config.ShowBuffTimers,
      Set(v => config.ShowBuffTimers = v)
    );
    _currentTarget.Add(buffTimersCheckbox);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.PlayBuffExpireSound)),
        whichOption++,
        showBuffTimers.ToggleExpireSound,
        () => config.PlayBuffExpireSound,
        Set(v => config.PlayBuffExpireSound = v),
        buffTimersCheckbox
      )
    );

    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowCustomIcons)),
        whichOption++,
        showCustomIcons.ToggleOption,
        () => config.ShowCustomIcons,
        Set(v => config.ShowCustomIcons = v)
      )
    );

    // --- Farm & Field ---
    BeginSection("farm-field", () => I18n.Section_FarmAndField(), () => bannerField);

    var animalPetIcon = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowAnimalsNeedPets)),
      whichOption++,
      showWhenAnimalNeedsPet.ToggleOption,
      () => config.ShowAnimalsNeedPets,
      Set(v => config.ShowAnimalsNeedPets = v)
    );
    _currentTarget.Add(animalPetIcon);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.HideAnimalPetOnMaxFriendship)),
        whichOption++,
        showWhenAnimalNeedsPet.ToggleDisableOnMaxFriendshipOption,
        () => config.HideAnimalPetOnMaxFriendship,
        Set(v => config.HideAnimalPetOnMaxFriendship = v),
        animalPetIcon
      )
    );
    var showWorldTooltipCheckbox = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowWorldTooltip)),
      whichOption++,
      showTileTooltips.ToggleOption,
      () => config.ShowWorldTooltip,
      Set(v => config.ShowWorldTooltip = v)
    );
    _currentTarget.Add(showWorldTooltipCheckbox);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowCropTooltip)),
        whichOption++,
        _ => { },
        () => config.ShowCropTooltip,
        Set(v => config.ShowCropTooltip = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowTreeTooltip)),
        whichOption++,
        _ => { },
        () => config.ShowTreeTooltip,
        Set(v => config.ShowTreeTooltip = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowBarrelTooltip)),
        whichOption++,
        _ => { },
        () => config.ShowBarrelTooltip,
        Set(v => config.ShowBarrelTooltip = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowFishPondTooltip)),
        whichOption++,
        _ => { },
        () => config.ShowFishPondTooltip,
        Set(v => config.ShowFishPondTooltip = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowAnimalBuildingTooltip)),
        whichOption++,
        _ => { },
        () => config.ShowAnimalBuildingTooltip,
        Set(v => config.ShowAnimalBuildingTooltip = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowForageableTooltip)),
        whichOption++,
        _ => { },
        () => config.ShowForageableTooltip,
        Set(v => config.ShowForageableTooltip = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowChestTooltip)),
        whichOption++,
        _ => { },
        () => config.ShowChestTooltip,
        Set(v => config.ShowChestTooltip = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowArtifactSpotTooltip)),
        whichOption++,
        showArtifactSpotTooltip.ToggleOption,
        () => config.ShowArtifactSpotTooltip,
        Set(v => config.ShowArtifactSpotTooltip = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowGarbageCanTooltip)),
        whichOption++,
        showGarbageCanTooltip.ToggleOption,
        () => config.ShowGarbageCanTooltip,
        Set(v => config.ShowGarbageCanTooltip = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowShaftDestination)),
        whichOption++,
        showShaftDestination.ToggleOption,
        () => config.ShowShaftDestination,
        Set(v => config.ShowShaftDestination = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowHarvestQuality)),
        whichOption++,
        _ => { },
        () => config.ShowHarvestQuality,
        Set(v => config.ShowHarvestQuality = v),
        showWorldTooltipCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsElement(I18n.ShowHarvestQuality_Note(), isSmallText: true, isIndented: true)
    );

    showMachineProcessingItem.SetMode(config.MachineProcessingIconsMode);
    _currentTarget.Add(
      new ModOptionsDropdown(
        _helper.SafeGetString(nameof(config.MachineProcessingIconsMode)),
        whichOption++,
        new List<string>
        {
          I18n.MachineProcessingMode_Off(),
          I18n.MachineProcessingMode_Toggle(),
          I18n.MachineProcessingMode_Hold(),
        },
        () => config.MachineProcessingIconsMode,
        v =>
        {
          config.MachineProcessingIconsMode = v;
          saveConfig();
          showMachineProcessingItem.SetMode(v);
        }
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowFishPondIcons)),
        whichOption++,
        showMachineProcessingItem.ToggleFishPondOption,
        () => config.ShowFishPondIcons,
        Set(v => config.ShowFishPondIcons = v)
      )
    );
    var showItemEffectRanges = new ModOptionsCheckbox(
      I18n.ShowItemEffectRanges(),
      whichOption++,
      showScarecrowAndSprinklerRange.ToggleOption,
      () => config.ShowItemEffectRanges,
      Set(v => config.ShowItemEffectRanges = v)
    );
    _currentTarget.Add(showItemEffectRanges);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        I18n.ShowPlacedItemRanges(),
        whichOption++,
        showScarecrowAndSprinklerRange.ToggleShowPlacedItemRangesOption,
        () => config.ShowPlacedItemRanges,
        Set(v => config.ShowPlacedItemRanges = v),
        showItemEffectRanges
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        I18n.ShowBombRange(),
        whichOption++,
        showScarecrowAndSprinklerRange.ToggleShowBombRangeOption,
        () => config.ShowBombRange,
        Set(v => config.ShowBombRange = v)
      )
    );
    var enableItemRangeKeybinds = new ModOptionsCheckbox(
      I18n.EnableItemRangeKeybinds(),
      whichOption++,
      showScarecrowAndSprinklerRange.ToggleButtonControlShowOption,
      () => config.ButtonControlShow,
      Set(v => config.ButtonControlShow = v)
    );
    _currentTarget.Add(enableItemRangeKeybinds);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        I18n.ShowRangeTooltip(),
        whichOption++,
        showScarecrowAndSprinklerRange.ToggleShowRangeTooltipOption,
        () => config.ShowRangeTooltip,
        Set(v => config.ShowRangeTooltip = v),
        enableItemRangeKeybinds
      )
    );
    _currentTarget.Add(
      new ModOptionsElement(
        $"{I18n.Keybinds_ShowOneRange_DisplayedName()}:\n"
          + $"> {config.ShowOneRange}\n"
          + $"{I18n.Keybinds_ShowAllRange_DisplayedName()}:\n"
          + $"> {config.ShowAllRange}",
        isSmallText: true,
        isIndented: true
      )
    );

    // --- Experience & Skills ---
    BeginSection("experience-skills", () => I18n.Section_ExperienceAndSkills(), () => bannerExp);

    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowLevelUpAnimation)),
        whichOption++,
        experienceBar.ToggleLevelUpAnimation,
        () => config.ShowLevelUpAnimation,
        Set(v => config.ShowLevelUpAnimation = v)
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowExperienceBar)),
        whichOption++,
        experienceBar.ToggleShowExperienceBar,
        () => config.ShowExperienceBar,
        Set(v => config.ShowExperienceBar = v)
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowExperienceGain)),
        whichOption++,
        experienceBar.ToggleShowExperienceGain,
        () => config.ShowExperienceGain,
        Set(v => config.ShowExperienceGain = v)
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.AllowExperienceBarToFadeOut)),
        whichOption++,
        experienceBar.ToggleExperienceBarFade,
        () => config.AllowExperienceBarToFadeOut,
        Set(v => config.AllowExperienceBarToFadeOut = v)
      )
    );
    var fishOnCatchIcon = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowFishOnCatch)),
      whichOption++,
      showFishOnCatch.ToggleOption,
      () => config.ShowFishOnCatch,
      Set(v => config.ShowFishOnCatch = v)
    );
    _currentTarget.Add(fishOnCatchIcon);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowFishQualityStar)),
        whichOption++,
        showFishOnCatch.ToggleQualityStarOption,
        () => config.ShowFishQualityStar,
        Set(v => config.ShowFishQualityStar = v),
        fishOnCatchIcon
      )
    );

    // --- Items & Shopping ---
    BeginSection("items-shopping", () => I18n.Section_ItemsAndShopping(), () => bannerItems);

    if (!ShowItemQualityPatch.ExternalModLoaded)
    {
      _currentTarget.Add(
        new ModOptionsCheckbox(
          _helper.SafeGetString(nameof(config.ShowItemQualityOnPickup)),
          whichOption++,
          enabled => ShowItemQualityPatch.Enabled = enabled,
          () => config.ShowItemQualityOnPickup,
          Set(v => config.ShowItemQualityOnPickup = v)
        )
      );
    }

    var showItemHoverCheckbox = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowExtraItemInformation)),
      whichOption++,
      showItemHoverInformation.ToggleOption,
      () => config.ShowExtraItemInformation,
      Set(v => config.ShowExtraItemInformation = v)
    );
    _currentTarget.Add(showItemHoverCheckbox);
    var sellPriceCheckbox = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowInventoryItemSellPrice)),
      whichOption++,
      _ => { },
      () => config.ShowInventoryItemSellPrice,
      Set(v => config.ShowInventoryItemSellPrice = v),
      showItemHoverCheckbox
    );
    _currentTarget.Add(sellPriceCheckbox);
    _currentTarget.Add(
      new ModOptionsElement(
        I18n.ShowInventoryItemSellPrice_Note(),
        isSmallText: true,
        isIndented: true
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.GatePricesByPriceCatalogue)),
        whichOption++,
        _ => { },
        () => config.GatePricesByPriceCatalogue,
        Set(v => config.GatePricesByPriceCatalogue = v),
        sellPriceCheckbox
      )
    );
    var artisanPricesCheckbox = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowInventoryItemArtisanPrices)),
      whichOption++,
      _ => { },
      () => config.ShowInventoryItemArtisanPrices,
      Set(v => config.ShowInventoryItemArtisanPrices = v),
      sellPriceCheckbox
    );
    _currentTarget.Add(artisanPricesCheckbox);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.OnlyShowKnownArtisanMachines)),
        whichOption++,
        _ => { },
        () => config.OnlyShowKnownArtisanMachines,
        Set(v => config.OnlyShowKnownArtisanMachines = v),
        artisanPricesCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsDropdown(
        _helper.SafeGetString(nameof(config.MaxArtisanRows)),
        whichOption++,
        ["5", "10", "15", "20", "30", "50", "100"],
        () =>
          config.MaxArtisanRows switch
          {
            5 => 0,
            10 => 1,
            15 => 2,
            20 => 3,
            30 => 4,
            50 => 5,
            100 => 6,
            _ => 1,
          },
        SetInt(v =>
          config.MaxArtisanRows = v switch
          {
            0 => 5,
            1 => 10,
            2 => 15,
            3 => 20,
            4 => 30,
            5 => 50,
            6 => 100,
            _ => 10,
          }
        ),
        artisanPricesCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowInventoryItemBundleBanner)),
        whichOption++,
        _ => { },
        () => config.ShowInventoryItemBundleBanner,
        Set(v => config.ShowInventoryItemBundleBanner = v),
        showItemHoverCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowInventoryItemDonationStatus)),
        whichOption++,
        _ => { },
        () => config.ShowInventoryItemDonationStatus,
        Set(v => config.ShowInventoryItemDonationStatus = v),
        showItemHoverCheckbox
      )
    );
    var shippingStatusCheckbox = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowInventoryItemShippingStatus)),
      whichOption++,
      _ => { },
      () => config.ShowInventoryItemShippingStatus,
      Set(v => config.ShowInventoryItemShippingStatus = v),
      showItemHoverCheckbox
    );
    _currentTarget.Add(shippingStatusCheckbox);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.UseShippingBinIcon)),
        whichOption++,
        _ => { },
        () => config.UseShippingBinIcon,
        Set(v => config.UseShippingBinIcon = v),
        shippingStatusCheckbox
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowHarvestPricesInShop)),
        whichOption++,
        shopHarvestPrices.ToggleOption,
        () => config.ShowHarvestPricesInShop,
        Set(v => config.ShowHarvestPricesInShop = v)
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowLockedBundleItems)),
        whichOption++,
        v => BundleHelper.ShowLockedBundles = v,
        () => config.ShowLockedBundleItems,
        Set(v => config.ShowLockedBundleItems = v)
      )
    );
    var grangeScoreCheckbox = new ModOptionsCheckbox(
      _helper.SafeGetString(nameof(config.ShowGrangeScore)),
      whichOption++,
      showGrangeScore.ToggleOption,
      () => config.ShowGrangeScore,
      Set(v => config.ShowGrangeScore = v)
    );
    _currentTarget.Add(grangeScoreCheckbox);
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowGrangePrize)),
        whichOption++,
        v => showGrangeScore.ShowPrize = v,
        () => config.ShowGrangePrize,
        Set(v => config.ShowGrangePrize = v),
        grangeScoreCheckbox
      )
    );

    // --- NPC & Social ---
    BeginSection("npc-social", () => I18n.Section_NpcAndSocial(), () => bannerNpc);

    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowMailboxCount)),
        whichOption++,
        showMailboxCount.ToggleOption,
        () => config.ShowMailboxCount,
        Set(v => config.ShowMailboxCount = v)
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.ShowHeartFills)),
        whichOption++,
        showAccurateHearts.ToggleOption,
        () => config.ShowHeartFills,
        Set(v => config.ShowHeartFills = v)
      )
    );
    _currentTarget.Add(
      new ModOptionsCheckbox(
        _helper.SafeGetString(nameof(config.DisplayCalendarAndBillboard)),
        whichOption++,
        showCalendarAndBillboardOnGameMenuButton.ToggleOption,
        () => config.DisplayCalendarAndBillboard,
        Set(v => config.DisplayCalendarAndBillboard = v)
      )
    );

    // --- Icon Order (always visible) ---
    _currentTarget = _bottomElements;
    _currentTarget.Add(new ModOptionsElement(I18n.Section_IconOrder()));
    _currentTarget.Add(new ModOptionsElement(I18n.Section_IconOrder_Subtitle(), isSmallText: true));

    foreach (string key in IconHandler.IconKeys)
    {
      string label = key switch
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
        _ => key,
      };

      string capturedKey = key;
      _currentTarget.Add(
        new ModOptionsNumberPicker(
          label,
          whichOption++,
          () => config.IconOrder.TryGetValue(capturedKey, out int v) ? v : 99,
          SetInt(v => config.IconOrder[capturedKey] = v)
        )
      );
    }

    // Build the initial visible list from sections + expanded state
    RebuildVisibleList();

    if (_hasBgm)
    {
      RegisterBgmTab();
    }
  }

  public void Dispose()
  {
    foreach (IDisposable item in _elementsToDispose)
    {
      item.Dispose();
    }

    _modOptionsPage.Value?.Dispose();
    _modOptionsPage.Value = null;

    _helper.Events.Input.ButtonsChanged -= OnButtonsChanged;

    if (_hasBgm)
    {
      DisposeBgm();
    }
    else
    {
      _helper.Events.Input.ButtonPressed -= OnButtonPressed;
      _helper.Events.GameLoop.UpdateTicking -= OnUpdateTicking;
      _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
      _helper.Events.Display.RenderingActiveMenu -= OnRenderingMenu;
      _helper.Events.Display.RenderedActiveMenu -= OnRenderedMenu;
      GameRunner.instance.Window.ClientSizeChanged -= OnWindowClientSizeChanged;
      _helper.Events.Display.WindowResized -= OnWindowResized;
    }
  }

  #region Better Game Menu integration

  private void RegisterBgmTab()
  {
    if (
      !ApiManager.GetApi<IBetterGameMenuApi>(
        ModCompat.BetterGameMenu,
        out IBetterGameMenuApi? bgmApi
      )
    )
    {
      return;
    }

    // Register our tab with BGM
    Texture2D tabIcon = AssetHelper.TryLoadTexture(_helper, "assets/tab_icon.png");
    IBetterGameMenuApi.DrawDelegate iconDraw = bgmApi.CreateDraw(
      tabIcon,
      new Rectangle(0, 0, 16, 16),
      scale: 3f,
      offset: new Vector2(0, 6)
    );

    bgmApi.RegisterTab(
      id: optionsTabName,
      order: bgmTabOrder,
      getDisplayName: () => I18n.OptionsTabTooltip(),
      getIcon: () => (iconDraw, true),
      priority: 0,
      getPageInstance: menu => new ModOptionsPage(_optionsElements, _helper.Events, menu),
      getTabVisible: () => ShowPersonalConfigButton,
      getWidth: w => w,
      getHeight: h => h,
      onResize: ctx =>
      {
        (ctx.OldPage as ModOptionsPage)?.Dispose();
        return new ModOptionsPage(_optionsElements, _helper.Events, ctx.Menu);
      }
    );

    // Right-click opens GMCM if available
    bgmApi.OnTabContextMenu(OnBgmTabContextMenu);
  }

  private void OnBgmTabContextMenu(ITabContextMenuEvent evt)
  {
    if (evt.Tab != optionsTabName)
    {
      return;
    }

    if (
      ApiManager.GetApi<IGenericModConfigMenuApi>(
        ModCompat.Gmcm,
        out IGenericModConfigMenuApi? gmcm
      )
    )
    {
      IModInfo? modInfo = _helper.ModRegistry.Get(_helper.ModRegistry.ModID);
      if (modInfo != null)
      {
        evt.Entries.Add(
          evt.CreateEntry(I18n.OpenSettings(), () => gmcm.OpenModMenu(modInfo.Manifest))
        );
      }
    }
  }

  private void DisposeBgm()
  {
    if (
      ApiManager.GetApi<IBetterGameMenuApi>(
        ModCompat.BetterGameMenu,
        out IBetterGameMenuApi? bgmApi
      )
    )
    {
      bgmApi.UnregisterImplementation(optionsTabName);
      bgmApi.OffTabContextMenu(OnBgmTabContextMenu);
    }
  }

  #endregion

  #region Vanilla GameMenu support

  private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
  {
    if (!ShowPersonalConfigButton)
    {
      return;
    }

    if (IsAndroid)
    {
      HandleAndroidButtonPressed(e);
      return;
    }

    if (Game1.activeClickableMenu is GameMenu gameMenu)
    {
      // Right trigger → our tab (left trigger is handled by the game)
      if (e.Button == SButton.RightTrigger && !e.IsSuppressed())
      {
        if (gameMenu.currentTab + 1 == _modOptionsTabPageNumber.Value && gameMenu.readyToClose())
        {
          ChangeToOurTab(gameMenu);
          _helper.Input.Suppress(SButton.RightTrigger);
        }
      }

      // Based on GameMenu.receiveLeftClick / Game1.updateActiveMenu
      if ((e.Button == SButton.MouseLeft || e.Button == SButton.ControllerA) && !e.IsSuppressed())
      {
        // Workaround: map page calls GameMenu.changeTab which fails for our tab
        if (
          gameMenu.currentTab == GameMenu.mapTab
          && gameMenu.lastOpenedNonMapTab == _modOptionsTabPageNumber.Value
        )
        {
          _changeToOurTabAfterTick.Value = true;
          gameMenu.lastOpenedNonMapTab = GameMenu.optionsTab;
          ModEntry.MonitorObject.Log(
            $"ModOptionsPageHandler: map close workaround, currentTab={gameMenu.currentTab}, lastNonMap={_modOptionsTabPageNumber.Value}, button={e.Button}",
            LogLevel.Trace
          );
        }

        if (!gameMenu.invisible && !GameMenu.forcePreventClose)
        {
          const bool uiScale = true;
          if (
            _modOptionsTab.Value?.containsPoint(Game1.getMouseX(uiScale), Game1.getMouseY(uiScale))
              == true
            && gameMenu.currentTab != _modOptionsTabPageNumber.Value
            && gameMenu.readyToClose()
          )
          {
            ChangeToOurTab(gameMenu);
            _helper.Input.Suppress(e.Button);
          }
        }
      }
    }
  }

  #endregion

  private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
  {
    KeybindList keybind = ModEntry.ModConfig.OpenModOptionsKeybind;
    if (!keybind.JustPressed())
    {
      return;
    }

    _helper.Input.SuppressActiveKeybinds(keybind);

    if (_hasBgm)
    {
      OnButtonsChanged_Bgm();
    }
    else
    {
      OnButtonsChanged_Vanilla();
    }
  }

  private void OnButtonsChanged_Bgm()
  {
    if (
      !ApiManager.GetApi<IBetterGameMenuApi>(
        ModCompat.BetterGameMenu,
        out IBetterGameMenuApi? bgmApi
      )
    )
    {
      return;
    }

    IClickableMenu? menu = Game1.activeClickableMenu;
    IBetterGameMenu? bgmMenu = menu != null ? bgmApi.AsMenu(menu) : null;

    if (bgmMenu != null)
    {
      // Already in BGM — switch to our tab
      bgmMenu.TryChangeTab(optionsTabName);
    }
    else if (Context.IsPlayerFree)
    {
      // Open BGM to our tab
      Game1.activeClickableMenu = bgmApi.CreateMenu(optionsTabName);
    }
  }

  private void OnButtonsChanged_Vanilla()
  {
    if (Game1.activeClickableMenu is GameMenu gameMenu)
    {
      // Already in GameMenu — switch to our tab
      if (_modOptionsTabPageNumber.Value != null && gameMenu.readyToClose())
      {
        ChangeToOurTab(gameMenu);
      }
    }
    else if (Context.IsPlayerFree)
    {
      // Open GameMenu and defer tab switch to next tick (page not yet added)
      Game1.activeClickableMenu = new GameMenu();
      _switchToOurTabNextTick.Value = true;
    }
  }

  #region Vanilla-only event handlers

  private void OnUpdateTicking(object? sender, EventArgs e)
  {
    // Window resize workaround: re-add our tab before the tick
    if (_addOurTabBeforeTick)
    {
      _addOurTabBeforeTick = false;
      GameRunner.instance.ExecuteForInstances(instance =>
      {
        if (_lastMenu.Value != Game1.activeClickableMenu)
        {
          EarlyOnMenuChanged(_lastMenu.Value, Game1.activeClickableMenu);
          _lastMenu.Value = Game1.activeClickableMenu;
        }
      });
      ModEntry.MonitorObject.Log(
        $"ModOptionsPageHandler: tab re-injected after resize, menu={Game1.activeClickableMenu?.GetType().Name}",
        LogLevel.Trace
      );
    }
  }

  private void OnUpdateTicked(object? sender, EventArgs e)
  {
    var gameMenu = Game1.activeClickableMenu as GameMenu;

    // Map closed → switch back to our tab
    if (_changeToOurTabAfterTick.Value)
    {
      _changeToOurTabAfterTick.Value = false;
      if (gameMenu != null)
      {
        ChangeToOurTab(gameMenu);
        ModEntry.MonitorObject.Log(
          "ModOptionsPageHandler: restored tab after resize",
          LogLevel.Trace
        );
      }
    }

    if (_lastMenu.Value != Game1.activeClickableMenu)
    {
      EarlyOnMenuChanged(_lastMenu.Value, Game1.activeClickableMenu);
      _lastMenu.Value = Game1.activeClickableMenu;
      gameMenu = Game1.activeClickableMenu as GameMenu;
    }

    // Deferred tab switch from keybind open
    if (_switchToOurTabNextTick.Value)
    {
      _switchToOurTabNextTick.Value = false;
      if (gameMenu != null && _modOptionsTabPageNumber.Value != null)
      {
        ChangeToOurTab(gameMenu);
      }
    }

    if (_lastMenuTab.Value != gameMenu?.currentTab)
    {
      OnGameMenuTabChanged(gameMenu);
      _lastMenuTab.Value = gameMenu?.currentTab;
    }
  }

  // Called during UpdateTicked (earlier than Display.MenuChanged)
  private void EarlyOnMenuChanged(IClickableMenu? oldMenu, IClickableMenu? newMenu)
  {
    // Remove from old menu
    if (oldMenu is GameMenu oldGameMenu)
    {
      if (_modOptionsPage.Value != null)
      {
        oldGameMenu.pages.Remove(_modOptionsPage.Value);
        _modOptionsPage.Value.Dispose();
        _modOptionsPage.Value = null;
      }

      if (_modOptionsPageButton.Value != null)
      {
        _modOptionsPageButton.Value = null;
      }

      _modOptionsTabPageNumber.Value = null;
      _modOptionsTab.Value = null;
    }

    // Add to new menu
    if (newMenu is GameMenu newGameMenu)
    {
      // Requires Game1.activeClickableMenu to not be null
      if (_modOptionsPage.Value == null)
      {
        _modOptionsPage.Value = new ModOptionsPage(_optionsElements, _helper.Events);
      }

      if (ShowPersonalConfigButton && _modOptionsPageButton.Value == null)
      {
        _modOptionsPageButton.Value = new ModOptionsPageButton(_helper);
        _modOptionsPageButton.Value.xPositionOnScreen = GetButtonXPosition(newGameMenu);
      }

      List<IClickableMenu> tabPages = newGameMenu.pages;
      _modOptionsTabPageNumber.Value = tabPages.Count;
      tabPages.Add(_modOptionsPage.Value);

      // Restore saved page state (from resize)
      if (_savedPageState.Value != null)
      {
        _modOptionsPage.Value.LoadState(_savedPageState.Value);
        _savedPageState.Value = null;
      }

      if (IsAndroid)
      {
        return;
      }

      // Find the exit tab dynamically (last tab in the list)
      ClickableComponent? exitTab = newGameMenu.tabs.Count > 0 ? newGameMenu.tabs[^1] : null;

      // name = tab id, label = hover text
      _modOptionsTab.Value = new ClickableComponent(
        new Rectangle(
          GetButtonXPosition(newGameMenu),
          newGameMenu.yPositionOnScreen + IClickableMenu.tabYPositionRelativeToMenuY + 64,
          64,
          64
        ),
        optionsTabName,
        "ui2_mod_options"
      )
      {
        myID = ModTabSnapId,

        leftNeighborID = exitTab?.myID ?? -99998,
        tryDefaultIfNoDownNeighborExists = true,
        fullyImmutable = true,
      };

      // Don't add to GameMenu.tabs — GameMenu.draw breaks when our page is current tab
      if (exitTab != null)
      {
        exitTab.rightNeighborID = _modOptionsTab.Value.myID;
        AddOurTabToClickableComponents(newGameMenu, _modOptionsTab.Value);
      }
      else
      {
        ModEntry.MonitorObject.LogOnce(
          "ModOptionsPageHandler: ExitPage tab not found in GameMenu.tabs",
          LogLevel.Error
        );
      }
    }
  }

  private void OnGameMenuTabChanged(GameMenu? gameMenu)
  {
    if (gameMenu != null)
    {
      if (ShowPersonalConfigButton && _modOptionsTab.Value != null)
      {
        // Based on GameMenu.setTabNeighborsForCurrentPage
        if (gameMenu.currentTab == GameMenu.inventoryTab)
        {
          _modOptionsTab.Value.downNeighborID = downNeighborInInventory;

          // Wire inventory slot back up to our tab
          ClickableComponent? slot = gameMenu
            .GetCurrentPage()
            .getComponentWithID(downNeighborInInventory);
          if (slot != null)
          {
            slot.upNeighborID = _modOptionsTab.Value.myID;
          }
        }
        else if (gameMenu.currentTab == GameMenu.exitTab)
        {
          _modOptionsTab.Value.downNeighborID = 535;
        }
        else
        {
          _modOptionsTab.Value.downNeighborID = ClickableComponent.SNAP_TO_DEFAULT;
        }

        AddOurTabToClickableComponents(gameMenu, _modOptionsTab.Value);
      }
    }
  }

  private void OnRenderingMenu(object? sender, RenderingActiveMenuEventArgs e)
  {
    if (!ShowPersonalConfigButton || IsAndroid)
    {
      return;
    }

    if (Game1.activeClickableMenu is GameMenu gameMenu && gameMenu.GetChildMenu() == null)
    {
      // Draw behind the menu so it's visible during transitions (e.g. collections letter view)
      DrawButton(gameMenu);

      // Suppress vanilla tab tooltip so we can draw it after our tab in OnRenderedMenu.
      // Only suppress when OnRenderedMenu will actually run (mirrors its guards), otherwise
      // the tooltip would be cleared and never restored.
      if (
        gameMenu.currentTab != GameMenu.mapTab
        && gameMenu.GetCurrentPage() is not CollectionsPage { letterviewerSubMenu: not null }
      )
      {
        _suppressedTabHoverText.Value = gameMenu.hoverText;
        gameMenu.hoverText = "";
      }
    }
  }

  private void OnRenderedMenu(object? sender, RenderedActiveMenuEventArgs e)
  {
    if (!ShowPersonalConfigButton)
    {
      return;
    }

    if (IsAndroid)
    {
      DrawAndroidButton();
      return;
    }

    if (
      Game1.activeClickableMenu is not GameMenu gameMenu
      || gameMenu.currentTab == GameMenu.mapTab
      || gameMenu.GetChildMenu() != null
      || gameMenu.GetCurrentPage() is CollectionsPage { letterviewerSubMenu: not null }
    )
    {
      return;
    }

    DrawButton(gameMenu);

    // Restore and draw the vanilla tab tooltip on top of our tab.
    if (!string.IsNullOrEmpty(_suppressedTabHoverText.Value))
    {
      gameMenu.hoverText = _suppressedTabHoverText.Value;
      _suppressedTabHoverText.Value = "";
      IClickableMenu.drawHoverText(Game1.spriteBatch, gameMenu.hoverText, Game1.smallFont);
    }

    Tools.DrawMouseCursor();

    if (_modOptionsTab.Value?.containsPoint(Game1.getMouseX(), Game1.getMouseY()) == true)
    {
      IClickableMenu.drawHoverText(Game1.spriteBatch, I18n.OptionsTabTooltip(), Game1.smallFont);
    }
  }

  private void OnWindowClientSizeChanged(object? sender, EventArgs e)
  {
    _windowResizing = true;
    GameRunner.instance.ExecuteForInstances(instance =>
    {
      if (
        Game1.activeClickableMenu is GameMenu gameMenu
        && gameMenu.currentTab == _modOptionsTabPageNumber.GetValueForScreen(instance.instanceId)
      )
      {
        // Swap to game's options tab — GameMenu is recreated before we can re-add our page
        if (gameMenu.GetCurrentPage() is ModOptionsPage modOptionsPage)
        {
          _savedPageState.Value = new ModOptionsPageState();
          modOptionsPage.SaveState(_savedPageState.Value);
        }

        gameMenu.currentTab = GameMenu.optionsTab;
        _instancesWithOptionsPageOpen.Add(instance.instanceId);
      }
    });
    if (_instancesWithOptionsPageOpen.Count > 0)
    {
      ModEntry.MonitorObject.LogOnce(
        $"ModOptionsPageHandler: resize with options page open, instances={_instancesWithOptionsPageOpen.Count}",
        LogLevel.Trace
      );
    }
  }

  // Called between frames (after Display.Rendered, before Update.Ticking)
  private void OnWindowResized(object? sender, EventArgs e)
  {
    if (_windowResizing)
    {
      _windowResizing = false;
      if (_instancesWithOptionsPageOpen.Count > 0)
      {
        GameRunner.instance.ExecuteForInstances(instance =>
        {
          if (_instancesWithOptionsPageOpen.Remove(instance.instanceId))
          {
            if (Game1.activeClickableMenu is GameMenu gameMenu)
            {
              gameMenu.currentTab = (int)
                _modOptionsTabPageNumber.GetValueForScreen(instance.instanceId)!;
            }
          }
        });

        ModEntry.MonitorObject.Log(
          $"ModOptionsPageHandler: resize complete, restored mod tab, instances={_instancesWithOptionsPageOpen.Count}",
          LogLevel.Trace
        );
        _addOurTabBeforeTick = true;
      }
    }
  }

  /// <summary>Based on <see cref="GameMenu.changeTab" /></summary>
  private void ChangeToOurTab(GameMenu gameMenu)
  {
    var modOptionsTabIndex = (int)_modOptionsTabPageNumber.Value!;
    gameMenu.currentTab = modOptionsTabIndex;
    gameMenu.lastOpenedNonMapTab = modOptionsTabIndex;
    gameMenu.initializeUpperRightCloseButton();
    gameMenu.invisible = false;
    Game1.playSound("smallSelect");

    // populateClickableComponentList handles AddTabsToClickableComponents; we just add our tab for snap support
    gameMenu.GetCurrentPage().populateClickableComponentList();
    if (_modOptionsTab.Value != null)
    {
      AddOurTabToClickableComponents(gameMenu, _modOptionsTab.Value);
    }

    gameMenu.setTabNeighborsForCurrentPage();
    if (Game1.options.SnappyMenus)
    {
      gameMenu.snapToDefaultClickableComponent();
    }
  }

  /// <summary>Add our tab to the page's clickable components (initializes list if needed, skips duplicates).</summary>
  private void AddOurTabToClickableComponents(GameMenu gameMenu, ClickableComponent modOptionsTab)
  {
    IClickableMenu currentPage = gameMenu.GetCurrentPage()!;
    if (currentPage.allClickableComponents == null)
    {
      currentPage.populateClickableComponentList();
    }

    if (!currentPage.allClickableComponents!.Contains(modOptionsTab))
    {
      currentPage.allClickableComponents.Add(modOptionsTab);
    }
  }

  private int GetButtonXPosition(GameMenu gameMenu)
  {
    return gameMenu.xPositionOnScreen + gameMenu.width - 165;
  }

  /// <summary>Android: draw the options entry as a slot button next to the inventory organize button.</summary>
  private void DrawAndroidButton()
  {
    _androidButtonBounds.Value = Rectangle.Empty;

    if (
      Game1.activeClickableMenu is not GameMenu gameMenu
      || gameMenu.currentTab != GameMenu.inventoryTab
      || gameMenu.GetChildMenu() != null
      || gameMenu.GetCurrentPage() is not InventoryPage invPage
      || _modOptionsPageButton.Value == null
    )
    {
      return;
    }

    // Anchor to the organize button, whose bounds already account for menu padding (xEdge).
    // These live on InventoryMenu on Android (not on PC's type), so read them by reflection.
    ClickableComponent? anchor =
      invPage.inventory != null ? GetAndroidInventoryButton(invPage.inventory) : null;
    if (anchor == null)
    {
      return;
    }

    const int size = 64;
    const int gap = 8;
    int pageRight = invPage.xPositionOnScreen + invPage.width;
    int x = Math.Min(anchor.bounds.Right + gap, pageRight - 8 - size);
    int y = anchor.bounds.Y;

    _androidButtonBounds.Value = new Rectangle(x, y, size, size);
    _modOptionsPageButton.Value.DrawInSlot(Game1.spriteBatch, x, y);

    if (_androidButtonBounds.Value.Contains(Game1.getMouseX(), Game1.getMouseY()))
    {
      IClickableMenu.drawHoverText(Game1.spriteBatch, I18n.OptionsTabTooltip(), Game1.smallFont);
    }
  }

  private static FieldInfo? _organizeButtonField;
  private static FieldInfo? _trashCanField;
  private static bool _inventoryButtonFieldsResolved;

  /// <summary>Android: reads InventoryMenu.organizeButton (or trashCan) by reflection - absent on PC's type.</summary>
  private static ClickableComponent? GetAndroidInventoryButton(InventoryMenu inventory)
  {
    if (!_inventoryButtonFieldsResolved)
    {
      const BindingFlags flags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
      _organizeButtonField = typeof(InventoryMenu).GetField("organizeButton", flags);
      _trashCanField = typeof(InventoryMenu).GetField("trashCan", flags);
      _inventoryButtonFieldsResolved = true;
    }

    return _organizeButtonField?.GetValue(inventory) as ClickableComponent
      ?? _trashCanField?.GetValue(inventory) as ClickableComponent;
  }

  private void HandleAndroidButtonPressed(ButtonPressedEventArgs e)
  {
    if (e.Button is not (SButton.MouseLeft or SButton.ControllerA) || e.IsSuppressed())
    {
      return;
    }

    if (
      Game1.activeClickableMenu is not GameMenu gameMenu
      || gameMenu.currentTab == _modOptionsTabPageNumber.Value
      || !gameMenu.readyToClose()
    )
    {
      return;
    }

    int mouseX = (int)Utility.ModifyCoordinateForUIScale(Game1.getMouseX());
    int mouseY = (int)Utility.ModifyCoordinateForUIScale(Game1.getMouseY());
    if (_androidButtonBounds.Value.Contains(mouseX, mouseY))
    {
      ChangeToOurTab(gameMenu);
      _helper.Input.Suppress(e.Button);
    }
  }

  private void DrawButton(GameMenu gameMenu)
  {
    ModOptionsPageButton? button = _modOptionsPageButton.Value;

    if (button == null || _modOptionsTabPageNumber.Value == null)
    {
      return;
    }

    button.yPositionOnScreen =
      gameMenu.yPositionOnScreen
      + (gameMenu.currentTab == _modOptionsTabPageNumber.Value ? 24 : 16);

    button.draw(Game1.spriteBatch);
  }

  #endregion

  /// <summary>Returns version string from SMAPI manifest, assembly, or "(unknown version)".</summary>
  private static string GetVersionString(IModHelper helper)
  {
    IModInfo? modInfo = helper.ModRegistry.Get(helper.ModRegistry.ModID);
    if (modInfo != null)
    {
      return $"v{modInfo.Manifest.Version}";
    }

    ModEntry.MonitorObject.LogOnce(
      "ModOptionsPageHandler: could not retrieve mod information",
      LogLevel.Info
    );

    Version? assemblyVersion = Assembly.GetAssembly(typeof(ModEntry))?.GetName().Version;
    if (assemblyVersion != null)
    {
      return $"v={assemblyVersion}";
    }

    ModEntry.MonitorObject.LogOnce(
      "ModOptionsPageHandler: could not retrieve assembly version",
      LogLevel.Info
    );

    return "(unknown version)";
  }
}

/// <summary>Saved/restored state across game menu resizes.</summary>
internal class ModOptionsPageState
{
  public int? currentComponent;
  public int? currentIndex;
}
