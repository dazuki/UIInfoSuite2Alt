using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.GameData.Crops;
using StardewValley.GameData.FruitTrees;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Compatibility.Helpers;
using SObject = StardewValley.Object;

namespace UIInfoSuite2Alt.Infrastructure;

public static class Tools
{
  #region Tooltip Colors
  public static readonly Color TooltipGreen = new(45, 100, 5);
  public static readonly Color TooltipYellow = new(110, 70, 25);
  public static readonly Color TooltipBlue = new(25, 85, 145);
  public static readonly Color TooltipRed = new(165, 25, 25);
  public static readonly Color TooltipWalnutYellow = new(128, 106, 0);
  public static readonly Color TooltipWalnutRed = new(128, 21, 0);
  public static readonly Color TooltipWalnutGreen = new(25, 77, 0);
  public static readonly Color TooltipBuffYellow = new(226, 150, 25);
  public static readonly Color TooltipBuffBlue = new(25, 186, 227);
  #endregion

  public static int GetWidthInPlayArea()
  {
    if (Game1.isOutdoorMapSmallerThanViewport())
    {
      int right = Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea.Right;
      int totalWidth = Game1.currentLocation.map.Layers[0].LayerWidth * Game1.tileSize;
      int viewportPadding = Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea.Right - totalWidth;

      return right - viewportPadding / 2;
    }

    return Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea.Right;
  }

  public static int GetSellToStorePrice(Item item)
  {
    if (item is SObject obj)
    {
      return obj.sellToStorePrice();
    }

    return item.salePrice() / 2;
  }

  public static SObject? GetHarvest(Item item)
  {
    if (
      item is not SObject { Category: SObject.SeedsCategory } seedsObject
      || seedsObject.ItemId == Crop.mixedSeedsId
    )
    {
      return null;
    }

    if (
      seedsObject.IsFruitTreeSapling()
      && FruitTree.TryGetData(item.ItemId, out FruitTreeData? fruitTreeData)
    )
    {
      if (fruitTreeData.Fruit is not { Count: > 0 })
      {
        ModEntry.MonitorObject.LogOnce(
          $"Tools: fruit tree '{item.ItemId}' has no fruit entries",
          LogLevel.Warn
        );
        return null;
      }

      // ItemId can be null when the entry uses an item query (RandomItemId, etc.);
      // pick the first literal ItemId we find.
      string? fruitId = fruitTreeData
        .Fruit.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.ItemId))
        ?.ItemId;
      if (string.IsNullOrWhiteSpace(fruitId))
      {
        ModEntry.MonitorObject.LogOnce(
          $"Tools: fruit tree '{item.ItemId}' has no literal fruit ItemId (uses item query?); skipping harvest price",
          LogLevel.Trace
        );
        return null;
      }

      // TODO support multiple items returned
      return TryCreateHarvest(fruitId, item.ItemId);
    }

    if (Crop.TryGetData(item.ItemId, out CropData cropData))
    {
      if (string.IsNullOrWhiteSpace(cropData.HarvestItemId))
      {
        ModEntry.MonitorObject.LogOnce(
          $"Tools: crop '{item.ItemId}' has no literal HarvestItemId (uses item query?); skipping harvest price",
          LogLevel.Trace
        );
        return null;
      }

      return TryCreateHarvest(cropData.HarvestItemId, item.ItemId);
    }

    // Custom Bush saplings and vanilla tea sapling
    if (
      ApiManager.GetApi(ModCompat.CustomBush, out ICustomBushApi? customBushApi)
      && customBushApi.TryGetDrops(item.QualifiedItemId, out IList<ICustomBushDrop>? drops)
      && drops.Count > 0
    )
    {
      string? dropId = drops.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.ItemId))?.ItemId;
      if (string.IsNullOrWhiteSpace(dropId))
      {
        ModEntry.MonitorObject.LogOnce(
          $"Tools: custom bush '{item.QualifiedItemId}' has no literal drop ItemId; skipping harvest price",
          LogLevel.Trace
        );
        return null;
      }

      return TryCreateHarvest(dropId, item.QualifiedItemId);
    }

    // Vanilla tea sapling fallback (no Custom Bush mod)
    if (item.QualifiedItemId == "(O)251")
    {
      return TryCreateHarvest("(O)614", item.QualifiedItemId);
    }

    return null;
  }

  // Wraps ItemRegistry.Create so a third-party Harmony patch on Object..ctor (e.g. ItemExtensions,
  // AlternativeTextures) can't spam the tooltip event with exceptions every frame.
  private static SObject? TryCreateHarvest(string harvestItemId, string seedItemId)
  {
    try
    {
      return ItemRegistry.Create<SObject>(harvestItemId, allowNull: true);
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.LogOnce(
        $"Tools: failed to create harvest item '{harvestItemId}' for seed '{seedItemId}'; skipping harvest price, {ex.Message}",
        LogLevel.Warn
      );
      return null;
    }
  }

  public static int GetHarvestPrice(Item item)
  {
    return GetHarvest(item)?.sellToStorePrice() ?? 0;
  }

  public static void DrawMouseCursor()
  {
    if (!Game1.options.hardwareCursor)
    {
      int mouseCursorToRender = Game1.options.gamepadControls
        ? Game1.mouseCursor + 44
        : Game1.mouseCursor;
      Rectangle what = Game1.getSourceRectForStandardTileSheet(
        Game1.mouseCursors,
        mouseCursorToRender,
        16,
        16
      );

      Game1.spriteBatch.Draw(
        Game1.mouseCursors,
        new Vector2(Game1.getMouseX(), Game1.getMouseY()),
        what,
        Color.White,
        0.0f,
        Vector2.Zero,
        Game1.pixelZoom + Game1.dialogueButtonScale / 150.0f,
        SpriteEffects.None,
        1f
      );
    }
  }

  /// <summary>Estimates the vanilla tooltip box bounds, replicating logic from IClickableMenu.drawHoverText.</summary>
  public static Rectangle EstimateVanillaTooltipBounds(Item item, bool informantSellPrice = false)
  {
    SpriteFont descFont = Game1.smallFont;
    SpriteFont titleFont = Game1.dialogueFont;

    string description = item.getDescription();
    string title = item.DisplayName;
    string category = item is SObject obj ? obj.getCategoryName() : "";

    int width =
      Math.Max((int)descFont.MeasureString(description).X, (int)titleFont.MeasureString(title).X)
      + 32;
    int height =
      (int)descFont.MeasureString(description).Y + 32 + (int)titleFont.MeasureString(title).Y + 16;

    if (item is FishingRod)
    {
      int slots = item.attachmentSlots();
      if (slots == 1)
      {
        height += 68;
      }
      else if (slots > 1)
      {
        height += 144;
      }
    }
    else
    {
      height += 68 * item.attachmentSlots();
    }

    if (category.Length > 0)
    {
      width = Math.Max(width, (int)descFont.MeasureString(category).X + 32);
      height += (int)descFont.MeasureString("T").Y;
    }

    // Delegate to the item's own extra-space calculation so ammo/buff/stat rows are accounted for.
    try
    {
      var descBuilder = new StringBuilder(description);
      Point extra = item.getExtraSpaceNeededForTooltipSpecialIcons(
        descFont,
        width,
        92,
        height,
        descBuilder,
        title,
        -1
      );
      if (extra.X != 0)
      {
        width = extra.X;
      }
      if (extra.Y != 0)
      {
        height = extra.Y;
      }
    }
    catch
    {
      // Modded items can throw here; fall back to the base estimate.
    }

    if (item is SObject edible && edible.Edibility is not (-300 or 0) && item is not MeleeWeapon)
    {
      int staminaRecovery = edible.staminaRecoveredOnConsumption();
      int healthRecovery = edible.healthRecoveredOnConsumption();
      height += 40 * ((staminaRecovery > 0 && healthRecovery > 0) ? 2 : 1);
    }

    // Informant's sell-price injects moneyAmountToShowAtBottom: max(font height + 4, 44).
    if (informantSellPrice && item is SObject sellable)
    {
      int sellPrice = Utility.getSellToStorePriceOfItem(sellable, false);
      if (sellPrice >= 0 || sellable.canBeShipped())
      {
        height += (int)Math.Max(descFont.MeasureString(sellPrice.ToString()).Y + 4f, 44f);
      }
    }

    height = Math.Max(height, 60);
    width += 4;

    int x = Game1.getOldMouseX() + 32;
    int y = Game1.getOldMouseY() + 32;

    // Vanilla drawToolTip shifts by +40 on both axes while holding an item, to clear the dragged sprite.
    if (IsHoldingItemOnCursor())
    {
      x += 40;
      y += 40;
    }

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

    return new Rectangle(x, y, width, height);
  }

  public static bool IsHoldingItemOnCursor()
  {
    if (Game1.player.CursorSlotItem != null)
    {
      return true;
    }

    if (Game1.activeClickableMenu is MenuWithInventory mwi && mwi.heldItem != null)
    {
      return true;
    }

    return false;
  }

  public static Item? GetHoveredItem()
  {
    Item? hoverItem = null;

    if (Game1.activeClickableMenu == null && Game1.onScreenMenus != null)
    {
      hoverItem = Game1
        .onScreenMenus.OfType<Toolbar>()
        .Select(tb => tb.hoverItem)
        .FirstOrDefault(hi => hi is not null);
    }

    if (GameMenuHelper.GetCurrentPage(Game1.activeClickableMenu) is InventoryPage inventory)
    {
      hoverItem = inventory.hoveredItem;
    }

    if (Game1.activeClickableMenu is ItemGrabMenu itemMenu)
    {
      hoverItem = itemMenu.hoveredItem;
    }

    return hoverItem;
  }

  public static void SetSubTexture(
    Color[] sourceColors,
    Color[] destColors,
    int destWidth,
    Rectangle destBounds,
    bool overlay = false
  )
  {
    if (
      sourceColors.Length > destColors.Length
      || destBounds.Width * destBounds.Height > destColors.Length
    )
    {
      return;
    }

    var emptyColor = Color.Transparent;
    var srcIdx = 0;
    for (var yOffset = 0; yOffset < destBounds.Height; yOffset++)
    {
      for (var xOffset = 0; xOffset < destBounds.Width; xOffset++)
      {
        int idx = destBounds.X + xOffset + destWidth * (yOffset + destBounds.Y);
        Color sourcePixel = sourceColors[srcIdx++];

        // Skip transparent pixels in overlay mode
        if (overlay && emptyColor.Equals(sourcePixel))
        {
          continue;
        }

        destColors[idx] = sourcePixel;
      }
    }
  }

  /// <summary>Extract a source rectangle from a spritesheet into a new standalone texture.</summary>
  public static Texture2D CropTexture(Texture2D source, Rectangle sourceRect)
  {
    var data = new Color[sourceRect.Width * sourceRect.Height];
    source.GetData(0, sourceRect, data, 0, data.Length);

    var cropped = new Texture2D(Game1.graphics.GraphicsDevice, sourceRect.Width, sourceRect.Height);
    cropped.SetData(data);
    return cropped;
  }

  public static Texture2D RecolorTexture(Texture2D source, Color color)
  {
    var pixels = new Color[source.Width * source.Height];
    source.GetData(pixels);
    for (int i = 0; i < pixels.Length; i++)
    {
      if (pixels[i].A > 0)
        pixels[i] = new Color(color.R, color.G, color.B, pixels[i].A);
    }
    var result = new Texture2D(Game1.graphics.GraphicsDevice, source.Width, source.Height);
    result.SetData(pixels);
    return result;
  }

  private static IEnumerable<int> GetDaysFromCondition(
    GameStateQuery.ParsedGameStateQuery parsedGameStateQuery
  )
  {
    HashSet<int> days = new();
    if (parsedGameStateQuery.Query.Length < 2)
    {
      return days;
    }

    string queryStr = parsedGameStateQuery.Query[0];
    if ("day_of_month".Equals(queryStr, StringComparison.OrdinalIgnoreCase))
    {
      for (var i = 1; i < parsedGameStateQuery.Query.Length; i++)
      {
        string dayStr = parsedGameStateQuery.Query[i];
        if ("even".Equals(dayStr, StringComparison.OrdinalIgnoreCase))
        {
          days.AddRange(Enumerable.Range(1, 28).Where(x => x % 2 == 0));
        }
        else if ("odd".Equals(dayStr, StringComparison.OrdinalIgnoreCase))
        {
          days.AddRange(Enumerable.Range(1, 28).Where(x => x % 2 != 0));
        }
        else if (int.TryParse(dayStr, out int parsedInt))
        {
          days.Add(parsedInt);
        }
      }
    }
    else if ("day_of_week".Equals(queryStr, StringComparison.OrdinalIgnoreCase))
    {
      // Convert day-of-week to matching days in 28-day month (dayOfMonth % 7 = DayOfWeek)
      for (var i = 1; i < parsedGameStateQuery.Query.Length; i++)
      {
        if (WorldDate.TryGetDayOfWeekFor(parsedGameStateQuery.Query[i], out DayOfWeek dayOfWeek))
        {
          int dow = (int)dayOfWeek;
          for (int day = 1; day <= 28; day++)
          {
            if (day % 7 == dow)
            {
              days.Add(day);
            }
          }
        }
      }
    }
    else
    {
      return days;
    }

    return parsedGameStateQuery.Negated
      ? Enumerable.Range(1, 28).Where(x => !days.Contains(x)).ToHashSet()
      : days;
  }

  public static int? GetNextDayFromCondition(string? condition, bool includeToday = true)
  {
    HashSet<int> days = new();
    if (condition == null)
    {
      return null;
    }

    GameStateQuery.ParsedGameStateQuery[]? conditionEntries = GameStateQuery.Parse(condition);

    foreach (GameStateQuery.ParsedGameStateQuery parsedGameStateQuery in conditionEntries)
    {
      days.AddRange(GetDaysFromCondition(parsedGameStateQuery));
    }

    days.RemoveWhere(day => day < Game1.dayOfMonth || (!includeToday && day == Game1.dayOfMonth));

    return days.Count == 0 ? null : days.Min();
  }

  public static int? GetLastDayFromCondition(string? condition)
  {
    HashSet<int> days = new();
    if (condition == null)
    {
      return null;
    }

    GameStateQuery.ParsedGameStateQuery[]? conditionEntries = GameStateQuery.Parse(condition);

    foreach (GameStateQuery.ParsedGameStateQuery parsedGameStateQuery in conditionEntries)
    {
      days.AddRange(GetDaysFromCondition(parsedGameStateQuery));
    }

    return days.Count == 0 ? null : days.Max();
  }

  #region Exclamation Drawing
  /// <summary>Origin that centres the exclamation sprite on its draw position.</summary>
  public static readonly Vector2 ExclamationOrigin = new(2.5f, 7f);

  private static readonly Rectangle ExclamationSource = new(403, 496, 5, 14);

  private const int PulseDurationMs = 1000;
  private const int PulseCycleMs = 4000;

  /// <summary>Draw the exclamation mark sprite. Origin defaults to the sprite's top-left.</summary>
  public static void DrawExclamation(
    SpriteBatch b,
    Vector2 position,
    float scale = 1.6f,
    Vector2? origin = null,
    float layerDepth = 1f
  )
  {
    b.Draw(
      Game1.mouseCursors,
      position,
      ExclamationSource,
      Color.White,
      0f,
      origin ?? Vector2.Zero,
      scale,
      SpriteEffects.None,
      layerDepth
    );
  }

  /// <summary>Draw the exclamation mark pulsing on a shared clock: 1s pulse, 3s rest.</summary>
  public static void DrawPulsingExclamation(
    SpriteBatch b,
    Vector2 position,
    float baseScale = 1.6f,
    Vector2? origin = null,
    float layerDepth = 1f
  )
  {
    float scale = baseScale;
    Vector2 shake = Vector2.Zero;
    double phase = (Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0) % PulseCycleMs;

    if (phase < PulseDurationMs)
    {
      // Flat-topped curve: capped growth across the middle, back to base size at the edges.
      scale = baseScale / (Math.Max(300f, Math.Abs((float)phase - 500f)) / 500f);
      if (scale > baseScale)
      {
        shake = new Vector2(Game1.random.Next(-1, 2), Game1.random.Next(-1, 2));
      }
    }

    DrawExclamation(b, position + shake, scale, origin, layerDepth);
  }
  #endregion

  #region Text Drawing
  /// <summary>Draw text with a 3-direction shadow (diagonal, down, right) for readability on varied backgrounds.</summary>
  public static void DrawShadowedText(
    SpriteBatch batch,
    SpriteFont font,
    string text,
    Vector2 position,
    Color textColor,
    Color shadowColor
  )
  {
    batch.DrawString(font, text, position + new Vector2(2f, 2f), shadowColor);
    batch.DrawString(font, text, position + new Vector2(0f, 2f), shadowColor);
    batch.DrawString(font, text, position + new Vector2(2f, 0f), shadowColor);
    batch.DrawString(font, text, position, textColor * 0.9f);
  }
  #endregion

  #region Tiny Digit Drawing
  private const float TinyDigitScale = 2f;
  private const int TinyDigitWidth = 5;
  private const int TinyDigitHeight = 7;

  /// <summary>Draw a multi-digit number using the tiny digit sprites from Game1.mouseCursors.</summary>
  public static void DrawTinyDigits(
    SpriteBatch b,
    int number,
    Vector2 position,
    ref float xOffset,
    int step,
    Color digitColor,
    Color shadowColor
  )
  {
    if (number == 0)
    {
      DrawTinyDigit(b, 0, position, ref xOffset, step, digitColor, shadowColor);
      return;
    }

    int digitCount = 0;
    int temp = number;
    while (temp > 0)
    {
      digitCount++;
      temp /= 10;
    }

    int divisor = (int)Math.Pow(10, digitCount - 1);
    for (int i = 0; i < digitCount; i++)
    {
      int digit = number / divisor % 10;
      DrawTinyDigit(b, digit, position, ref xOffset, step, digitColor, shadowColor);
      divisor /= 10;
    }
  }

  /// <summary>Draw a single tiny digit sprite from Game1.mouseCursors.</summary>
  public static void DrawTinyDigit(
    SpriteBatch b,
    int digit,
    Vector2 position,
    ref float xOffset,
    int step,
    Color digitColor,
    Color shadowColor
  )
  {
    var sourceRect = new Rectangle(
      368 + digit * TinyDigitWidth,
      56,
      TinyDigitWidth,
      TinyDigitHeight
    );

    // Shadow
    b.Draw(
      Game1.mouseCursors,
      position + new Vector2(xOffset + 1, 1),
      sourceRect,
      shadowColor,
      0f,
      Vector2.Zero,
      TinyDigitScale,
      SpriteEffects.None,
      0.99f
    );

    // Digit
    b.Draw(
      Game1.mouseCursors,
      position + new Vector2(xOffset, 0f),
      sourceRect,
      digitColor,
      0f,
      Vector2.Zero,
      TinyDigitScale,
      SpriteEffects.None,
      1f
    );

    xOffset += step;
  }

  /// <summary>Default step size for tiny digit drawing.</summary>
  public static int TinyDigitStep => (int)(TinyDigitWidth * TinyDigitScale) - 1;

  /// <summary>Draw a colon (two vertically stacked dots) sized to match tiny digits.</summary>
  public static void DrawTinyColon(
    SpriteBatch b,
    Vector2 position,
    float xOffset,
    int colonDotGap,
    Color dotColor,
    Color shadowColor
  )
  {
    float dotSize = TinyDigitScale;
    float scaledHeight = TinyDigitHeight * TinyDigitScale;
    float dotX = position.X + xOffset + (colonDotGap - dotSize) / 2f;

    // Upper dot (~30% from top)
    var upperPos = new Vector2(dotX, position.Y + scaledHeight * 0.25f);
    // Lower dot (~65% from top)
    var lowerPos = new Vector2(dotX, position.Y + scaledHeight * 0.6f);

    // Shadow
    DrawTinyDot(b, upperPos + Vector2.One, dotSize, shadowColor, 0.99f);
    DrawTinyDot(b, lowerPos + Vector2.One, dotSize, shadowColor, 0.99f);

    // Dots
    DrawTinyDot(b, upperPos, dotSize, dotColor, 1f);
    DrawTinyDot(b, lowerPos, dotSize, dotColor, 1f);
  }

  /// <summary>Draw a small filled square (dot) at the given position.</summary>
  public static void DrawTinyDot(
    SpriteBatch b,
    Vector2 position,
    float size,
    Color color,
    float layerDepth
  )
  {
    b.Draw(
      Game1.staminaRect,
      new Rectangle((int)position.X, (int)position.Y, (int)size, (int)size),
      null,
      color,
      0f,
      Vector2.Zero,
      SpriteEffects.None,
      layerDepth
    );
  }
  #endregion
}
