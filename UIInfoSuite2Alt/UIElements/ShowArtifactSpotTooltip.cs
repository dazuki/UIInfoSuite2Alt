using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Infrastructure.Helpers;
using Object = StardewValley.Object;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowArtifactSpotTooltip : IDisposable
{
  private readonly IModHelper _helper;
  private readonly PerScreen<Object?> _hoveredSpot = new();
  private readonly PerScreen<List<PredictedDrop>?> _predictedItems = new();

  // Cache predictions per tile to avoid re-running the predictor every update tick.
  private readonly PerScreen<Vector2?> _cachedTile = new();

  // FTM (Farm Type Manager) places BuriedItems subclass of Object with custom contents.
  // Detected by type name to avoid hard dependency.
  private const string FtmBuriedItemsTypeName = "FarmTypeManager.ModEntry+BuriedItems";
  private readonly bool _hasFtm;

  // Archaeology Skill mod: Antiquarian profession guarantees a bonus artifact on every dig.
  // We show the profession icon in the tooltip corner when the player has it.
  private const string ArchaeologySkillIconAsset =
    "Mods/moonslime.ArchaeologySkill/interface/ArchaeologyiconBalt";
  private readonly int? _antiquarianProfessionId;

  public ShowArtifactSpotTooltip(IModHelper helper)
  {
    _helper = helper;
    _hasFtm = helper.ModRegistry.IsLoaded(ModCompat.FarmTypeManager);

    if (
      helper.ModRegistry.IsLoaded(ModCompat.ArchaeologySkill)
      && ApiManager.GetApi<ISpaceCoreApi>(ModCompat.SpaceCore, out ISpaceCoreApi? spaceCoreApi)
    )
    {
      try
      {
        _antiquarianProfessionId = spaceCoreApi.GetProfessionId(
          "moonslime.Archaeology",
          "Archaeology10a1"
        );
      }
      catch (Exception ex)
      {
        ModEntry.MonitorObject.Log(
          $"ShowArtifactSpotTooltip: failed to resolve Antiquarian profession, {ex.Message}",
          LogLevel.Warn
        );
      }
    }
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
    // Invalidate cache on new day since seeds change with DaysPlayed
    _cachedTile.Value = null;
    _predictedItems.Value = null;
  }

  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (!e.IsMultipleOf(4))
    {
      return;
    }

    _hoveredSpot.Value = null;

    if (Game1.currentLocation == null)
    {
      _cachedTile.Value = null;
      _predictedItems.Value = null;
      return;
    }

    Vector2 gamepadTile =
      Game1.player.CurrentTool != null
        ? Utility.snapToInt(Game1.player.GetToolLocation() / Game1.tileSize)
        : Utility.snapToInt(Game1.player.GetGrabTile());
    Vector2 mouseTile = Game1.currentCursorTile;
    Vector2 tile =
      Game1.options.gamepadControls && Game1.timerUntilMouseFade <= 0 ? gamepadTile : mouseTile;

    if (!Game1.currentLocation.Objects.TryGetValue(tile, out Object? obj))
    {
      _cachedTile.Value = null;
      _predictedItems.Value = null;
      return;
    }

    if (obj.QualifiedItemId != "(O)590" && obj.QualifiedItemId != "(O)SeedSpot")
    {
      _cachedTile.Value = null;
      _predictedItems.Value = null;
      return;
    }

    _hoveredSpot.Value = obj;

    // Use cached prediction if still hovering the same tile
    if (_cachedTile.Value == tile && _predictedItems.Value != null)
    {
      return;
    }

    int tileX = (int)tile.X;
    int tileY = (int)tile.Y;

    // FTM BuriedItems: read custom items directly instead of predicting vanilla drops
    List<PredictedDrop>? ftmItems = TryGetFtmBuriedItems(obj);

    // SpaceCore till spots: store their drop in modData instead of the vanilla drop table
    List<PredictedDrop>? scItems = TryGetSpaceCoreTillDrop(obj);

    if (ftmItems != null)
    {
      _predictedItems.Value = ftmItems;
    }
    else if (scItems != null)
    {
      _predictedItems.Value = scItems;
    }
    else
    {
      _predictedItems.Value =
        obj.QualifiedItemId == "(O)SeedSpot"
          ? ArtifactSpotPredictor.PredictSeedSpotDrop(Game1.player, tileX, tileY)
          : ArtifactSpotPredictor.PredictArtifactSpotDrop(
            Game1.currentLocation,
            tileX,
            tileY,
            Game1.player
          );
    }

    _cachedTile.Value = tile;
  }

  /// <summary>
  /// If the object is an FTM BuriedItems instance, reads its Items field directly.
  /// Returns null if not an FTM buried item.
  /// </summary>
  private List<PredictedDrop>? TryGetFtmBuriedItems(Object obj)
  {
    if (!_hasFtm || obj.GetType().FullName != FtmBuriedItemsTypeName)
    {
      return null;
    }

    try
    {
      var items = _helper.Reflection.GetField<IList<Item>>(obj, "Items").GetValue();
      return items?.Count > 0 ? items.Select(i => new PredictedDrop(i)).ToList() : null;
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.LogOnce(
        $"ShowArtifactSpotTooltip: failed to read FTM buried items via reflection; skipping prediction, {ex.Message}",
        LogLevel.Trace
      );
      return null;
    }
  }

  // SpaceCore stores the drop in modData["spacechase0.SpaceCore/TillDropOverride"] when placing till spots.
  private static List<PredictedDrop>? TryGetSpaceCoreTillDrop(Object obj)
  {
    if (!obj.modData.TryGetValue("spacechase0.SpaceCore/TillDropOverride", out string? itemId))
    {
      return null;
    }

    Item? item = ItemRegistry.Create(itemId, allowNull: true);
    return item != null ? [new PredictedDrop(item)] : null;
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally() || Game1.activeClickableMenu != null)
    {
      return;
    }

    List<PredictedDrop>? drops = _predictedItems.Value;
    Object? spot = _hoveredSpot.Value;
    if (drops == null || drops.Count == 0 || spot == null)
    {
      return;
    }

    bool showAntiquarian =
      _antiquarianProfessionId.HasValue
      && spot.QualifiedItemId == "(O)590"
      && Game1.player.professions.Contains(_antiquarianProfessionId.Value);

    DrawDropListTooltip(Game1.spriteBatch, drops, spot.TileLocation, showAntiquarian);
  }

  private void DrawDropListTooltip(
    SpriteBatch b,
    List<PredictedDrop> drops,
    Vector2 spotTile,
    bool showAntiquarian
  )
  {
    const int spriteSize = 32;
    const int spritePadding = 4;
    SpriteFont font = Game1.smallFont;

    // Measure lines
    float maxWidth = 0;
    foreach (PredictedDrop drop in drops)
    {
      float lineWidth = spriteSize + spritePadding + MeasureDropLine(drop, font, spriteSize);
      maxWidth = Math.Max(maxWidth, lineWidth);
    }

    int width = (int)maxWidth + 32;
    int height = Math.Max(66, drops.Count * Math.Max(font.LineSpacing, spriteSize) + 32);

    // Position
    int overrideX = -1;
    int overrideY = -1;
    if (Game1.options.gamepadControls && Game1.timerUntilMouseFade <= 0)
    {
      Vector2 tile = Utility.ModifyCoordinatesForUIScale(
        Game1.GlobalToLocal(spotTile * Game1.tileSize)
      );
      overrideX = (int)(tile.X + Utility.ModifyCoordinateForUIScale(32));
      overrideY = (int)(tile.Y + Utility.ModifyCoordinateForUIScale(32));
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

    width += 4;

    // Background box
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

    // Antiquarian profession icon in top-right corner
    if (showAntiquarian)
    {
      try
      {
        Texture2D antiquarianIcon = _helper.GameContent.Load<Texture2D>(ArchaeologySkillIconAsset);
        const float iconScale = 3f;
        float iconSize = Math.Max(antiquarianIcon.Width, antiquarianIcon.Height) * iconScale;
        Vector2 iconPos = new(x + width - iconSize + 4, y - 8);

        // Shadow: 1px left, 1px down
        b.Draw(
          antiquarianIcon,
          iconPos + new Vector2(-1f, 1f) * iconScale,
          null,
          Color.Black * 0.35f,
          0f,
          Vector2.Zero,
          iconScale,
          SpriteEffects.None,
          0.9f
        );

        b.Draw(
          antiquarianIcon,
          iconPos,
          null,
          Color.White,
          0f,
          Vector2.Zero,
          iconScale,
          SpriteEffects.None,
          0.91f
        );
      }
      catch
      {
        // Asset not available
      }
    }

    // Draw each drop
    Color textColor = Game1.textColor;
    Color shadowColor = Game1.textShadowColor;
    Color noteChanceColor = Tools.TooltipYellow;
    int lineHeight = Math.Max(font.LineSpacing, spriteSize);
    float lineY = y + 16 + 4;

    foreach (PredictedDrop drop in drops)
    {
      ParsedItemData? itemData = ItemRegistry.GetData(drop.Item.QualifiedItemId);
      if (itemData != null)
      {
        Texture2D texture = itemData.GetTexture();
        Rectangle sourceRect = itemData.GetSourceRect();
        float scale = spriteSize / (float)Math.Max(sourceRect.Width, sourceRect.Height);
        float spriteCenterY = lineY + lineHeight / 2f - (sourceRect.Height * scale) / 2f - 2f;
        b.Draw(
          texture,
          new Vector2(x + 16, spriteCenterY),
          sourceRect,
          Color.White,
          0f,
          Vector2.Zero,
          scale,
          SpriteEffects.None,
          0.9f
        );
      }

      float textY = lineY + lineHeight / 2f - font.LineSpacing / 2f;
      float textX = x + 16 + spriteSize + spritePadding;

      if (drop.SecretNoteChance > 0f && drop.SecretNoteItemId != null)
      {
        // Format: "Stone / [icon] Secret Note (47%)"
        string itemName =
          drop.Item.Stack > 1
            ? $"{drop.Item.DisplayName} x{drop.Item.Stack}"
            : drop.Item.DisplayName;
        string separator = " / ";
        string noteName = drop.SecretNoteDisplayName + " ";
        string noteChance = $"({drop.SecretNoteChance * 100:0}%)";

        // Draw item name with shadow
        Vector2 pos = new(textX, textY);
        Tools.DrawShadowedText(b, font, itemName, pos, textColor, shadowColor);

        // Draw separator
        float cursorX = textX + font.MeasureString(itemName).X;
        Tools.DrawShadowedText(
          b,
          font,
          separator,
          new Vector2(cursorX, textY),
          textColor,
          shadowColor
        );
        cursorX += font.MeasureString(separator).X;

        // Draw note icon inline
        ParsedItemData? noteData = ItemRegistry.GetData(drop.SecretNoteItemId);
        if (noteData != null)
        {
          Texture2D noteTex = noteData.GetTexture();
          Rectangle noteSrc = noteData.GetSourceRect();
          float noteScale = spriteSize / (float)Math.Max(noteSrc.Width, noteSrc.Height);
          float noteIconY = lineY + lineHeight / 2f - (noteSrc.Height * noteScale) / 2f - 2f;
          b.Draw(
            noteTex,
            new Vector2(cursorX, noteIconY),
            noteSrc,
            Color.White,
            0f,
            Vector2.Zero,
            noteScale,
            SpriteEffects.None,
            0.9f
          );
          cursorX += spriteSize + spritePadding;
        }

        // Draw note name in normal color, percentage in yellow
        Tools.DrawShadowedText(
          b,
          font,
          noteName,
          new Vector2(cursorX, textY),
          textColor,
          shadowColor
        );
        cursorX += font.MeasureString(noteName).X;
        Tools.DrawShadowedText(
          b,
          font,
          noteChance,
          new Vector2(cursorX, textY),
          noteChanceColor,
          shadowColor
        );
      }
      else
      {
        string text =
          drop.Item.Stack > 1
            ? $"{drop.Item.DisplayName} x{drop.Item.Stack}"
            : drop.Item.DisplayName;
        Vector2 pos = new(textX, textY);
        Tools.DrawShadowedText(b, font, text, pos, textColor, shadowColor);
      }

      lineY += lineHeight;
    }
  }

  private static float MeasureDropLine(PredictedDrop drop, SpriteFont font, int iconSize)
  {
    string itemName =
      drop.Item.Stack > 1 ? $"{drop.Item.DisplayName} x{drop.Item.Stack}" : drop.Item.DisplayName;

    if (drop.SecretNoteChance > 0f && drop.SecretNoteItemId != null)
    {
      string separator = " / ";
      string noteName = drop.SecretNoteDisplayName + " ";
      string noteChance = $"({drop.SecretNoteChance * 100:0}%)";
      return font.MeasureString(itemName).X
        + font.MeasureString(separator).X
        + iconSize
        + 4 // spritePadding
        + font.MeasureString(noteName).X
        + font.MeasureString(noteChance).X;
    }

    return font.MeasureString(itemName).X;
  }
}
