using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Compatibility.Helpers;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Infrastructure.Helpers;
using UIInfoSuite2Alt.Options;
using UIInfoSuite2Alt.Patches;
using UIInfoSuite2Alt.UIElements;
using UIInfoSuite2Alt.UIElements.Menus;

namespace UIInfoSuite2Alt;

public partial class ModEntry : Mod
{
  public static ModConfig ModConfig { get; set; } = null!;

  private static EventHandler<ButtonsChangedEventArgs>? _calendarAndQuestKeyBindingsHandler;
  private static EventHandler<ButtonsChangedEventArgs>? _monsterEradicationKeyBindingsHandler;
  private static EventHandler<ButtonsChangedEventArgs>? _hideTreesKeyBindingsHandler;

  private static IModHelper _modHelper = null!;
  private ModOptionsPageHandler? _modOptionsPageHandler;
  private static Dictionary<string, string>? _lastConfigSnapshot;

  public static IReflectionHelper Reflection { get; private set; } = null!;

  internal const string CustomIconsAssetName = "Mods/DazUki.UIInfoSuite2Alt/CustomIcons";

  private const string LauncherDrawerDictAssetName = "aedenthorn.LauncherDrawer/dict";

  public static IMonitor MonitorObject { get; private set; } = null!;

  /// <summary>Save the global config.json to disk.</summary>
  public static void SaveConfig()
  {
    _modHelper.WriteConfig(ModConfig);

    var newSnapshot = ModConfig.SnapshotToggles();
    if (_lastConfigSnapshot != null)
    {
      List<string> changes = ModConfig.DiffToggles(_lastConfigSnapshot, newSnapshot);
      if (changes.Count > 0)
      {
        string diff = string.Join(", ", changes);
        MonitorObject.Log($"ModEntry: config saved, {diff}", LogLevel.Trace);
      }
    }

    _lastConfigSnapshot = newSnapshot;
  }

  /// <summary>
  /// Harmony prefix+postfix pair for SetWindowSize: prevents crash when resizing while on our custom tab.
  /// Prefix: temporarily switches away from our tab so the GameMenu recreation uses a valid tab index.
  /// Postfix: if SetWindowSize returned early (no actual resize), restores our tab.
  /// </summary>
  private static void SetWindowSize_Prefix(out int __state)
  {
    __state = -1;
    if (
      Game1.activeClickableMenu is GameMenu gameMenu
      && gameMenu.currentTab >= 0
      && gameMenu.currentTab < gameMenu.pages.Count
      && gameMenu.pages[gameMenu.currentTab] is ModOptionsPage
    )
    {
      __state = gameMenu.currentTab;
      gameMenu.currentTab = GameMenu.optionsTab;
    }
  }

  private static void SetWindowSize_Postfix(int __state)
  {
    // If we switched away but the menu wasn't recreated (no-op resize), restore our tab
    if (
      __state >= 0
      && Game1.activeClickableMenu is GameMenu gameMenu
      && __state < gameMenu.pages.Count
      && gameMenu.pages[__state] is ModOptionsPage
    )
    {
      gameMenu.currentTab = __state;
    }
  }

  #region Entry
  public override void Entry(IModHelper helper)
  {
    I18n.Init(helper.Translation);
    Reflection = helper.Reflection;
    MonitorObject = Monitor;
    _modHelper = helper;

    Monitor.Log($"ModEntry: loaded v{ModManifest.Version}", LogLevel.Info);

    var harmony = new Harmony(ModManifest.UniqueID);
    TvChannelWatcher.Initialize(harmony, helper);
    ShowFishOnCatch.Initialize(harmony);
    HudMessagePatch.Initialize(harmony, helper.ModRegistry.IsLoaded(ModCompat.SpaceCore));
    ShowAccurateHearts.Initialize(harmony);
    ShowItemQualityPatch.Initialize(
      harmony,
      helper.ModRegistry.IsLoaded(ModCompat.ShowItemQuality)
    );
    HideTreesPatch.Initialize(harmony, helper);
    SuppressProbeNoisePatch.Initialize(harmony);
    ShowCalendarAndBillboardOnInventoryPatch.Initialize(harmony);
    BuffIconSizePatch.Initialize(harmony);
    if (AndroidHud.IsAndroid)
    {
      OptionsButtonIconPatch.Icon = AssetHelper.TryLoadTexture(helper, "assets/tab_icon.png");
      OptionsButtonIconPatch.Initialize(harmony);
    }
    harmony.Patch(
      AccessTools.Method(typeof(Game1), nameof(Game1.SetWindowSize)),
      prefix: new HarmonyMethod(typeof(ModEntry), nameof(SetWindowSize_Prefix)),
      postfix: new HarmonyMethod(typeof(ModEntry), nameof(SetWindowSize_Postfix))
    );
    Monitor.Log(
      "ModEntry: Harmony patches applied - TvChannelWatcher, ShowFishOnCatch, HudMessagePatch, ShowAccurateHearts, ShowItemQualityPatch, HideTreesPatch, SuppressProbeNoisePatch, ShowCalendarAndBillboardOnInventoryPatch, BuffIconSizePatch, SetWindowSize",
      LogLevel.Trace
    );

    ModConfig = Helper.ReadConfig<ModConfig>();

    DebugCommandHandler.Register(helper, Monitor, ModManifest);

    helper.Events.Content.AssetRequested += OnAssetRequested;
    helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
    helper.Events.GameLoop.DayStarted += OnDayStarted;
    helper.Events.GameLoop.GameLaunched += OnGameLaunched;
    helper.Events.Display.RenderedHud += OnRenderedHud;

    if (AndroidHud.IsAndroid)
    {
      helper.Events.GameLoop.UpdateTicking += (_, _) => AndroidHud.ClaimTouchOverHud();
    }

    RegisterCalendarAndQuestKeyBindings(helper, true);
    RegisterMonsterEradicationKeyBindings(helper, true);
    RegisterHideTreesKeyBinding(helper, true);

    IconHandler.Handler.IsQuestLogPermanent = helper.ModRegistry.IsLoaded(ModCompat.DeluxeJournal);

    if (helper.ModRegistry.IsLoaded(ModCompat.DailyTasksReportPlus))
    {
      IconHandler.Handler.ExtraYOffset = 18;
    }

    if (helper.ModRegistry.IsLoaded(ModCompat.BetterRanching))
    {
      Monitor.Log(
        "ModEntry: Better Ranching detected, disabling overlapping animal indicators",
        LogLevel.Info
      );
    }

    if (helper.ModRegistry.IsLoaded(ModCompat.BetterJunimos))
    {
      Monitor.Log(
        "ModEntry: Better Junimos detected, using configured Junimo hut radius",
        LogLevel.Info
      );
    }

    CheckForConflictingMods(helper);
  }
  #endregion

  #region Conflict detection
  private void CheckForConflictingMods(IModHelper helper)
  {
    var conflicts = new (string ModId, string Name)[]
    {
      (ModCompat.UIInfoSuite2, "UI Info Suite 2"),
      (ModCompat.UIInfoSuite, "UI Info Suite"),
    };

    foreach (var (modId, name) in conflicts)
    {
      if (helper.ModRegistry.IsLoaded(modId))
      {
        Monitor.Log(
          $"ModEntry: conflict detected - '{name}' ({modId}), both mods provide the same features, please remove one",
          LogLevel.Warn
        );
      }
    }
  }
  #endregion

  // GMCM registration is in Options/GmcmRegistration.cs (partial class)

  #region Mod recommendations
  private static void LogModRecommendations(IModHelper helper)
  {
    var recommendations = new (string ModId, string Name, int NexusId, string Reason)[]
    {
      (ModCompat.Gmcm, "Generic Mod Config Menu", 5098, "Required to Change Keybinds in-game"),
      (
        ModCompat.NpcMapLocations,
        "NPC Map Locations",
        239,
        "UIIS2Alt npc map tracking was Removed in v2.7.0"
      ),
    };

    foreach (var (modId, name, nexusId, reason) in recommendations)
    {
      if (!helper.ModRegistry.IsLoaded(modId))
      {
        MonitorObject.Log(
          $"ModEntry: recommended mod not installed - {name} [Nexus:{nexusId}] - {reason}",
          LogLevel.Warn
        );
      }
    }
  }
  #endregion

  #region Event subscriptions
  private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
  {
    // Only main player screen
    if (Context.ScreenId != 0)
    {
      return;
    }

    HideTreesPatch.Reset();
    _modOptionsPageHandler?.Dispose();
  }

  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    if (Context.ScreenId != 0)
    {
      return;
    }

    // Re-read config (may have been edited externally)
    ModConfig = Helper.ReadConfig<ModConfig>();
    BundleHelper.ClearCaches();
    UnlockableBundleHelper.ClearCache();
    ApplyFeatures();
  }

  /// <summary>Recreate feature handler to re-apply all toggles from current config.</summary>
  private void ApplyFeatures()
  {
    if (!Context.IsWorldReady)
    {
      return;
    }

    var currentSnapshot = ModConfig.SnapshotToggles();

    if (_lastConfigSnapshot == null)
    {
      string all = string.Join(", ", currentSnapshot.Select(kv => $"{kv.Key}={kv.Value}"));
      MonitorObject.Log($"ModEntry: initial config, {all}", LogLevel.Trace);
    }
    else
    {
      List<string> changes = ModConfig.DiffToggles(_lastConfigSnapshot, currentSnapshot);
      if (changes.Count == 1)
      {
        MonitorObject.Log($"ModEntry: GMCM config changed, {changes[0]}", LogLevel.Trace);
      }
      else if (changes.Count > 1)
      {
        string diff = string.Join("\n - ", changes);
        MonitorObject.Log($"ModEntry: GMCM config changed\n - {diff}", LogLevel.Trace);
      }
    }

    _lastConfigSnapshot = currentSnapshot;

    IconHandler.Handler.IconOrder = ModConfig.IconOrder;
    IconHandler.Handler.UseVerticalLayout = ModConfig.UseVerticalIconLayout;
    IconHandler.Handler.IconsPerRow = ModConfig.IconsPerRow;
    IconHandler.Handler.ShowQuestCount = ModConfig.ShowQuestCount;
    _modOptionsPageHandler?.Dispose();
    _modOptionsPageHandler = new ModOptionsPageHandler(Helper, ModConfig, SaveConfig);
  }

  private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
  {
    if (e.NameWithoutLocale.IsEquivalentTo(CustomIconsAssetName))
    {
      e.LoadFrom(() => new Dictionary<string, CustomIconData>(), AssetLoadPriority.Low);
    }

    // Add our settings as a Launcher Drawer entry. Gated on GMCM since the action opens our GMCM page.
    if (
      e.NameWithoutLocale.IsEquivalentTo(LauncherDrawerDictAssetName)
      && Helper.ModRegistry.IsLoaded(ModCompat.LauncherDrawer)
      && Helper.ModRegistry.IsLoaded(ModCompat.Gmcm)
    )
    {
      e.Edit(data =>
      {
        var dict = data.AsDictionary<string, Dictionary<string, object>>().Data;
        dict[ModManifest.UniqueID] = new Dictionary<string, object>
        {
          { "Name", I18n.LauncherDrawer_EntryName() },
          { "Description", I18n.LauncherDrawer_EntryDescription() },
          {
            "Action",
            new Action(() =>
              Helper
                .ModRegistry.GetApi<IGenericModConfigMenuApi>(ModCompat.Gmcm)
                ?.OpenModMenuAsChildMenu(ModManifest)
            )
          },
        };
      });
    }
  }

  private static void OnRenderedHud(object? sender, RenderedHudEventArgs e)
  {
    // Slide icons left of the Launcher Drawer column while it's open (no-op when LD is absent).
    // Vertical layout sits further right, so it needs extra clearance.
    int clearance =
      LauncherDrawerHelper.DrawerClearance + (IconHandler.Handler.UseVerticalLayout ? 50 : 0);
    IconHandler.Handler.ExtraXOffset = LauncherDrawerHelper.ShouldShiftIcons ? -clearance : 0;

    IconHandler.Handler.DrawQueuedIcons(e.SpriteBatch);

    if (
      ModConfig.ShowHideTreesBanner
      && UIElementUtils.IsRenderingNormally()
      && Game1.activeClickableMenu == null
    )
    {
      HideTreesPatch.DrawHiddenBanner(e.SpriteBatch, ModConfig.HideTreesKeybind.ToString());
    }
  }

  public static void RegisterCalendarAndQuestKeyBindings(IModHelper helper, bool subscribe)
  {
    SetButtonsChangedSubscription(
      helper,
      subscribe,
      ref _calendarAndQuestKeyBindingsHandler,
      () => HandleCalendarAndQuestKeyBindings(helper)
    );
  }

  private static void HandleCalendarAndQuestKeyBindings(IModHelper helper)
  {
    if (Context.IsPlayerFree)
    {
      if (ModConfig.OpenCalendarKeybind.JustPressed())
      {
        helper.Input.SuppressActiveKeybinds(ModConfig.OpenCalendarKeybind);
        Game1.activeClickableMenu = new Billboard();
      }
      else if (ModConfig.OpenQuestBoardKeybind.JustPressed())
      {
        helper.Input.SuppressActiveKeybinds(ModConfig.OpenQuestBoardKeybind);
        ShowCalendarAndBillboardOnGameMenuButton.OpenQuestBoardFromKeybind();
      }
      else if (ModConfig.OpenSpecialOrdersBoardKeybind.JustPressed())
      {
        helper.Input.SuppressActiveKeybinds(ModConfig.OpenSpecialOrdersBoardKeybind);
        ShowCalendarAndBillboardOnGameMenuButton.OpenSpecialOrdersBoardFromKeybind();
      }
      else if (ModConfig.OpenQiSpecialOrdersBoardKeybind.JustPressed())
      {
        helper.Input.SuppressActiveKeybinds(ModConfig.OpenQiSpecialOrdersBoardKeybind);
        ShowCalendarAndBillboardOnGameMenuButton.OpenQiSpecialOrdersBoardFromKeybind();
      }
    }
    else if (Game1.activeClickableMenu != null)
    {
      if (ModConfig.OpenCalendarKeybind.JustPressed() && Game1.activeClickableMenu is Billboard)
      {
        helper.Input.SuppressActiveKeybinds(ModConfig.OpenCalendarKeybind);
        Game1.playSound("bigDeSelect");
        Game1.exitActiveMenu();
      }
      else if (
        ModConfig.OpenQuestBoardKeybind.JustPressed()
        && Game1.activeClickableMenu is Billboard or QuestBoardSelector
      )
      {
        helper.Input.SuppressActiveKeybinds(ModConfig.OpenQuestBoardKeybind);
        Game1.playSound("bigDeSelect");
        Game1.exitActiveMenu();
      }
      else if (
        ModConfig.OpenSpecialOrdersBoardKeybind.JustPressed()
        && Game1.activeClickableMenu is SpecialOrdersBoard or SpecialOrdersBoardSelector
      )
      {
        helper.Input.SuppressActiveKeybinds(ModConfig.OpenSpecialOrdersBoardKeybind);
        Game1.playSound("bigDeSelect");
        Game1.exitActiveMenu();
      }
      else if (
        ModConfig.OpenQiSpecialOrdersBoardKeybind.JustPressed()
        && Game1.activeClickableMenu is SpecialOrdersBoard
      )
      {
        helper.Input.SuppressActiveKeybinds(ModConfig.OpenQiSpecialOrdersBoardKeybind);
        Game1.playSound("bigDeSelect");
        Game1.exitActiveMenu();
      }
    }
  }

  public static void RegisterMonsterEradicationKeyBindings(IModHelper helper, bool subscribe)
  {
    SetButtonsChangedSubscription(
      helper,
      subscribe,
      ref _monsterEradicationKeyBindingsHandler,
      () => HandleMonsterEradicationKeyBindings(helper)
    );
  }

  private static void HandleMonsterEradicationKeyBindings(IModHelper helper)
  {
    if (Context.IsPlayerFree && ModConfig.OpenMonsterEradicationKeybind.JustPressed())
    {
      helper.Input.SuppressActiveKeybinds(ModConfig.OpenMonsterEradicationKeybind);
      MonsterQuestHelper.ShowMonsterKillList();
    }
  }

  public static void RegisterHideTreesKeyBinding(IModHelper helper, bool subscribe)
  {
    SetButtonsChangedSubscription(
      helper,
      subscribe,
      ref _hideTreesKeyBindingsHandler,
      () => HandleHideTreesKeyBinding(helper)
    );
  }

  private static void HandleHideTreesKeyBinding(IModHelper helper)
  {
    if (Context.IsPlayerFree && ModConfig.HideTreesKeybind.JustPressed())
    {
      helper.Input.SuppressActiveKeybinds(ModConfig.HideTreesKeybind);
      HideTreesPatch.Toggle();
    }
  }

  private static void SetButtonsChangedSubscription(
    IModHelper helper,
    bool subscribe,
    ref EventHandler<ButtonsChangedEventArgs>? handler,
    Action onPressed
  )
  {
    handler ??= (_, _) => onPressed();

    helper.Events.Input.ButtonsChanged -= handler;

    if (subscribe)
    {
      helper.Events.Input.ButtonsChanged += handler;
    }
  }
  #endregion
}
