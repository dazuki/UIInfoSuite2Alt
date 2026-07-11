using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.Internal;
using StardewValley.Menus;
using StardewValley.Objects;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Infrastructure.Extensions;
using Object = StardewValley.Object;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowBirthdayIcon : IDisposable
{
  #region Properties
  private readonly PerScreen<List<NPC>> _birthdayNPCs = new(() => []);

  private readonly PerScreen<List<ClickableTextureComponent>> _birthdayIcons = new(() => []);

  private readonly PerScreen<HashSet<string>> _ownedItemIds = new(() => new HashSet<string>());

  private readonly PerScreen<HashSet<string>> _inventoryItemIds = new(() => new HashSet<string>());

  private readonly PerScreen<Dictionary<string, List<LovedGift>>> _lovedGiftsByNpc = new(() =>
    new Dictionary<string, List<LovedGift>>()
  );

  private readonly PerScreen<Dictionary<string, List<Item>>> _lovedItemsByNpc = new(() =>
    new Dictionary<string, List<Item>>()
  );

  // Birthday NPCs hidden today only because the player hasn't met them yet; re-scanned once
  // any of them becomes met (talking adds a friendshipData entry) so the icon appears same-day.
  private readonly PerScreen<HashSet<string>> _pendingUnmetBirthdayNpcs = new(() =>
    new HashSet<string>()
  );

  private readonly record struct LovedGift(Item Item, bool InInventory);

  private bool Enabled { get; set; }
  private bool HideBirthdayIfFullFriendShip { get; set; }
  private bool UseStackedBirthdayIcons { get; set; }
  private bool ShowUnmetVillagers { get; set; } = true;
  private bool ShowUnrevealedLoves { get; set; } = true;
  private readonly IModHelper _helper;
  #endregion


  #region Life cycle
  public ShowBirthdayIcon(IModHelper helper)
  {
    _helper = helper;
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool showBirthdayIcon)
  {
    Enabled = showBirthdayIcon;

    _helper.Events.GameLoop.DayStarted -= OnDayStarted;
    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
    _helper.Events.Player.InventoryChanged -= OnInventoryChanged;

    if (showBirthdayIcon)
    {
      CheckForBirthday();
      _helper.Events.GameLoop.DayStarted += OnDayStarted;
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
      _helper.Events.Player.InventoryChanged += OnInventoryChanged;
    }
  }

  public void ToggleDisableOnMaxFriendshipOption(bool hideBirthdayIfFullFriendShip)
  {
    if (HideBirthdayIfFullFriendShip == hideBirthdayIfFullFriendShip)
    {
      return;
    }

    HideBirthdayIfFullFriendShip = hideBirthdayIfFullFriendShip;
    ToggleOption(Enabled);
  }

  /// <summary>Seed the flag before ToggleOption runs so the initial birthday scan isn't redone.</summary>
  public void InitDisableOnMaxFriendshipOption(bool hideBirthdayIfFullFriendShip)
  {
    HideBirthdayIfFullFriendShip = hideBirthdayIfFullFriendShip;
  }

  /// <summary>Seed the flag before ToggleOption runs so the initial birthday scan isn't redone.</summary>
  public void InitShowUnmetVillagersOption(bool showUnmetVillagers)
  {
    ShowUnmetVillagers = showUnmetVillagers;
  }

  public void ToggleShowUnmetVillagersOption(bool showUnmetVillagers)
  {
    if (ShowUnmetVillagers == showUnmetVillagers)
    {
      return;
    }

    ShowUnmetVillagers = showUnmetVillagers;
    ToggleOption(Enabled);
  }

  public void ToggleStackedOption(bool useStackedBirthdayIcons)
  {
    UseStackedBirthdayIcons = useStackedBirthdayIcons;
    _birthdayIcons.Value.Clear();
  }

  public void ToggleShowUnrevealedLovesOption(bool showUnrevealedLoves)
  {
    ShowUnrevealedLoves = showUnrevealedLoves;
    RebuildLovedGiftsView();
  }
  #endregion


  #region Event subscriptions
  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (e.IsOneSecond)
    {
      CheckForGiftGiven();
      CheckForNewlyMetBirthdayNpc();
    }
  }

  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    // A just-disposed instance can still receive the in-flight raise via SMAPI's handler snapshot.
    if (!Enabled)
    {
      return;
    }

    CheckForBirthday();
  }

  private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
  {
    if (!e.IsLocalPlayer || _birthdayNPCs.Value.Count == 0)
    {
      return;
    }

    bool inventoryChanged = false;
    foreach (Item item in e.Added)
    {
      if (string.IsNullOrEmpty(item?.QualifiedItemId))
      {
        continue;
      }

      if (_inventoryItemIds.Value.Add(item.QualifiedItemId))
      {
        inventoryChanged = true;
      }
    }

    foreach (Item item in e.Removed)
    {
      if (string.IsNullOrEmpty(item?.QualifiedItemId))
      {
        continue;
      }

      if (!Game1.player.Items.Any(i => i?.QualifiedItemId == item.QualifiedItemId))
      {
        if (_inventoryItemIds.Value.Remove(item.QualifiedItemId))
        {
          inventoryChanged = true;
        }
      }
    }

    if (inventoryChanged)
    {
      RebuildLovedGiftsView();
    }
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (UIElementUtils.IsRenderingNormally())
    {
      EnqueueBirthdayIcons();
    }
  }
  #endregion


  #region Logic
  private void CheckForGiftGiven()
  {
    List<NPC> npcs = _birthdayNPCs.Value;
    // Iterate end-to-start so RemoveAt doesn't shift unread indices.
    for (int i = npcs.Count - 1; i >= 0; i--)
    {
      Friendship? friendship = GetFriendshipWithNPC(npcs[i].Name);
      if (friendship != null && friendship.GiftsToday > 0)
      {
        _lovedGiftsByNpc.Value.Remove(npcs[i].Name);
        _lovedItemsByNpc.Value.Remove(npcs[i].Name);
        npcs.RemoveAt(i);
        _birthdayIcons.Value.Clear();
      }
    }
  }

  private void CheckForNewlyMetBirthdayNpc()
  {
    HashSet<string> pending = _pendingUnmetBirthdayNpcs.Value;
    if (pending.Count == 0)
    {
      return;
    }

    foreach (string name in pending)
    {
      if (Game1.player.friendshipData.ContainsKey(name))
      {
        CheckForBirthday();
        return;
      }
    }
  }

  private void CheckForBirthday()
  {
    _birthdayNPCs.Value.Clear();
    _birthdayIcons.Value.Clear();
    _lovedGiftsByNpc.Value.Clear();
    _lovedItemsByNpc.Value.Clear();
    _pendingUnmetBirthdayNpcs.Value.Clear();
    HashSet<string> seen = new();
    // ForEachVillager walks building interiors too (includeInteriors), so NPCs living in
    // modded interiors (RSV, SVE) are scanned; Game1.locations alone skips those.
    Utility.ForEachVillager(character =>
    {
      if (character.isBirthday() && seen.Add(character.Name))
      {
        // Match the vanilla calendar's visibility (Billboard.GetBirthdays): hide NPCs flagged
        // Calendar=HiddenAlways (e.g. VMV's Rayan) entirely.
        bool met = Game1.player.friendshipData.ContainsKey(character.Name);
        CalendarBehavior? calendar = character.GetData()?.Calendar;
        if (calendar == CalendarBehavior.HiddenAlways)
        {
          return true;
        }

        // Defer NPCs hidden only because they're unmet: HiddenUntilMet (calendar-gated on
        // meeting) or, when the option is off, any unmet villager. Track them so the
        // one-second tick re-scans and shows the icon the moment the player meets them.
        if (!met && (calendar == CalendarBehavior.HiddenUntilMet || !ShowUnmetVillagers))
        {
          _pendingUnmetBirthdayNpcs.Value.Add(character.Name);
          return true;
        }

        // Null friendship (unmet AlwaysShown NPC) renders as 0 hearts in the tooltip.
        Friendship? friendship = GetFriendshipWithNPC(character.Name);
        bool atMaxFriendship =
          HideBirthdayIfFullFriendShip
          && friendship != null
          && friendship.Points
            >= Utility.GetMaximumHeartsForCharacter(character) * NPC.friendshipPointsPerHeartLevel;
        if (!atMaxFriendship)
        {
          _birthdayNPCs.Value.Add(character);
        }
      }

      return true;
    });

    if (_birthdayNPCs.Value.Count > 0)
    {
      ModEntry.MonitorObject.LogOnce(
        $"ShowBirthdayIcon: birthdays today, npcs=[{string.Join(", ", _birthdayNPCs.Value.Select(n => n.Name))}]",
        LogLevel.Trace
      );

      ScanOwnedItems();
      RecomputeLovedItemsByNpc();
      RebuildLovedGiftsView();
    }
  }

  private static Friendship? GetFriendshipWithNPC(string name)
  {
    try
    {
      if (Game1.player.friendshipData.TryGetValue(name, out Friendship friendship))
      {
        return friendship;
      }

      return null;
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.LogOnce(
        $"ShowBirthdayIcon: failed to get friendship data, npc={name}\n{ex}",
        LogLevel.Error
      );
    }

    return null;
  }

  private void ScanOwnedItems()
  {
    HashSet<string> ids = _ownedItemIds.Value;
    HashSet<string> invIds = _inventoryItemIds.Value;
    ids.Clear();
    invIds.Clear();

    // ForEachItemContext walks the whole world; filter it down to items the player actually
    // owns, matching LookupAnything's owned-item rules.
    Utility.ForEachItemContext(
      (in ForEachItemContext context) =>
      {
        Item item = context.Item;
        try
        {
          // Skip forage/litter lying on the ground rather than forageables the player stored.
          if (
            item is Object obj
            && (obj.IsSpawnedObject || obj.isForage() || obj.Category == Object.litterCategory)
          )
          {
            return true;
          }

          // Skip machine output that isn't ready yet (e.g. a fermenting keg): not owned until
          // collected. Casks can be emptied anytime, so their contents still count.
          IList<object> path = context.GetPath();
          if (
            path.Count > 0
            && path[^1] is Object { MinutesUntilReady: > 0 } machine
            && machine is not Cask
          )
          {
            return true;
          }

          if (!string.IsNullOrEmpty(item?.QualifiedItemId))
          {
            ids.Add(item.QualifiedItemId);
          }
        }
        catch (Exception ex)
        {
          // A modded item overriding isForage/etc. could throw; skip just that item so the
          // rest of the scan (including the player inventory, walked last) still completes.
          ModEntry.MonitorObject.LogOnce(
            $"ShowBirthdayIcon: skipped an item during owned scan, id={item?.QualifiedItemId}, {ex.Message}",
            LogLevel.Trace
          );
        }

        return true;
      }
    );

    // Inventory is always owned; add it directly so it's never lost if the world scan bails.
    foreach (Item? item in Game1.player.Items)
    {
      if (!string.IsNullOrEmpty(item?.QualifiedItemId))
      {
        invIds.Add(item.QualifiedItemId);
        ids.Add(item.QualifiedItemId);
      }
    }

    ModEntry.MonitorObject.Log(
      $"ShowBirthdayIcon: owned item scan complete, uniqueItems={ids.Count}, inInventory={invIds.Count}",
      LogLevel.Trace
    );
  }

  // Heavy: iterates every owned item id, asks each birthday NPC for gift taste once.
  // Only run when the owned-id set changes (DayStarted scan, or a new id appears via pickup).
  private void RecomputeLovedItemsByNpc()
  {
    Dictionary<string, List<Item>> cache = _lovedItemsByNpc.Value;
    cache.Clear();

    if (_birthdayNPCs.Value.Count == 0 || _ownedItemIds.Value.Count == 0)
    {
      return;
    }

    List<Item> ownedItems = new(_ownedItemIds.Value.Count);
    foreach (string id in _ownedItemIds.Value)
    {
      Item? item = TryCreateItem(id);
      if (item != null)
      {
        ownedItems.Add(item);
      }
    }

    foreach (NPC npc in _birthdayNPCs.Value)
    {
      List<Item> loved = new();
      foreach (Item item in ownedItems)
      {
        int taste;
        try
        {
          taste = npc.getGiftTasteForThisItem(item);
        }
        catch (Exception ex)
        {
          ModEntry.MonitorObject.LogOnce(
            $"ShowBirthdayIcon: failed to get gift taste for '{item.QualifiedItemId}' / NPC '{npc.Name}'; skipping, {ex.Message}",
            LogLevel.Trace
          );
          continue;
        }

        if (taste == NPC.gift_taste_love)
        {
          loved.Add(item);
        }
      }

      loved.Sort(
        (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName)
      );
      cache[npc.Name] = loved;
    }
  }

  private void RebuildLovedGiftsView()
  {
    Dictionary<string, List<LovedGift>> map = _lovedGiftsByNpc.Value;
    map.Clear();

    if (_birthdayNPCs.Value.Count == 0)
    {
      return;
    }

    HashSet<string> inventoryIds = _inventoryItemIds.Value;
    foreach (NPC npc in _birthdayNPCs.Value)
    {
      if (!_lovedItemsByNpc.Value.TryGetValue(npc.Name, out List<Item>? loved))
      {
        continue;
      }

      List<LovedGift> view = new(loved.Count);
      foreach (Item item in loved)
      {
        view.Add(new LovedGift(item, inventoryIds.Contains(item.QualifiedItemId)));
      }

      map[npc.Name] = view.OrderByDescending(g => g.InInventory).ToList();
    }
  }

  private static Item? TryCreateItem(string qualifiedId)
  {
    try
    {
      return ItemRegistry.Create(qualifiedId, allowNull: true);
    }
    catch
    {
      return null;
    }
  }

  private static readonly Rectangle BirthdayBackgroundSource = new(228, 409, 16, 16);
  private static readonly Rectangle FilledHeartSource = new(211, 428, 7, 6);
  private static readonly Rectangle EmptyHeartSource = new(218, 428, 7, 6);
  private const float IconScale = 2.9f;
  private const float HeadshotScale = 2f;
  private const int HeadshotSize = 16;
  private const float HeartScale = 3f;

  private void EnqueueBirthdayIcons()
  {
    List<NPC> npcs = _birthdayNPCs.Value;
    if (npcs.Count == 0)
    {
      return;
    }

    List<ClickableTextureComponent> icons = _birthdayIcons.Value;

    if (UseStackedBirthdayIcons && npcs.Count >= 2)
    {
      EnqueueStackedIcon(npcs, icons);
    }
    else
    {
      EnqueueIndividualIcons(npcs, icons);
    }
  }

  private void EnqueueIndividualIcons(List<NPC> npcs, List<ClickableTextureComponent> icons)
  {
    if (icons.Count != npcs.Count)
    {
      icons.Clear();
      foreach (NPC npc in npcs)
      {
        icons.Add(
          new ClickableTextureComponent(
            npc.Name,
            Rectangle.Empty,
            null,
            npc.Name,
            npc.Sprite.Texture,
            npc.GetHeadShot(),
            HeadshotScale
          )
        );
      }
    }

    for (int i = 0; i < npcs.Count; i++)
    {
      int capturedI = i;
      IconHandler.Handler.EnqueueIcon(
        "Birthday",
        (batch, pos) =>
        {
          DrawBirthdayBackground(batch, pos);
          icons[capturedI].bounds = GetIconBounds(pos);
          icons[capturedI].sourceRect = npcs[capturedI].GetHeadShot();
          icons[capturedI].draw(batch);
        },
        batch =>
        {
          if (icons[capturedI].containsPoint(Game1.getMouseX(), Game1.getMouseY()))
          {
            DrawIndividualTooltip(batch, npcs[capturedI]);
          }
        }
      );
    }
  }

  private static void EnqueueStackedIcon(List<NPC> npcs, List<ClickableTextureComponent> icons)
  {
    if (icons.Count != 1 || icons[0].name != "Stacked")
    {
      icons.Clear();
      icons.Add(
        new ClickableTextureComponent(
          "Stacked",
          Rectangle.Empty,
          null,
          "Stacked",
          Game1.mouseCursors,
          BirthdayBackgroundSource,
          IconScale
        )
      );
    }

    IconHandler.Handler.EnqueueIcon(
      "Birthday",
      (batch, pos) =>
      {
        DrawBirthdayBackground(batch, pos);

        int count = npcs.Count;
        int digitCount = count == 0 ? 1 : (int)Math.Floor(Math.Log10(count)) + 1;
        int totalDigitWidth = digitCount * Tools.TinyDigitStep;
        float iconCenterX = pos.X + 16f * IconScale / 2f;
        float iconCenterY = pos.Y - 3f + 20f * IconScale / 2f;
        float xOffset = 0f;
        var digitPos = new Vector2(iconCenterX - totalDigitWidth / 2f, iconCenterY - 7f);
        Tools.DrawTinyDigits(
          batch,
          count,
          digitPos,
          ref xOffset,
          Tools.TinyDigitStep,
          Color.White,
          Color.Black * 0.3f
        );

        icons[0].bounds = GetIconBounds(pos);
      },
      batch =>
      {
        if (icons[0].containsPoint(Game1.getMouseX(), Game1.getMouseY()))
        {
          DrawStackedTooltip(batch, npcs);
        }
      }
    );
  }

  private static void DrawBirthdayBackground(SpriteBatch batch, Point pos)
  {
    batch.Draw(
      Game1.mouseCursors,
      new Vector2(pos.X, pos.Y - 3),
      BirthdayBackgroundSource,
      Color.White,
      0.0f,
      Vector2.Zero,
      IconScale,
      SpriteEffects.None,
      1f
    );
  }

  private static Rectangle GetIconBounds(Point pos)
  {
    return new Rectangle(pos.X - 7, pos.Y - 5, (int)(16.0 * IconScale), (int)(16.0 * IconScale));
  }

  // Pixel-granular bottom-up fill mirroring ShowAccurateHearts.DrawPartialHeart.
  // Assumes HeartScale is a whole-number scale factor.
  private static void DrawPartialHeartFill(SpriteBatch batch, Vector2 heartPos, int partialPoints)
  {
    int scale = (int)HeartScale;
    int totalHeight = FilledHeartSource.Height * scale;
    int fillHeight = (int)
      Math.Ceiling((double)partialPoints / NPC.friendshipPointsPerHeartLevel * totalHeight);
    int completeRows = fillHeight / scale;
    int partialPixels = fillHeight % scale;
    var tint = Color.White * 0.7f;

    if (completeRows > 0)
    {
      int srcY = FilledHeartSource.Y + FilledHeartSource.Height - completeRows;
      int dstY = (int)heartPos.Y + (FilledHeartSource.Height - completeRows) * scale;
      batch.Draw(
        Game1.mouseCursors,
        new Rectangle((int)heartPos.X, dstY, FilledHeartSource.Width * scale, completeRows * scale),
        new Rectangle(FilledHeartSource.X, srcY, FilledHeartSource.Width, completeRows),
        tint
      );
    }

    if (partialPixels > 0)
    {
      int srcRow = FilledHeartSource.Height - completeRows - 1;
      int srcY = FilledHeartSource.Y + srcRow;
      int dstY = (int)heartPos.Y + srcRow * scale + (scale - partialPixels);
      batch.Draw(
        Game1.mouseCursors,
        new Rectangle((int)heartPos.X, dstY, FilledHeartSource.Width * scale, partialPixels),
        new Rectangle(FilledHeartSource.X, srcY, FilledHeartSource.Width, 1),
        tint
      );
    }
  }

  private const int GiftsPerLine = 3;
  private const int GiftRowCap = 2;
  private const float GiftCakeScale = 2f;
  private const float GiftHeartOverlayScale = 2f;

  private void DrawIndividualTooltip(SpriteBatch batch, NPC npc)
  {
    SpriteFont font = Game1.smallFont;
    Friendship? friendship = GetFriendshipWithNPC(npc.Name);
    int points = friendship?.Points ?? 0;
    int maxHearts = Utility.GetMaximumHeartsForCharacter(npc);
    int totalHeartSlots = Math.Max(maxHearts, 10);
    bool isDatable = npc.datable.Value;
    bool isDating = friendship?.IsDating() ?? false;
    bool isMarried = friendship?.IsMarried() ?? false;

    string title = string.Format(I18n.NpcBirthday(), npc.displayName);
    float heartW = FilledHeartSource.Width * HeartScale;
    float heartH = FilledHeartSource.Height * HeartScale;
    float heartsRowWidth = totalHeartSlots * heartW + Math.Max(0, totalHeartSlots - 1) * HeartScale;

    List<LovedGift> loved = _lovedGiftsByNpc.Value.TryGetValue(npc.Name, out List<LovedGift>? list)
      ? list
      : new List<LovedGift>();

    // Build colored segments ("Name," or "Name" with inventory-aware color), chunked into rows.
    List<(string Text, Color Color)> segments = new();
    for (int i = 0; i < loved.Count; i++)
    {
      string text =
        i < loved.Count - 1 ? loved[i].Item.DisplayName + "," : loved[i].Item.DisplayName;
      Color color = loved[i].InInventory ? Tools.TooltipGreen : Game1.textColor;
      segments.Add((text, color));
    }

    // Cap the displayed gifts so a large owned-loved list can't fill the screen;
    // holding the expand keybind reveals the full list.
    bool expandLoves = ModEntry.ModConfig.ExpandBirthdayLovesKeybind.IsDown();
    int maxShownGifts = GiftRowCap * GiftsPerLine;
    string? moreText = null;
    if (!expandLoves && segments.Count > maxShownGifts)
    {
      int hidden = segments.Count - maxShownGifts;
      segments.RemoveRange(maxShownGifts, hidden);
      moreText = I18n.BirthdayLovesMore(
        count: hidden,
        key: ModEntry.ModConfig.ExpandBirthdayLovesKeybind.ToString()
      );
    }

    List<List<(string Text, Color Color)>> giftRows = new();
    for (int start = 0; start < segments.Count; start += GiftsPerLine)
    {
      int end = Math.Min(start + GiftsPerLine, segments.Count);
      giftRows.Add(segments.GetRange(start, end - start));
    }

    float spaceWidth = font.MeasureString(" ").X;

    float giftCakeW = BirthdayBackgroundSource.Width * GiftCakeScale;
    float giftCakeH = BirthdayBackgroundSource.Height * GiftCakeScale;
    float giftIconGap = 6f;
    float giftTextIndent = giftCakeW + giftIconGap;
    float giftLineHeight = Math.Max(font.LineSpacing, giftCakeH);

    const int padding = 16;
    const int sectionGap = 6;

    float titleWidth = font.MeasureString(title).X;
    float maxGiftLineWidth = 0f;
    foreach (var row in giftRows)
    {
      float w = 0f;
      for (int j = 0; j < row.Count; j++)
      {
        w += font.MeasureString(row[j].Text).X;
        if (j < row.Count - 1)
        {
          w += spaceWidth;
        }
      }

      maxGiftLineWidth = Math.Max(maxGiftLineWidth, w);
    }
    float moreTextWidth = moreText != null ? font.MeasureString(moreText).X : 0f;
    bool showGiftSection = ShowUnrevealedLoves && loved.Count > 0;
    float giftSectionWidth = showGiftSection
      ? giftTextIndent + Math.Max(maxGiftLineWidth, moreTextWidth)
      : 0f;

    float innerWidth = Math.Max(Math.Max(titleWidth, heartsRowWidth), giftSectionWidth);

    int tooltipWidth = (int)innerWidth + padding * 2;
    int giftLineCount = giftRows.Count + (moreText != null ? 1 : 0);
    int giftSectionHeight = showGiftSection ? (int)(giftLineCount * giftLineHeight) : 0;
    int tooltipHeight =
      padding * 2
      + font.LineSpacing
      + sectionGap
      + (int)heartH
      + (showGiftSection ? sectionGap + giftSectionHeight : 0);

    int mouseX = Game1.getMouseX();
    int mouseY = Game1.getMouseY();
    int x = mouseX + 32;
    int y = mouseY + 32;
    Rectangle safeArea = Utility.getSafeArea();
    if (x + tooltipWidth > safeArea.Right)
    {
      x = safeArea.Right - tooltipWidth;
    }
    if (y + tooltipHeight > safeArea.Bottom)
    {
      y = safeArea.Bottom - tooltipHeight;
    }

    IClickableMenu.drawTextureBox(
      batch,
      Game1.menuTexture,
      new Rectangle(0, 256, 60, 60),
      x,
      y,
      tooltipWidth,
      tooltipHeight,
      Color.White
    );

    float textX = x + padding;
    float textY = y + padding;

    Tools.DrawShadowedText(
      batch,
      font,
      title,
      new Vector2(textX, textY),
      Game1.textColor,
      Game1.textShadowColor
    );
    textY += font.LineSpacing + sectionGap;

    // hearts (mirrors SocialPage.drawNPCSlotHeart: always Math.Max(max, 10) slots,
    // slots 8-9 rendered as "locked" for datable NPCs until the player is dating/married;
    // partial heart uses pixel-granular bottom-up fill like ShowAccurateHearts)
    int fullHearts = points / NPC.friendshipPointsPerHeartLevel;
    int partialPoints = points % NPC.friendshipPointsPerHeartLevel;
    for (int i = 0; i < totalHeartSlots; i++)
    {
      bool locked = isDatable && !isDating && !isMarried && i >= 8 && i < 10;
      var heartPos = new Vector2(textX + i * (heartW + HeartScale), textY);

      if (locked)
      {
        batch.Draw(
          Game1.mouseCursors,
          heartPos,
          FilledHeartSource,
          Color.Black * 0.35f,
          0f,
          Vector2.Zero,
          HeartScale,
          SpriteEffects.None,
          0.91f
        );
      }
      else if (i < fullHearts)
      {
        batch.Draw(
          Game1.mouseCursors,
          heartPos,
          FilledHeartSource,
          Color.White,
          0f,
          Vector2.Zero,
          HeartScale,
          SpriteEffects.None,
          0.91f
        );
      }
      else
      {
        batch.Draw(
          Game1.mouseCursors,
          heartPos,
          EmptyHeartSource,
          Color.White,
          0f,
          Vector2.Zero,
          HeartScale,
          SpriteEffects.None,
          0.91f
        );
        if (i == fullHearts && partialPoints > 0)
        {
          DrawPartialHeartFill(batch, heartPos, partialPoints);
        }
      }
    }
    textY += heartH + sectionGap;

    // Inventory-carried gifts render in green and sort to the front (LA-style).
    if (!showGiftSection)
    {
      return;
    }

    for (int i = 0; i < giftRows.Count; i++)
    {
      float lineY = textY + i * giftLineHeight;
      if (i == 0)
      {
        DrawCakeWithHeartBadge(batch, textX, lineY, giftLineHeight, FilledHeartSource);
      }

      float textLineY = lineY + (giftLineHeight - font.LineSpacing) / 2f;
      float segX = textX + giftTextIndent;
      var row = giftRows[i];
      for (int j = 0; j < row.Count; j++)
      {
        Tools.DrawShadowedText(
          batch,
          font,
          row[j].Text,
          new Vector2(segX, textLineY),
          row[j].Color,
          Game1.textShadowColor
        );
        segX += font.MeasureString(row[j].Text).X;
        if (j < row.Count - 1)
        {
          segX += spaceWidth;
        }
      }
    }

    if (moreText != null)
    {
      float moreLineY = textY + giftRows.Count * giftLineHeight;
      float moreTextY = moreLineY + (giftLineHeight - font.LineSpacing) / 2f;
      Tools.DrawShadowedText(
        batch,
        font,
        moreText,
        new Vector2(textX + giftTextIndent, moreTextY),
        Tools.TooltipYellow,
        Game1.textShadowColor
      );
    }
  }

  private static void DrawCakeWithHeartBadge(
    SpriteBatch batch,
    float textX,
    float rowY,
    float rowHeight,
    Rectangle heartSource
  )
  {
    float cakeW = BirthdayBackgroundSource.Width * GiftCakeScale;
    float cakeH = BirthdayBackgroundSource.Height * GiftCakeScale;
    float heartW = heartSource.Width * GiftHeartOverlayScale;
    float heartH = heartSource.Height * GiftHeartOverlayScale;
    float cakeY = rowY + (rowHeight - cakeH) / 2f;

    batch.Draw(
      Game1.mouseCursors,
      new Vector2(textX, cakeY),
      BirthdayBackgroundSource,
      Color.White,
      0f,
      Vector2.Zero,
      GiftCakeScale,
      SpriteEffects.None,
      0.91f
    );
    batch.Draw(
      Game1.mouseCursors,
      new Vector2(textX + cakeW - heartW, cakeY + cakeH - heartH),
      heartSource,
      Color.White,
      0f,
      Vector2.Zero,
      GiftHeartOverlayScale,
      SpriteEffects.None,
      0.92f
    );
  }

  private static void DrawStackedTooltip(SpriteBatch batch, List<NPC> npcs)
  {
    SpriteFont font = Game1.smallFont;
    string header = I18n.BirthdaysToday();

    float headshotDrawSize = HeadshotSize * HeadshotScale;
    float headshotPadding = 4f;
    float lineHeight = Math.Max(font.LineSpacing, headshotDrawSize);
    float headerWidth = font.MeasureString(header).X;
    float maxNameWidth = 0f;
    foreach (NPC npc in npcs)
    {
      float nameWidth = font.MeasureString(npc.displayName).X;
      if (nameWidth > maxNameWidth)
      {
        maxNameWidth = nameWidth;
      }
    }

    float npcLineWidth = headshotDrawSize + headshotPadding + maxNameWidth;
    float contentWidth = Math.Max(headerWidth, npcLineWidth);
    int padding = 16;
    int tooltipWidth = (int)contentWidth + padding * 2;
    int tooltipHeight = (int)(font.LineSpacing + 4 + npcs.Count * lineHeight) + padding * 2;

    int mouseX = Game1.getMouseX();
    int mouseY = Game1.getMouseY();
    int x = mouseX + 32;
    int y = mouseY + 32;
    Rectangle safeArea = Utility.getSafeArea();
    if (x + tooltipWidth > safeArea.Right)
    {
      x = safeArea.Right - tooltipWidth;
    }

    if (y + tooltipHeight > safeArea.Bottom)
    {
      y = safeArea.Bottom - tooltipHeight;
    }

    IClickableMenu.drawTextureBox(
      batch,
      Game1.menuTexture,
      new Rectangle(0, 256, 60, 60),
      x,
      y,
      tooltipWidth,
      tooltipHeight,
      Color.White
    );

    float textX = x + padding;
    float textY = y + padding;
    Tools.DrawShadowedText(
      batch,
      font,
      header,
      new Vector2(textX, textY),
      Game1.textColor,
      Game1.textShadowColor
    );
    textY += font.LineSpacing + 4;

    foreach (NPC npc in npcs)
    {
      Rectangle headShot = npc.GetHeadShot();
      float scale = headshotDrawSize / headShot.Width;
      float headshotY = textY + (lineHeight - headShot.Height * scale) / 2f - 10f;

      batch.Draw(
        npc.Sprite.Texture,
        new Vector2(textX, headshotY),
        headShot,
        Color.White,
        0f,
        Vector2.Zero,
        scale,
        SpriteEffects.None,
        1f
      );

      float nameY = textY + (lineHeight - font.LineSpacing) / 2f;
      Tools.DrawShadowedText(
        batch,
        font,
        npc.displayName,
        new Vector2(textX + headshotDrawSize + headshotPadding, nameY),
        Game1.textColor,
        Game1.textShadowColor
      );

      textY += lineHeight;
    }
  }
  #endregion
}
