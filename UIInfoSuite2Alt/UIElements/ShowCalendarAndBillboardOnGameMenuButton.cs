using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.SpecialOrders;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Compatibility.Helpers;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.UIElements.Menus;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowCalendarAndBillboardOnGameMenuButton : IDisposable
{
  #region Properties
  private const int IconSpacing = 8;
  private const int DrawSize = 32;
  private const int DesktopSlotSize = 64;

  private static FieldInfo? _bottomBoxYField;
  private static bool _bottomBoxYResolved;

  // Snap component IDs for gamepad navigation
  private const int CalendarSnapId = 77770;
  private const int QuestSnapId = 77771;
  private const int QiOrdersSnapId = 77772;
  private const int SpecialOrdersSnapId = 77773;

  private readonly PerScreen<Rectangle> _calendarBounds = new(() => Rectangle.Empty);
  private readonly PerScreen<Rectangle> _questBounds = new(() => Rectangle.Empty);
  private readonly PerScreen<Rectangle> _specialOrdersBounds = new(() => Rectangle.Empty);
  private readonly PerScreen<Rectangle> _qiOrdersBounds = new(() => Rectangle.Empty);

  private readonly PerScreen<ClickableComponent> _calendarSnap = new(() =>
    new ClickableComponent(Rectangle.Empty, "calendar") { myID = CalendarSnapId }
  );
  private readonly PerScreen<ClickableComponent> _questSnap = new(() =>
    new ClickableComponent(Rectangle.Empty, "quest") { myID = QuestSnapId }
  );
  private readonly PerScreen<ClickableComponent> _specialOrdersSnap = new(() =>
    new ClickableComponent(Rectangle.Empty, "specialOrders") { myID = SpecialOrdersSnapId }
  );
  private readonly PerScreen<ClickableComponent> _qiOrdersSnap = new(() =>
    new ClickableComponent(Rectangle.Empty, "qiOrders") { myID = QiOrdersSnapId }
  );

  private readonly IModHelper _helper;
  private Texture2D? _townTexture;
  private readonly bool _hasRidgesideVillage;
  private readonly bool _hasSunberryVillage;
  private readonly bool _hasEscasModdingPlugins;
  private readonly bool _hasSwordAndSorcery;
  private readonly bool _hasBiggerBackpack;
  private readonly bool _hasFullInventoryView;
  private readonly bool _hasCpCatValley;

  private readonly PerScreen<int> _soPulseTimer = new();
  private readonly PerScreen<int> _soPulseDelay = new();

  private const string BoardSigPrefix = "UIInfoSuite2Alt.BoardSig.";
  private List<(string BoardType, string DisplayName)>? _cachedModBoards;
  private int _cachedModBoardsDay = -1;

  // RSV quest board reflection cache
  private bool _rsvQuestReflectionInit;
  private FieldInfo? _rsvDailyQuestDataField;
  private ConstructorInfo? _rsvQuestBoardCtor;
  private FieldInfo? _rsvAcceptedDailyQuestField;
  private FieldInfo? _rsvDailyTownQuestField;
  private List<(string BoardType, string DisplayName)>? _cachedModQuestBoards;
  private int _cachedModQuestBoardsDay = -1;

  private static ShowCalendarAndBillboardOnGameMenuButton? _instance;
  private static bool _enabled;
  #endregion

  #region Lifecycle
  public ShowCalendarAndBillboardOnGameMenuButton(IModHelper helper)
  {
    _instance = this;
    _helper = helper;
    _hasRidgesideVillage = helper.ModRegistry.IsLoaded(ModCompat.RidgesideVillage);
    _hasSunberryVillage = helper.ModRegistry.IsLoaded(ModCompat.SunberryVillage);
    _hasEscasModdingPlugins = helper.ModRegistry.IsLoaded(ModCompat.EscasModdingPlugins);
    _hasSwordAndSorcery = helper.ModRegistry.IsLoaded(ModCompat.SwordAndSorcery);
    _hasBiggerBackpack = helper.ModRegistry.IsLoaded(ModCompat.BiggerBackpack);
    _hasFullInventoryView = helper.ModRegistry.IsLoaded(ModCompat.FullInventoryView);
    _hasCpCatValley = helper.ModRegistry.IsLoaded(ModCompat.CatValley);
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool showCalendarAndBillboard)
  {
    _enabled = showCalendarAndBillboard;
    _helper.Events.Display.RenderedActiveMenu -= OnRenderedActiveMenu;
    StopWatchingMenuClose();
    _helper.Events.Input.ButtonPressed -= OnButtonPressed;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;

    if (showCalendarAndBillboard)
    {
      _helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
      _helper.Events.Input.ButtonPressed += OnButtonPressed;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    }
  }
  #endregion


  #region Event subscriptions
  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    // Pulse timer for SO exclamation
    int elapsed = (int)Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
    if (_soPulseTimer.Value > 0)
    {
      _soPulseTimer.Value -= elapsed;
    }
    else if (_soPulseDelay.Value > 0)
    {
      _soPulseDelay.Value -= elapsed;
    }
    else
    {
      _soPulseTimer.Value = 1000;
      _soPulseDelay.Value = 3000;
    }
  }

  private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
  {
    if (e.Button is SButton.MouseLeft or SButton.ControllerA)
    {
      if (ActivateBillboard())
      {
        _helper.Input.Suppress(e.Button);
      }
    }
  }

  private void OnRenderedActiveMenu(object? sender, EventArgs e)
  {
    IClickableMenu? activeMenu = Game1.activeClickableMenu;

    if (
      GameMenuHelper.IsTab(activeMenu, GameMenu.inventoryTab)
      && GameMenuHelper.GetChildMenu(activeMenu) == null
    )
    {
      DrawBillboard();
    }
  }
  #endregion


  #region Logic
  /// <summary>Called by the InventoryPage.draw transpiler; draws icon sprites and caches bounds for hover/snap passes.</summary>
  public static void DrawIconsFromInventoryPagePatch(SpriteBatch b)
  {
    if (!_enabled || _instance == null)
    {
      return;
    }

    IClickableMenu? menu = Game1.activeClickableMenu;
    if (
      menu == null
      || !GameMenuHelper.IsTab(menu, GameMenu.inventoryTab)
      || GameMenuHelper.GetChildMenu(menu) != null
    )
    {
      return;
    }

    _instance.ComputeBoundsAndDrawIcons(b, menu);
  }

  private void ComputeBoundsAndDrawIcons(SpriteBatch b, IClickableMenu menu)
  {
    float scale = GetMenuScale(menu);
    int S(int value) => (int)(value * scale);

    int drawSize = S(DrawSize);
    int iconSpacing = S(IconSpacing);

    // Mod compatibility offsets
    int offset = 294;

    if (_hasBiggerBackpack)
      offset -= 64;

    if (_hasFullInventoryView)
      offset -= 64;

    if (_hasCpCatValley)
      offset -= 8;

    int menuRight = menu.xPositionOnScreen + menu.width;

    // Android "Menu padding" (Game1.xEdge) insets the InventoryPage but not the GameMenu; anchor
    // to the page's right edge to track it. xEdge defaults to 0, so no-op until the player raises it.
    if (
      Constants.TargetPlatform == GamePlatform.Android
      && GameMenuHelper.GetCurrentPage(menu) is InventoryPage invPage
    )
    {
      menuRight = invPage.xPositionOnScreen + invPage.width;
    }

    int baseX = menuRight - S(120);
    int baseY = menu.yPositionOnScreen + menu.height - S(offset);

    if (TryGetAndroidDividerY(menu, out int dividerY))
    {
      baseY = dividerY + S(28) + iconSpacing;
    }

    ParsedItemData calendarData = ItemRegistry.GetDataOrErrorItem("(F)1402");
    Rectangle calendarSrc = calendarData.GetSourceRect();
    Rectangle calendarDest = new(
      baseX,
      baseY - S(28),
      S(calendarSrc.Width * 2),
      S(calendarSrc.Height * 2)
    );
    Rectangle questDest = new(baseX + drawSize + iconSpacing, baseY - S(6), drawSize, drawSize);

    _calendarBounds.Value = new Rectangle(baseX, baseY - S(6), drawSize, drawSize);
    _questBounds.Value = questDest;

    bool soUnlocked = SpecialOrder.IsSpecialOrdersBoardUnlocked();
    Rectangle specialOrdersDest = Rectangle.Empty;
    if (soUnlocked)
    {
      int soWidth = drawSize * 17 / 13;
      specialOrdersDest = new Rectangle(
        questDest.X - S(4),
        questDest.Y + drawSize + iconSpacing,
        soWidth,
        drawSize
      );
    }
    _specialOrdersBounds.Value = specialOrdersDest;

    bool qiUnlocked = IslandWest.IsQiWalnutRoomDoorUnlocked(out _);
    Rectangle qiOrdersDest = Rectangle.Empty;
    if (qiUnlocked)
    {
      int qiWidth = S(15 * 2);
      int qiHeight = S(14 * 2);
      qiOrdersDest = new Rectangle(
        specialOrdersDest != Rectangle.Empty
          ? specialOrdersDest.X - qiWidth - iconSpacing + S(4)
          : questDest.X - S(4),
        (
          specialOrdersDest != Rectangle.Empty
            ? specialOrdersDest.Y
            : questDest.Y + drawSize + iconSpacing
        ) + S(2),
        qiWidth,
        qiHeight
      );
    }
    _qiOrdersBounds.Value = qiOrdersDest;

    DrawIcons(
      b,
      calendarData,
      calendarDest,
      calendarSrc,
      questDest,
      soUnlocked,
      specialOrdersDest,
      qiUnlocked,
      qiOrdersDest
    );
  }

  /// <summary>
  /// Android sizes inventory slots to the viewport rather than the fixed 64 desktop uses, so desktop-tuned
  /// pixel sizes come out small next to them. Mirrors the game's own InventoryMenu.scaleFactor. Always 1 on desktop.
  /// </summary>
  private static float GetMenuScale(IClickableMenu menu)
  {
    if (Constants.TargetPlatform != GamePlatform.Android)
    {
      return 1f;
    }

    if (
      GameMenuHelper.GetCurrentPage(menu) is not InventoryPage page
      || page.inventory?.inventory is not { Count: > 0 } slots
    )
    {
      return 1f;
    }

    int slotSize = slots[0].bounds.Width;
    return slotSize > 0 ? (float)slotSize / DesktopSlotSize : 1f;
  }

  private static bool TryGetAndroidDividerY(IClickableMenu menu, out int dividerY)
  {
    dividerY = 0;
    if (
      Constants.TargetPlatform != GamePlatform.Android
      || GameMenuHelper.GetCurrentPage(menu) is not InventoryPage page
    )
    {
      return false;
    }

    if (!_bottomBoxYResolved)
    {
      _bottomBoxYField = page.GetType()
        .GetField("bottomBoxY", BindingFlags.NonPublic | BindingFlags.Instance);
      _bottomBoxYResolved = true;
    }

    if (_bottomBoxYField?.GetValue(page) is int value && value > 0)
    {
      dividerY = value;
      return true;
    }

    dividerY = page.yPositionOnScreen + page.height / 2;
    return dividerY > 0;
  }

  private void DrawBillboard()
  {
    // Icon sprites are drawn earlier via the InventoryPage patch; this pass only does hover text + gamepad snap.
    IClickableMenu? menu = Game1.activeClickableMenu;
    if (menu == null)
    {
      return;
    }

    SpriteBatch b = Game1.spriteBatch;
    int mouseX = Game1.getMouseX();
    int mouseY = Game1.getMouseY();

    if (_calendarBounds.Value.Contains(mouseX, mouseY))
    {
      IClickableMenu.drawHoverText(b, I18n.Calendar(), Game1.dialogueFont);
    }
    else if (_questBounds.Value.Contains(mouseX, mouseY))
    {
      IClickableMenu.drawHoverText(b, I18n.Billboard(), Game1.dialogueFont);
    }
    else if (_specialOrdersBounds.Value.Contains(mouseX, mouseY))
    {
      IClickableMenu.drawHoverText(b, I18n.SpecialOrders(), Game1.dialogueFont);
    }
    else if (_qiOrdersBounds.Value.Contains(mouseX, mouseY))
    {
      IClickableMenu.drawHoverText(b, I18n.QiSpecialOrders(), Game1.dialogueFont);
    }

    InjectSnapComponents(menu);
  }

  private void DrawIcons(
    SpriteBatch b,
    ParsedItemData calendarData,
    Rectangle calendarDest,
    Rectangle calendarSrc,
    Rectangle questDest,
    bool soUnlocked,
    Rectangle specialOrdersDest,
    bool qiUnlocked,
    Rectangle qiOrdersDest
  )
  {
    b.Draw(calendarData.GetTexture(), calendarDest, calendarSrc, Color.White);
    b.Draw(Game1.objectSpriteSheet, questDest, new Rectangle(144, 592, 16, 16), Color.White);

    // Exclamation mark for available daily quests
    if (
      Game1.CanAcceptDailyQuest()
      || GetAvailableModQuestBoards().Any(mb => HasRsvUnacceptedQuest(mb.BoardType))
    )
    {
      float scale = 1.6f;
      b.Draw(
        Game1.mouseCursors,
        new Vector2(questDest.X + questDest.Width - 3f, questDest.Y - 5f),
        new Rectangle(403, 496, 5, 14),
        Color.White,
        0f,
        Vector2.Zero,
        scale,
        SpriteEffects.None,
        1f
      );
    }

    if (soUnlocked)
    {
      _townTexture ??= _helper.GameContent.Load<Texture2D>("Maps/spring_town");
      b.Draw(_townTexture, specialOrdersDest, new Rectangle(480, 1001, 17, 13), Color.White);

      if (
        HasUnviewedOrders("") || GetAvailableModBoards().Any(mb => HasUnviewedOrders(mb.BoardType))
      )
      {
        DrawPulsingExclamation(
          b,
          new Vector2(specialOrdersDest.X + specialOrdersDest.Width - 4f, specialOrdersDest.Y + 5f)
        );
      }
    }

    if (qiUnlocked)
    {
      b.Draw(Game1.objectSpriteSheet, qiOrdersDest, new Rectangle(288, 561, 15, 14), Color.White);

      if (HasUnviewedOrders("Qi"))
      {
        DrawPulsingExclamation(
          b,
          new Vector2(qiOrdersDest.X + qiOrdersDest.Width - 4f, qiOrdersDest.Y + 3f)
        );
      }
    }
  }

  private void DrawPulsingExclamation(SpriteBatch b, Vector2 position)
  {
    float baseScale = 1.6f;
    float scale = baseScale;
    Vector2 shake = Vector2.Zero;

    if (_soPulseTimer.Value > 0)
    {
      float pulseScale = 1f / (Math.Max(300f, Math.Abs(_soPulseTimer.Value % 1000 - 500)) / 500f);
      scale = baseScale * pulseScale;
      if (pulseScale > 1f)
      {
        shake = new Vector2(Game1.random.Next(-1, 2), Game1.random.Next(-1, 2));
      }
    }

    b.Draw(
      Game1.mouseCursors,
      position + shake,
      new Rectangle(403, 496, 5, 14),
      Color.White,
      0f,
      new Vector2(2.5f, 7f),
      scale,
      SpriteEffects.None,
      1f
    );
  }

  private void InjectSnapComponents(IClickableMenu menu)
  {
    IClickableMenu? page = GameMenuHelper.GetCurrentPage(menu);
    if (page == null)
      return;

    if (page.allClickableComponents == null)
      page.populateClickableComponentList();
    if (page.allClickableComponents == null)
      return;

    page.allClickableComponents.RemoveAll(c =>
      c.myID is CalendarSnapId or QuestSnapId or SpecialOrdersSnapId or QiOrdersSnapId
    );

    _calendarSnap.Value.bounds = _calendarBounds.Value;
    _questSnap.Value.bounds = _questBounds.Value;
    page.allClickableComponents.Add(_calendarSnap.Value);
    page.allClickableComponents.Add(_questSnap.Value);

    bool hasSO = _specialOrdersBounds.Value != Rectangle.Empty;
    bool hasQi = _qiOrdersBounds.Value != Rectangle.Empty;

    if (hasSO)
    {
      _specialOrdersSnap.Value.bounds = _specialOrdersBounds.Value;
      page.allClickableComponents.Add(_specialOrdersSnap.Value);
    }

    if (hasQi)
    {
      _qiOrdersSnap.Value.bounds = _qiOrdersBounds.Value;
      page.allClickableComponents.Add(_qiOrdersSnap.Value);
    }

    // Use inventory.capacity rather than MaxItems: Bigger Backpack can persist an inflated
    // MaxItems in the save even after the mod is removed, leaving slots that don't exist.
    int lastSlotId = (page as InventoryPage)?.inventory?.capacity - 1 ?? Game1.player.MaxItems - 1;
    (int SlotOffset, int SnapId)[] slotWiring =
    [
      (0, QuestSnapId),
      (-1, QuestSnapId),
      (-2, CalendarSnapId),
      (-3, CalendarSnapId),
    ];
    foreach ((int offset, int snapId) in slotWiring)
    {
      ClickableComponent? slot = page.getComponentWithID(lastSlotId + offset);
      if (slot != null)
      {
        slot.downNeighborID = snapId;
      }
    }
    int thirdLastSlotId = lastSlotId - 2;

    ClickableComponent? trinketSlot = page.getComponentWithID(InventoryPage.region_trinkets);
    if (trinketSlot != null)
    {
      trinketSlot.rightNeighborID = CalendarSnapId;
    }
    else
    {
      // Without a trinket slot, vanilla routes Hat/Shirt/Pants right to TrashCan; redirect to Calendar so our icons remain reachable.
      int[] rightColumnSlots =
      [
        InventoryPage.region_hat,
        InventoryPage.region_shirt,
        InventoryPage.region_pants,
      ];
      foreach (int slotId in rightColumnSlots)
      {
        ClickableComponent? slot = page.getComponentWithID(slotId);
        if (slot != null)
        {
          slot.rightNeighborID = CalendarSnapId;
        }
      }
    }

    ClickableComponent? trashCan = page.getComponentWithID(InventoryPage.region_trashCan);
    if (trashCan != null)
    {
      trashCan.leftNeighborID = QuestSnapId;
    }

    _calendarSnap.Value.rightNeighborID = QuestSnapId;
    _calendarSnap.Value.leftNeighborID =
      trinketSlot != null ? InventoryPage.region_trinkets : -99998;
    _calendarSnap.Value.upNeighborID = thirdLastSlotId;

    _questSnap.Value.leftNeighborID = CalendarSnapId;
    _questSnap.Value.rightNeighborID = trashCan != null ? InventoryPage.region_trashCan : -99998;
    _questSnap.Value.upNeighborID = lastSlotId;

    if (hasSO && hasQi)
    {
      _calendarSnap.Value.downNeighborID = QiOrdersSnapId;
      _questSnap.Value.downNeighborID = SpecialOrdersSnapId;

      _specialOrdersSnap.Value.upNeighborID = QuestSnapId;
      _specialOrdersSnap.Value.leftNeighborID = QiOrdersSnapId;
      _specialOrdersSnap.Value.rightNeighborID = -99998;
      _specialOrdersSnap.Value.downNeighborID = -99998;

      _qiOrdersSnap.Value.upNeighborID = CalendarSnapId;
      _qiOrdersSnap.Value.rightNeighborID = SpecialOrdersSnapId;
      _qiOrdersSnap.Value.leftNeighborID = -99998;
      _qiOrdersSnap.Value.downNeighborID = -99998;
    }
    else if (hasSO)
    {
      _calendarSnap.Value.downNeighborID = SpecialOrdersSnapId;
      _questSnap.Value.downNeighborID = SpecialOrdersSnapId;

      _specialOrdersSnap.Value.upNeighborID = QuestSnapId;
      _specialOrdersSnap.Value.leftNeighborID = -99998;
      _specialOrdersSnap.Value.rightNeighborID = -99998;
      _specialOrdersSnap.Value.downNeighborID = -99998;
    }
    else if (hasQi)
    {
      _calendarSnap.Value.downNeighborID = QiOrdersSnapId;
      _questSnap.Value.downNeighborID = QiOrdersSnapId;

      _qiOrdersSnap.Value.upNeighborID = CalendarSnapId;
      _qiOrdersSnap.Value.leftNeighborID = -99998;
      _qiOrdersSnap.Value.rightNeighborID = -99998;
      _qiOrdersSnap.Value.downNeighborID = -99998;
    }
    else
    {
      _calendarSnap.Value.downNeighborID = -99998;
      _questSnap.Value.downNeighborID = -99998;
    }
  }

  private void OnBoardSelected(string boardType)
  {
    MarkBoardViewed(boardType);
  }

  private static bool _waitingForMenuClose;

  private static void OpenMenuFromIcon(IClickableMenu menu)
  {
    menu.exitFunction = ReturnToInventory;

    // Clear flag before setting activeClickableMenu. The assignment may fire the
    // previous menu's exitFunction (which could be ReturnToInventory from a prior
    // OpenMenuFromIcon call, e.g. selector -> billboard). The false flag makes
    // that stale ReturnToInventory call no-op.
    _waitingForMenuClose = false;
    Game1.activeClickableMenu = menu;
    _waitingForMenuClose = true;

    // Some mods (e.g. Help Wanted) replace our menu with their own and overwrite
    // exitFunction, so our ReturnToInventory callback is lost.
    // Watch MenuChanged to catch when the final menu closes to null.
    // Unsubscribe first to prevent double-subscription when called in sequence
    // (e.g. selector menu -> billboard menu).
    if (_instance != null)
    {
      _instance._helper.Events.Display.MenuChanged -= OnMenuClosedWhileWatching;
      _instance._helper.Events.Display.MenuChanged += OnMenuClosedWhileWatching;
    }

    ModEntry.MonitorObject.Log(
      $"ShowCalendarAndBillboardOnGameMenuButton: watching menu close, menu={menu.GetType().Name}",
      LogLevel.Trace
    );
  }

  private static void StopWatchingMenuClose()
  {
    _waitingForMenuClose = false;
    if (_instance != null)
    {
      _instance._helper.Events.Display.MenuChanged -= OnMenuClosedWhileWatching;
      _instance._helper.Events.GameLoop.UpdateTicked -= ReopenGameMenuDeferred;
    }
  }

  private static void OnMenuClosedWhileWatching(object? sender, MenuChangedEventArgs e)
  {
    if (e.NewMenu != null)
    {
      return;
    }

    // Menu closed to null - unsubscribe handler, then call ReturnToInventory
    // which checks the flag and handles cleanup.
    if (_instance != null)
    {
      _instance._helper.Events.Display.MenuChanged -= OnMenuClosedWhileWatching;
    }

    ReturnToInventory();
  }

  internal static void ReturnToInventory()
  {
    if (!_waitingForMenuClose)
    {
      return;
    }

    StopWatchingMenuClose();

    // Defer by two ticks: tick 1 lets the watcher observe null, tick 2 reopens.
    if (Game1.activeClickableMenu == null && !Game1.eventUp && !Game1.dialogueUp)
    {
      // Suppress menu buttons so the game doesn't open its own GameMenu
      // during the brief null-menu window.
      SuppressMenuButtons();

      _reopenTicksRemaining = 1;
      if (_instance != null)
      {
        _instance._helper.Events.GameLoop.UpdateTicked += ReopenGameMenuDeferred;
      }
    }
    else
    {
      ModEntry.MonitorObject.Log(
        $"ShowCalendarAndBillboardOnGameMenuButton: return-to-inventory skipped, active={Game1.activeClickableMenu?.GetType().Name}",
        LogLevel.Trace
      );
    }
  }

  private static int _reopenTicksRemaining;

  private static void ReopenGameMenuDeferred(object? sender, UpdateTickedEventArgs e)
  {
    if (_reopenTicksRemaining > 0)
    {
      _reopenTicksRemaining--;
      // Suppress each tick to keep buttons quiet during the wait.
      SuppressMenuButtons();
      return;
    }

    if (_instance != null)
    {
      _instance._helper.Events.GameLoop.UpdateTicked -= ReopenGameMenuDeferred;
    }

    if (Game1.activeClickableMenu == null && !Game1.eventUp && !Game1.dialogueUp)
    {
      ModEntry.MonitorObject.Log(
        "ShowCalendarAndBillboardOnGameMenuButton: reopening GameMenu",
        LogLevel.Trace
      );
      Game1.activeClickableMenu = new GameMenu(GameMenu.inventoryTab, playOpeningSound: false);
      SuppressMenuButtons();
    }
    else
    {
      ModEntry.MonitorObject.Log(
        $"ShowCalendarAndBillboardOnGameMenuButton: return-to-inventory deferred but skipped, active={Game1.activeClickableMenu?.GetType().Name}",
        LogLevel.Trace
      );
    }
  }

  private static void SuppressMenuButtons()
  {
    if (_instance == null)
      return;

    foreach (InputButton button in Game1.options.menuButton)
    {
      _instance._helper.Input.Suppress(button.ToSButton());
    }
  }

  private static string GetBoardSignature(string boardType)
  {
    return string.Join(
      ",",
      Game1
        .player.team.availableSpecialOrders.Where(o => o.orderType.Value == boardType)
        .Select(o => o.questKey.Value)
        .OrderBy(k => k)
    );
  }

  private static bool HasUnviewedOrders(string boardType)
  {
    if (Game1.player.team.acceptedSpecialOrderTypes.Contains(boardType))
      return false;

    string signature = GetBoardSignature(boardType);
    if (string.IsNullOrEmpty(signature))
      return false;

    return !Game1.player.modData.TryGetValue(BoardSigPrefix + boardType, out string? viewedSig)
      || viewedSig != signature;
  }

  private static void MarkBoardViewed(string boardType)
  {
    Game1.player.modData[BoardSigPrefix + boardType] = GetBoardSignature(boardType);
  }

  private List<(string BoardType, string DisplayName)> GetAvailableModBoards()
  {
    if (_cachedModBoards != null && _cachedModBoardsDay == Game1.dayOfMonth)
      return _cachedModBoards;

    List<(string, string)> boards = [];
    if (_hasRidgesideVillage && Game1.player.eventsSeen.Contains("75160207"))
      boards.Add(("RSVTownSO", I18n.SpecialOrdersRSVTown()));
    if (
      _hasSunberryVillage
      && Game1.MasterPlayer.mailReceived.Contains("skellady.SBVCP_SpecialOrderBoardReady")
    )
      boards.Add(("SunberryBoard", I18n.SpecialOrdersSunberry()));
    if (
      _hasEscasModdingPlugins
      && Game1.player.eventsSeen.Contains("Lumisteria.MtVapius_Hamlet_OrderBoard")
    )
      boards.Add(("Esca.EMP/MtVapiusBoard", I18n.SpecialOrdersMtVapius()));
    if (
      _hasSwordAndSorcery
      && Game1.player.mailReceived.Contains("Mateo_SpecialOrders_BuildGuildMail")
    )
      boards.Add(("SwordSorcery", I18n.SpecialOrdersSwordSorcery()));

    _cachedModBoards = boards;
    _cachedModBoardsDay = Game1.dayOfMonth;
    return boards;
  }

  private bool ActivateBillboard()
  {
    if (
      !GameMenuHelper.IsTab(Game1.activeClickableMenu, GameMenu.inventoryTab)
      || Game1.player.CursorSlotItem != null
    )
    {
      return false;
    }

    int mouseX = (int)Utility.ModifyCoordinateForUIScale(Game1.getMouseX());
    int mouseY = (int)Utility.ModifyCoordinateForUIScale(Game1.getMouseY());

    bool isCalendar = _calendarBounds.Value.Contains(mouseX, mouseY);
    bool isQuest = _questBounds.Value.Contains(mouseX, mouseY);
    bool isSpecialOrders = _specialOrdersBounds.Value.Contains(mouseX, mouseY);
    bool isQiOrders = _qiOrdersBounds.Value.Contains(mouseX, mouseY);

    if (!isCalendar && !isQuest && !isSpecialOrders && !isQiOrders)
    {
      return false;
    }

    if (isQiOrders)
    {
      MarkBoardViewed("Qi");
      OpenMenuFromIcon(new SpecialOrdersBoard("Qi"));
      return true;
    }

    if (isSpecialOrders)
    {
      List<(string BoardType, string DisplayName)> modBoards = GetAvailableModBoards();
      if (modBoards.Count > 0)
      {
        HashSet<string> viewedTypes = [];
        if (!HasUnviewedOrders(""))
          viewedTypes.Add("");
        foreach ((string boardType, _) in modBoards)
        {
          if (!HasUnviewedOrders(boardType))
            viewedTypes.Add(boardType);
        }
        OpenMenuFromIcon(
          new SpecialOrdersBoardSelector(
            modBoards,
            OnBoardSelected,
            viewedTypes,
            returnToInventory: true
          )
        );
      }
      else
      {
        MarkBoardViewed("");
        OpenMenuFromIcon(new SpecialOrdersBoard());
      }
      return true;
    }

    // Quest board (with mod board selector if available)
    if (isQuest)
    {
      List<(string BoardType, string DisplayName)> modQuestBoards = GetAvailableModQuestBoards();
      if (modQuestBoards.Count > 0)
      {
        HashSet<string> viewedTypes = [];
        if (!Game1.CanAcceptDailyQuest())
          viewedTypes.Add("");
        foreach ((string boardType, _) in modQuestBoards)
        {
          if (!HasRsvUnacceptedQuest(boardType))
            viewedTypes.Add(boardType);
        }
        OpenMenuFromIcon(new QuestBoardSelector(modQuestBoards, OnQuestBoardSelected, viewedTypes));
        return true;
      }
    }

    if (Game1.questOfTheDay != null && string.IsNullOrEmpty(Game1.questOfTheDay.currentObjective))
    {
      Game1.questOfTheDay.currentObjective = "wat?";
    }

    OpenMenuFromIcon(new Billboard(dailyQuest: isQuest));
    return true;
  }

  public static void OpenQuestBoardFromKeybind()
  {
    if (_instance == null)
    {
      Game1.RefreshQuestOfTheDay();
      Game1.activeClickableMenu = new Billboard(true);
      return;
    }

    List<(string BoardType, string DisplayName)> modQuestBoards =
      _instance.GetAvailableModQuestBoards();
    if (modQuestBoards.Count > 0)
    {
      var viewedTypes = new HashSet<string>();
      if (!Game1.CanAcceptDailyQuest())
        viewedTypes.Add("");
      foreach ((string boardType, _) in modQuestBoards)
      {
        if (!_instance.HasRsvUnacceptedQuest(boardType))
          viewedTypes.Add(boardType);
      }
      Game1.activeClickableMenu = new QuestBoardSelector(
        modQuestBoards,
        _instance.OnQuestBoardSelected,
        viewedTypes
      );
      return;
    }

    Game1.RefreshQuestOfTheDay();
    Game1.activeClickableMenu = new Billboard(true);
  }

  public static void OpenSpecialOrdersBoardFromKeybind()
  {
    if (!SpecialOrder.IsSpecialOrdersBoardUnlocked())
      return;

    if (_instance == null)
    {
      Game1.activeClickableMenu = new SpecialOrdersBoard();
      return;
    }

    List<(string BoardType, string DisplayName)> modBoards = _instance.GetAvailableModBoards();
    if (modBoards.Count > 0)
    {
      var viewedTypes = new HashSet<string>();
      if (!HasUnviewedOrders(""))
        viewedTypes.Add("");
      foreach ((string boardType, _) in modBoards)
      {
        if (!HasUnviewedOrders(boardType))
          viewedTypes.Add(boardType);
      }
      Game1.activeClickableMenu = new SpecialOrdersBoardSelector(
        modBoards,
        _instance.OnBoardSelected,
        viewedTypes
      );
      return;
    }

    MarkBoardViewed("");
    Game1.activeClickableMenu = new SpecialOrdersBoard();
  }

  public static void OpenQiSpecialOrdersBoardFromKeybind()
  {
    if (!IslandWest.IsQiWalnutRoomDoorUnlocked(out _))
      return;

    MarkBoardViewed("Qi");
    Game1.activeClickableMenu = new SpecialOrdersBoard("Qi");
  }

  #region RSV Quest Board Support
  private void InitRsvQuestReflection()
  {
    if (_rsvQuestReflectionInit)
      return;
    _rsvQuestReflectionInit = true;
    if (!_hasRidgesideVillage)
      return;

    try
    {
      Assembly? rsvAssembly = null;
      foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
      {
        if (asm.GetName().Name == "RidgesideVillage")
        {
          rsvAssembly = asm;
          break;
        }
      }
      if (rsvAssembly == null)
        return;

      Type? questControllerType = rsvAssembly.GetType("RidgesideVillage.Questing.QuestController");
      Type? questBoardType = rsvAssembly.GetType("RidgesideVillage.Questing.RSVQuestBoard");
      Type? questDataType = rsvAssembly.GetType("RidgesideVillage.Questing.QuestData");
      if (questControllerType == null || questBoardType == null || questDataType == null)
        return;

      _rsvDailyQuestDataField = questControllerType.GetField(
        "dailyQuestData",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
      );

      _rsvQuestBoardCtor = questBoardType.GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
        null,
        [questDataType, typeof(string)],
        null
      );

      _rsvAcceptedDailyQuestField = questDataType.GetField(
        "acceptedDailyQuest",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
      );
      _rsvDailyTownQuestField = questDataType.GetField(
        "dailyTownQuest",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
      );
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"ShowCalendarAndBillboardOnGameMenuButton: RSV quest reflection init failed, {ex.Message}",
        LogLevel.Trace
      );
    }
  }

  private object? GetRsvQuestData()
  {
    if (_rsvDailyQuestDataField == null)
      return null;
    try
    {
      object? perScreen = _rsvDailyQuestDataField.GetValue(null);
      return perScreen?.GetType().GetProperty("Value")?.GetValue(perScreen);
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"ShowCalendarAndBillboardOnGameMenuButton: RSV quest data retrieval failed, {ex.Message}",
        LogLevel.Trace
      );
      return null;
    }
  }

  private bool HasRsvUnacceptedQuest(string boardType)
  {
    InitRsvQuestReflection();
    object? questData = GetRsvQuestData();
    if (questData == null)
      return false;

    try
    {
      if (boardType == "VillageQuestBoard")
      {
        object? quest = _rsvDailyTownQuestField?.GetValue(questData);
        bool accepted = (bool?)_rsvAcceptedDailyQuestField?.GetValue(questData) ?? true;
        return quest != null && !accepted;
      }
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"ShowCalendarAndBillboardOnGameMenuButton: RSV quest check failed for {boardType}, {ex.Message}",
        LogLevel.Trace
      );
    }
    return false;
  }

  private bool TryOpenRsvQuestBoard(string boardType)
  {
    InitRsvQuestReflection();
    if (_rsvQuestBoardCtor == null)
      return false;

    try
    {
      object? questData = GetRsvQuestData();
      if (questData == null)
        return false;

      object? board = _rsvQuestBoardCtor.Invoke([questData, boardType]);
      if (board is IClickableMenu menu)
      {
        OpenQuestBoard(menu);
        return true;
      }
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"ShowCalendarAndBillboardOnGameMenuButton: RSV quest board open failed for {boardType}, {ex.Message}",
        LogLevel.Trace
      );
    }
    return false;
  }

  private List<(string BoardType, string DisplayName)> GetAvailableModQuestBoards()
  {
    if (_cachedModQuestBoards != null && _cachedModQuestBoardsDay == Game1.dayOfMonth)
      return _cachedModQuestBoards;

    List<(string, string)> boards = [];
    if (_hasRidgesideVillage)
      boards.Add(("VillageQuestBoard", I18n.SpecialOrdersRSVTown()));

    _cachedModQuestBoards = boards;
    _cachedModQuestBoardsDay = Game1.dayOfMonth;
    return boards;
  }

  private void OnQuestBoardSelected(string boardType)
  {
    if (boardType == "")
    {
      if (Game1.questOfTheDay != null && string.IsNullOrEmpty(Game1.questOfTheDay.currentObjective))
        Game1.questOfTheDay.currentObjective = "wat?";
      OpenQuestBoard(new Billboard(dailyQuest: true));
    }
    else
    {
      if (!TryOpenRsvQuestBoard(boardType))
      {
        OpenQuestBoard(new Billboard(dailyQuest: true));
      }
    }
  }

  /// <summary>
  /// Opens a quest board menu. Uses OpenMenuFromIcon when in an icon-opened flow
  /// (so return-to-inventory works), otherwise sets the menu directly (keybind flow).
  /// </summary>
  private static void OpenQuestBoard(IClickableMenu menu)
  {
    if (_waitingForMenuClose)
    {
      OpenMenuFromIcon(menu);
    }
    else
    {
      Game1.activeClickableMenu = menu;
    }
  }
  #endregion

  #endregion
}
