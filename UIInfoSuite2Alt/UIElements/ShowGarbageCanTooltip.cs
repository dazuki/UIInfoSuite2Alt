using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using UIInfoSuite2Alt.Compatibility.Helpers;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Infrastructure.Helpers;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowGarbageCanTooltip : IDisposable
{
  private const string ActionPrefix = "Garbage ";
  private const int MaxItemsShown = 5;

  private readonly IModHelper _helper;
  private readonly PerScreen<Vector2?> _hoveredTile = new();
  private readonly PerScreen<List<Item>> _predictedItems = new(() => []);
  private readonly PerScreen<bool> _alreadyChecked = new();
  private readonly PerScreen<bool> _hasPrediction = new();
  private readonly PerScreen<int?> _lockedMinLevel = new();
  private readonly PerScreen<bool> _fromGarbageDayChest = new();

  private readonly PerScreen<Vector2?> _cachedTile = new();

  public ShowGarbageCanTooltip(IModHelper helper)
  {
    _helper = helper;
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool enabled)
  {
    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
    _helper.Events.GameLoop.DayStarted -= OnDayStarted;

    if (!enabled)
    {
      return;
    }

    _helper.Events.Display.RenderingHud += OnRenderingHud;
    _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    _helper.Events.GameLoop.DayStarted += OnDayStarted;
  }

  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    ResetState();
  }

  private void ResetState()
  {
    _cachedTile.Value = null;
    _predictedItems.Value = [];
    _hasPrediction.Value = false;
    _lockedMinLevel.Value = null;
    _fromGarbageDayChest.Value = false;
  }

  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (!e.IsMultipleOf(4))
    {
      return;
    }

    _hoveredTile.Value = null;

    GameLocation? location = Game1.currentLocation;
    if (location == null)
    {
      ResetState();
      return;
    }

    Vector2 mouseTile = Game1.currentCursorTile;
    Vector2 gamepadTile =
      Game1.player.CurrentTool != null
        ? Utility.snapToInt(Game1.player.GetToolLocation() / Game1.tileSize)
        : Utility.snapToInt(Game1.player.GetGrabTile());
    Vector2 tile =
      Game1.options.gamepadControls && Game1.timerUntilMouseFade <= 0 ? gamepadTile : mouseTile;

    if (!TryResolveGarbageCanId(location, tile, out string id))
    {
      ResetState();
      return;
    }

    _hoveredTile.Value = tile;

    // GarbageDay chest contents can change intra-day; don't use cached value for that path
    if (_cachedTile.Value == tile && _hasPrediction.Value && !_fromGarbageDayChest.Value)
    {
      _alreadyChecked.Value = Game1.netWorldState.Value.CheckedGarbage.Contains(id);
      return;
    }

    GarbageCanPredictor.Predict(
      location,
      id,
      tile,
      Game1.player,
      out List<Item> items,
      out bool alreadyChecked,
      out int? lockedMinLevel,
      out _,
      out bool fromGarbageDayChest
    );

    _predictedItems.Value = items;
    _alreadyChecked.Value = alreadyChecked;
    _lockedMinLevel.Value = lockedMinLevel;
    _fromGarbageDayChest.Value = fromGarbageDayChest;
    _hasPrediction.Value = true;
    _cachedTile.Value = tile;
  }

  internal static bool TryResolveGarbageCanId(GameLocation location, Vector2 tile, out string id)
  {
    id = string.Empty;

    // Buildings-layer tile action (vanilla + BinningSkill)
    string? action = location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Action", "Buildings");
    if (action != null && action.StartsWith(ActionPrefix, StringComparison.Ordinal))
    {
      string raw = action.Substring(ActionPrefix.Length).Trim();
      if (string.IsNullOrEmpty(raw))
      {
        return false;
      }

      id = raw switch
      {
        "0" => "JodiAndKent",
        "1" => "EmilyAndHaley",
        "2" => "Mayor",
        "3" => "Museum",
        "4" => "Blacksmith",
        "5" => "Saloon",
        "6" => "Evelyn",
        "7" => "JojaMart",
        _ => raw,
      };
      return true;
    }

    // GarbageDay mod replaces tile Action with a Chest; id is derived from GlobalInventoryId prefix
    if (GarbageDayHelper.TryGetGarbageCan(location, tile, out string gdId, out _))
    {
      id = gdId;
      return true;
    }

    return false;
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally() || Game1.activeClickableMenu != null)
    {
      return;
    }

    Vector2? tile = _hoveredTile.Value;
    if (tile == null || !_hasPrediction.Value)
    {
      return;
    }

    DrawTooltip(
      Game1.spriteBatch,
      _predictedItems.Value,
      tile.Value,
      _alreadyChecked.Value && !_fromGarbageDayChest.Value,
      _lockedMinLevel.Value
    );
  }

  private static void DrawTooltip(
    SpriteBatch b,
    List<Item> items,
    Vector2 hoverTile,
    bool alreadyChecked,
    int? lockedMinLevel
  )
  {
    const int spriteSize = 32;
    const int spritePadding = 4;
    SpriteFont font = Game1.smallFont;

    bool locked = lockedMinLevel.HasValue;
    int shownCount = locked ? 0 : Math.Min(items.Count, MaxItemsShown);
    bool truncated = !locked && items.Count > shownCount;
    bool emptyState = locked || shownCount == 0;

    string[] itemTexts = new string[shownCount];
    for (int i = 0; i < shownCount; i++)
    {
      Item it = items[i];
      itemTexts[i] = it.Stack > 1 ? $"{it.DisplayName} x{it.Stack}" : it.DisplayName;
    }

    string emptyText = locked
      ? I18n.GarbageCan_LockedBinning(level: lockedMinLevel!.Value)
      : I18n.GarbageCan_NothingToday();
    string truncatedText = truncated ? $"+{items.Count - shownCount} ..." : string.Empty;

    int lineHeight = Math.Max(font.LineSpacing, spriteSize);

    int maxTextWidth = 0;
    if (emptyState)
    {
      maxTextWidth = Math.Max(maxTextWidth, (int)font.MeasureString(emptyText).X);
    }
    else
    {
      for (int i = 0; i < shownCount; i++)
      {
        maxTextWidth = Math.Max(maxTextWidth, (int)font.MeasureString(itemTexts[i]).X);
      }
    }
    if (truncated)
    {
      maxTextWidth = Math.Max(maxTextWidth, (int)font.MeasureString(truncatedText).X);
    }

    int contentWidth = emptyState ? maxTextWidth : spriteSize + spritePadding + maxTextWidth;
    int width = contentWidth + 32 + 4;

    int rowCount = emptyState ? 1 : shownCount;
    int height = (lineHeight * rowCount) + (truncated ? font.LineSpacing : 0) + 40;

    int overrideX = -1;
    int overrideY = -1;
    if (Game1.options.gamepadControls && Game1.timerUntilMouseFade <= 0)
    {
      Vector2 tilePx = Utility.ModifyCoordinatesForUIScale(
        Game1.GlobalToLocal(hoverTile * Game1.tileSize)
      );
      overrideX = (int)(tilePx.X + Utility.ModifyCoordinateForUIScale(32));
      overrideY = (int)(tilePx.Y + Utility.ModifyCoordinateForUIScale(32));
    }

    int x = overrideX != -1 ? overrideX : Game1.getOldMouseX() + 32;
    int y = overrideY != -1 ? overrideY : Game1.getOldMouseY() + 32;

    Rectangle safeArea = Utility.getSafeArea();
    if (x + width > safeArea.Right)
    {
      x = safeArea.Right - width;
      y += 16;
    }

    if (y + height > safeArea.Bottom)
    {
      x += 16;
      if (x + width > safeArea.Right)
      {
        x = safeArea.Right - width;
      }

      y = safeArea.Bottom - height;
    }

    IClickableMenu.drawTextureBox(
      b,
      Game1.menuTexture,
      new Rectangle(0, 256, 60, 60),
      x,
      y,
      width,
      height,
      Color.White
    );

    bool greyed = alreadyChecked || locked;
    Color textColor = greyed ? Game1.textColor * 0.5f : Game1.textColor;
    Color shadowColor = Game1.textShadowColor;
    float itemAlpha = greyed ? 0.5f : 1f;

    float rowY = y + 16 + 4;

    if (emptyState)
    {
      float textY = rowY + lineHeight / 2f - font.LineSpacing / 2f;
      Tools.DrawShadowedText(
        b,
        font,
        emptyText,
        new Vector2(x + 16, textY),
        textColor,
        shadowColor
      );
      if (alreadyChecked)
      {
        int strikeRight = (int)(x + 16 + font.MeasureString(emptyText).X);
        int strikeY = (int)(rowY + lineHeight / 2f) - 1;
        b.Draw(
          Game1.staminaRect,
          new Rectangle(x + 16, strikeY, strikeRight - (x + 16), 2),
          textColor
        );
      }
      return;
    }

    for (int i = 0; i < shownCount; i++)
    {
      Item it = items[i];
      float textY = rowY + lineHeight / 2f - font.LineSpacing / 2f;
      float textX = x + 16;

      ParsedItemData? itemData = ItemRegistry.GetData(it.QualifiedItemId);
      if (itemData != null)
      {
        Texture2D texture = itemData.GetTexture();
        Rectangle sourceRect = itemData.GetSourceRect();
        float scale = spriteSize / (float)Math.Max(sourceRect.Width, sourceRect.Height);
        float spriteCenterY = rowY + lineHeight / 2f - (sourceRect.Height * scale) / 2f;
        b.Draw(
          texture,
          new Vector2(x + 16, spriteCenterY),
          sourceRect,
          Color.White * itemAlpha,
          0f,
          Vector2.Zero,
          scale,
          SpriteEffects.None,
          0.9f
        );
      }

      textX += spriteSize + spritePadding;

      Tools.DrawShadowedText(
        b,
        font,
        itemTexts[i],
        new Vector2(textX, textY),
        textColor,
        shadowColor
      );

      if (alreadyChecked)
      {
        int strikeX = x + 16;
        int strikeRight = (int)(textX + font.MeasureString(itemTexts[i]).X);
        int strikeY = (int)(rowY + lineHeight / 2f) - 1;
        b.Draw(
          Game1.staminaRect,
          new Rectangle(strikeX, strikeY, strikeRight - strikeX, 2),
          textColor
        );
      }

      rowY += lineHeight;
    }

    if (truncated)
    {
      Tools.DrawShadowedText(
        b,
        font,
        truncatedText,
        new Vector2(x + 16, rowY),
        Game1.textColor * 0.5f,
        shadowColor
      );
    }
  }
}
