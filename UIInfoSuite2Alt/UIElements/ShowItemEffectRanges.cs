using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Infrastructure;
using Object = StardewValley.Object;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowItemEffectRanges : IDisposable
{
  #region Properties
  private readonly PerScreen<List<Point>> _effectiveAreaCurrent = new(() => []);
  private readonly PerScreen<HashSet<Point>> _effectiveAreaOther = new(() => []);
  private readonly PerScreen<HashSet<Point>> _effectiveAreaIntersection = new(() => []);
  private readonly PerScreen<HashSet<Point>> _seenTiles = new(() => []);

  private static readonly Rectangle TileSpriteRect = new(194, 388, 16, 16);

  private const string MushroomLogQualifiedId = "(BC)MushroomLog";
  private const string MossySeedQualifiedId = "(O)MossySeed";
  private const string CherryBombQualifiedId = "(O)286";
  private const string BombQualifiedId = "(O)287";
  private const string MegaBombQualifiedId = "(O)288";

  private const int MinCustomRadius = 1;
  private const int MaxCustomRadius = 50;

  private readonly IModHelper _helper;
  private readonly Lazy<Texture2D> _wildTreeTexture;

  private Dictionary<string, ItemEffectRangeData> _customRanges = [];
  private bool _customRangesNeedReload = true;

  private int _junimoHutRadius = 8; // default radius
  private bool _showItemEffectRanges;
  private bool _showPlacedItemRanges = true;

  private bool ButtonControlShow { get; set; }
  private bool ShowRangeTooltip { get; set; } = true;
  private bool ShowBombRange { get; set; }

  private bool ButtonShowOneRange { get; set; }
  private bool ButtonShowAllRanges { get; set; }

  private readonly PerScreen<RangeTooltipInfo?> _rangeTooltipInfo = new(() => null);

  /// <summary>Whether the range tooltip is currently visible. Used by ShowTileTooltips to avoid overlapping tooltips.</summary>
  public bool IsRangeTooltipActive => ShowRangeTooltip && _rangeTooltipInfo.Value != null;

  private sealed class RangeTooltipInfo
  {
    public string ObjectName = "";
    public bool TrackOverlap;
    public int ObjectCount;
    public bool ShowingAll;
    public int OccupiedTiles;
    public int RawTotalTiles;
    public string? SubHeader;
    public string? WarningMessage;
    public Texture2D? SpriteTexture;
    public Rectangle? SpriteSourceRect;

    public void SetSpriteFromObject(Object obj)
    {
      var itemData = ItemRegistry.GetData(obj.QualifiedItemId);
      if (itemData != null)
      {
        SpriteTexture = itemData.GetTexture();
        SpriteSourceRect = itemData.GetSourceRect();
      }
    }
  }
  #endregion


  #region Lifecycle
  public ShowItemEffectRanges(IModHelper helper)
  {
    _helper = helper;
    _wildTreeTexture = new Lazy<Texture2D>(() =>
      AssetHelper.TryLoadTexture(_helper, "assets/wild_tree_tooltip.png")
    );
  }

  public void Dispose()
  {
    ToggleOption(false);
    ToggleShowBombRangeOption(false);
    ToggleButtonControlShowOption(false);
  }

  public void ToggleOption(bool showItemEffectRanges)
  {
    _showItemEffectRanges = showItemEffectRanges;
    UpdateEventSubscriptions();
  }

  public void ToggleShowPlacedItemRangesOption(bool showPlacedItemRanges)
  {
    _showPlacedItemRanges = showPlacedItemRanges;
  }

  public void ToggleButtonControlShowOption(bool buttonControlShow)
  {
    ButtonControlShow = buttonControlShow;

    _helper.Events.Input.ButtonsChanged -= OnButtonChanged;
    if (buttonControlShow)
    {
      _helper.Events.Input.ButtonsChanged += OnButtonChanged;
    }

    UpdateEventSubscriptions();
  }

  public void ToggleShowRangeTooltipOption(bool showRangeTooltip)
  {
    ShowRangeTooltip = showRangeTooltip;
  }

  public void ToggleShowBombRangeOption(bool showBombRange)
  {
    ShowBombRange = showBombRange;
    UpdateEventSubscriptions();
  }

  private void UpdateEventSubscriptions()
  {
    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.Display.RenderedHud -= OnRenderedHud;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
    _helper.Events.Content.AssetsInvalidated -= OnAssetsInvalidated;

    if (_showItemEffectRanges || ShowBombRange || ButtonControlShow)
    {
      _customRangesNeedReload = true;
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.Display.RenderedHud += OnRenderedHud;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
      _helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;
    }
  }
  #endregion


  #region Custom ranges
  private void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
  {
    foreach (IAssetName name in e.NamesWithoutLocale)
    {
      if (name.IsEquivalentTo(ModEntry.ItemEffectRangesAssetName))
      {
        _customRangesNeedReload = true;
        break;
      }
    }
  }

  private void ReloadCustomRanges()
  {
    _customRangesNeedReload = false;

    Dictionary<string, ItemEffectRangeData> data;
    try
    {
      data = _helper.GameContent.Load<Dictionary<string, ItemEffectRangeData>>(
        ModEntry.ItemEffectRangesAssetName
      );
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"ShowItemEffectRanges: failed to load item effect ranges asset, {ex.Message}",
        LogLevel.Error
      );
      _customRanges = [];
      return;
    }

    Dictionary<string, ItemEffectRangeData> valid = [];
    foreach ((string key, ItemEffectRangeData rangeData) in data)
    {
      if (rangeData.Radius < MinCustomRadius || rangeData.Radius > MaxCustomRadius)
      {
        ModEntry.MonitorObject.LogOnce(
          $"ShowItemEffectRanges: range '{key}' has an out-of-bounds radius, skipping, radius={rangeData.Radius}, allowed={MinCustomRadius}-{MaxCustomRadius}",
          LogLevel.Warn
        );
        continue;
      }

      // Keys without a type prefix can never match an item
      if (!key.StartsWith('('))
      {
        ModEntry.MonitorObject.LogOnce(
          $"ShowItemEffectRanges: range '{key}' is not a qualified item ID, skipping, expected a type prefix such as (BC) or (O)",
          LogLevel.Warn
        );
        continue;
      }

      if (!string.IsNullOrWhiteSpace(rangeData.Shape))
      {
        if (Enum.TryParse(rangeData.Shape, true, out ItemEffectRangeShape shape))
        {
          rangeData.ResolvedShape = shape;
        }
        else
        {
          ModEntry.MonitorObject.LogOnce(
            $"ShowItemEffectRanges: range '{key}' has an unknown shape, using Square, shape={rangeData.Shape}, allowed={string.Join("/", Enum.GetNames<ItemEffectRangeShape>())}",
            LogLevel.Warn
          );
        }
      }

      valid[key] = rangeData;
    }

    _customRanges = valid;

    if (_customRanges.Count > 0)
    {
      ModEntry.MonitorObject.Log(
        $"ShowItemEffectRanges: loaded custom item effect ranges, count={_customRanges.Count}, keys=[{string.Join(", ", _customRanges.Keys)}]",
        LogLevel.Trace
      );
    }
  }

  private bool TryGetCustomRange(Object obj, out ItemEffectRangeData rangeData)
  {
    return _customRanges.TryGetValue(obj.QualifiedItemId, out rangeData!);
  }

  private static int[][] GetCustomRangeMask(ItemEffectRangeData rangeData)
  {
    return rangeData.ResolvedShape switch
    {
      ItemEffectRangeShape.Circle => GetCircularMask(rangeData.Radius),
      ItemEffectRangeShape.Diamond => GetDiamondMask(rangeData.Radius),
      _ => GetCircularMask(100, maxDisplaySquareRadius: rangeData.Radius),
    };
  }

  /// <summary>Highlight one custom-range object and fill in its tooltip.</summary>
  private void AddCustomRange(
    Object obj,
    ItemEffectRangeData rangeData,
    Vector2 tile,
    bool isCurrent,
    bool showingAll
  )
  {
    int[][] mask = GetCustomRangeMask(rangeData);

    int tilesBeforeAdd = _effectiveAreaOther.Value.Count + _effectiveAreaCurrent.Value.Count;

    // Show-all puts everything in the "other" set so _seenTiles catches overlaps
    AddTilesToHighlightedArea(
      mask,
      isCurrent && !showingAll && rangeData.AffectsCrops,
      (int)tile.X,
      (int)tile.Y,
      skipNonTillable: rangeData.AffectsCrops
    );
    int addedTiles =
      _effectiveAreaOther.Value.Count + _effectiveAreaCurrent.Value.Count - tilesBeforeAdd;

    if (!isCurrent)
    {
      if (_rangeTooltipInfo.Value != null)
      {
        _rangeTooltipInfo.Value.ObjectCount++;
        _rangeTooltipInfo.Value.RawTotalTiles += addedTiles;
        if (!rangeData.AffectsCrops)
        {
          _rangeTooltipInfo.Value.OccupiedTiles++;
        }
      }

      return;
    }

    _rangeTooltipInfo.Value = new RangeTooltipInfo
    {
      ObjectName = obj.DisplayName,
      SubHeader = string.IsNullOrWhiteSpace(rangeData.EffectLabel) ? null : rangeData.EffectLabel,
      TrackOverlap = rangeData.AffectsCrops,
      ObjectCount = 1,
      ShowingAll = showingAll,
      // Crop filtering already drops the object's own tile, unfiltered ranges include it
      OccupiedTiles = rangeData.AffectsCrops ? 0 : 1,
      RawTotalTiles = rangeData.AffectsCrops ? addedTiles : CountTilesInArray(mask),
    };
    _rangeTooltipInfo.Value.SetSpriteFromObject(obj);
  }
  #endregion


  #region Event subscriptions
  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (!e.IsMultipleOf(4))
    {
      return;
    }

    // Guard against ticks during loading screen
    if (Game1.currentLocation is null)
    {
      return;
    }

    _effectiveAreaCurrent.Value.Clear();
    _effectiveAreaOther.Value.Clear();
    _effectiveAreaIntersection.Value.Clear();
    _seenTiles.Value.Clear();

    if (Game1.activeClickableMenu == null && UIElementUtils.IsRenderingNormally())
    {
      if (_customRangesNeedReload)
      {
        ReloadCustomRanges();
      }

      UpdateEffectiveArea();
      GetOverlapValue();

      // Overlap tracking off (e.g. wild trees): merge intersection in so all tiles render green
      if (
        _rangeTooltipInfo.Value is { TrackOverlap: false }
        && _effectiveAreaIntersection.Value.Count > 0
      )
      {
        _effectiveAreaOther.Value.UnionWith(_effectiveAreaIntersection.Value);
        _effectiveAreaIntersection.Value.Clear();
      }
      if (ButtonShowOneRange)
      {
        ButtonShowOneRange = false;
      }

      if (ButtonShowAllRanges)
      {
        ButtonShowAllRanges = false;
      }
    }
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    // Compute visible tile bounds to skip off-screen draw calls
    xTile.Dimensions.Rectangle viewport = Game1.viewport;
    int minTileX = viewport.X / Game1.tileSize - 1;
    int minTileY = viewport.Y / Game1.tileSize - 1;
    int maxTileX = (viewport.X + viewport.Width) / Game1.tileSize + 1;
    int maxTileY = (viewport.Y + viewport.Height) / Game1.tileSize + 1;

    DrawTileHighlights(
      e,
      _effectiveAreaOther.Value,
      Color.Lime * 0.8f,
      minTileX,
      minTileY,
      maxTileX,
      maxTileY
    );

    DrawTileHighlights(
      e,
      _effectiveAreaCurrent.Value,
      Color.Lime * 0.8f,
      minTileX,
      minTileY,
      maxTileX,
      maxTileY
    );

    DrawTileHighlights(
      e,
      _effectiveAreaIntersection.Value,
      Color.Orange,
      minTileX,
      minTileY,
      maxTileX,
      maxTileY
    );
  }

  private static void DrawTileHighlights(
    RenderingHudEventArgs e,
    IEnumerable<Point> tiles,
    Color color,
    int minTileX,
    int minTileY,
    int maxTileX,
    int maxTileY
  )
  {
    foreach (Point point in tiles)
    {
      if (point.X < minTileX || point.X > maxTileX || point.Y < minTileY || point.Y > maxTileY)
      {
        continue;
      }

      var position = new Vector2(
        point.X * Utility.ModifyCoordinateFromUIScale(Game1.tileSize),
        point.Y * Utility.ModifyCoordinateFromUIScale(Game1.tileSize)
      );
      e.SpriteBatch.Draw(
        Game1.mouseCursors,
        Utility.ModifyCoordinatesForUIScale(
          Game1.GlobalToLocal(Utility.ModifyCoordinatesForUIScale(position))
        ),
        TileSpriteRect,
        color,
        0.0f,
        Vector2.Zero,
        Utility.ModifyCoordinateForUIScale(Game1.pixelZoom),
        SpriteEffects.None,
        0.01f
      );
    }
  }

  private void OnButtonChanged(object? sender, ButtonsChangedEventArgs e)
  {
    if (Context.IsPlayerFree)
    {
      if (ModEntry.ModConfig.ShowOneRange.IsDown())
      {
        ButtonShowOneRange = true;
      }

      if (ModEntry.ModConfig.ShowAllRange.IsDown())
      {
        ButtonShowAllRanges = true;
      }
    }
  }

  private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
  {
    if (!ShowRangeTooltip)
    {
      return;
    }

    RangeTooltipInfo? info = _rangeTooltipInfo.Value;
    if (info == null)
    {
      return;
    }

    int reachableTiles = info.RawTotalTiles - info.OccupiedTiles;
    int overlapTiles = _effectiveAreaIntersection.Value.Count;
    int coveredTiles = _effectiveAreaOther.Value.Count + overlapTiles - info.OccupiedTiles;

    SpriteFont font = Game1.smallFont;

    string header = info.ShowingAll ? $"{info.ObjectName} x{info.ObjectCount}" : info.ObjectName;

    List<(string text, Color color)> lines = [(header, Game1.textColor)];

    if (info.SubHeader != null)
    {
      lines.Add((info.SubHeader, Tools.TooltipGreen));
    }

    if (info.WarningMessage != null)
    {
      lines.Add((info.WarningMessage, Tools.TooltipYellow));
    }
    else
    {
      lines.Add((I18n.ReachableTiles(count: reachableTiles), Tools.TooltipBlue));

      if (info.ShowingAll && info.TrackOverlap)
      {
        lines.Add((I18n.CoveredTiles(count: coveredTiles), Tools.TooltipGreen));
      }

      if (info.TrackOverlap && overlapTiles > 0)
      {
        lines.Add((I18n.OverlappingTiles(count: overlapTiles), Tools.TooltipYellow));
      }
    }

    // Sprite dimensions (same layout as ShowTileTooltips.DrawColoredHoverText)
    const int spriteSize = 32;
    const int spritePadding = 4;
    bool hasSprite = info.SpriteTexture != null && info.SpriteSourceRect != null;
    float spriteScale = hasSprite
      ? spriteSize
        / (float)Math.Max(info.SpriteSourceRect!.Value.Width, info.SpriteSourceRect.Value.Height)
      : 0;
    int renderedSpriteWidth = hasSprite
      ? (int)(info.SpriteSourceRect!.Value.Width * spriteScale)
      : 0;
    int spriteSpace = hasSprite ? renderedSpriteWidth + spritePadding : 0;

    float maxWidth = 0;
    bool isFirstLine = true;
    foreach ((string text, Color _) in lines)
    {
      float w = font.MeasureString(text).X;
      if (isFirstLine)
      {
        w += spriteSpace;
        isFirstLine = false;
      }

      if (w > maxWidth)
      {
        maxWidth = w;
      }
    }

    int boxWidth = (int)maxWidth + 32;
    int boxHeight = Math.Max(60, lines.Count * font.LineSpacing + 40);

    // Position near mouse, keep on screen
    int x = Game1.getMouseX() + 32;
    int y = Game1.getMouseY() + 32;

    if (x + boxWidth > Game1.uiViewport.Width)
    {
      x = Game1.getMouseX() - boxWidth - 8;
    }

    if (y + boxHeight > Game1.uiViewport.Height)
    {
      y = Game1.uiViewport.Height - boxHeight;
    }

    boxWidth += 4;

    IClickableMenu.drawTextureBox(
      e.SpriteBatch,
      Game1.menuTexture,
      new Rectangle(0, 256, 60, 60),
      x,
      y,
      boxWidth,
      boxHeight,
      Color.White
    );

    // Draw text lines with soft shadow (same layout as ShowTileTooltips)
    Color shadowColor = Game1.textShadowColor;
    float lineY = y + 16 + 4;
    bool isFirst = true;
    foreach ((string text, Color color) in lines)
    {
      float segX = x + 16;
      if (isFirst && hasSprite)
      {
        segX += spriteSpace;
      }

      var pos = new Vector2(segX, lineY);
      Tools.DrawShadowedText(e.SpriteBatch, font, text, pos, color, shadowColor);

      // Sprite on first line, vertically centered with text
      if (isFirst && hasSprite)
      {
        Rectangle sourceRect = info.SpriteSourceRect!.Value;
        float spriteCenterY =
          lineY + font.LineSpacing / 2f - (sourceRect.Height * spriteScale) / 2f - 2f;
        Vector2 spritePos = new(x + 16, spriteCenterY);
        e.SpriteBatch.Draw(
          info.SpriteTexture!,
          spritePos,
          sourceRect,
          Color.White,
          0f,
          Vector2.Zero,
          spriteScale,
          SpriteEffects.None,
          0.9f
        );
        isFirst = false;
      }

      lineY += font.LineSpacing;
    }
  }
  #endregion


  #region Logic
  private void UpdateEffectiveArea()
  {
    int[][] arrayToUse;
    List<Object> similarObjects;
    _rangeTooltipInfo.Value = null;

    if (ButtonControlShow && (ButtonShowOneRange || ButtonShowAllRanges))
    {
      Building building = Game1.currentLocation.getBuildingAt(Game1.GetPlacementGrabTile());

      if (building is JunimoHut hoveredHut)
      {
        // Re-read max radius live for config changes
        if (ApiManager.GetApi(ModCompat.BetterJunimos, out IBetterJunimosApi? betterJunimosApi))
        {
          _junimoHutRadius = betterJunimosApi.GetJunimoHutMaxRadius();
        }

        arrayToUse = GetDistanceArray(ObjectsWithDistance.JunimoHut);
        int hutTiles = CountTilesInArray(arrayToUse);

        _rangeTooltipInfo.Value = new RangeTooltipInfo
        {
          ObjectName = I18n.JunimoHuts(),
          TrackOverlap = true,
          ObjectCount = 1,
          ShowingAll = ButtonShowAllRanges,
          OccupiedTiles = 6, // 3x2 building
          RawTotalTiles = hutTiles,
        };

        AddTilesToHighlightedArea(
          arrayToUse,
          !ButtonShowAllRanges,
          hoveredHut.tileX.Value + 1,
          hoveredHut.tileY.Value + 1
        );

        if (ButtonShowAllRanges)
        {
          foreach (Building? nextBuilding in Game1.currentLocation.buildings)
          {
            if (nextBuilding is JunimoHut nextHut && nextHut != hoveredHut)
            {
              _rangeTooltipInfo.Value.ObjectCount++;
              _rangeTooltipInfo.Value.OccupiedTiles += 6;
              _rangeTooltipInfo.Value.RawTotalTiles += hutTiles;
              AddTilesToHighlightedArea(
                arrayToUse,
                false,
                nextHut.tileX.Value + 1,
                nextHut.tileY.Value + 1
              );
            }
          }
        }
      }
    }

    // Wild tree seed spread (Farm locations only)
    if (ButtonControlShow && (ButtonShowOneRange || ButtonShowAllRanges))
    {
      Vector2 gamepadTile =
        Game1.player.CurrentTool != null
          ? Utility.snapToInt(Game1.player.GetToolLocation() / Game1.tileSize)
          : Utility.snapToInt(Game1.player.GetGrabTile());
      Vector2 mouseTile = Game1.currentCursorTile;
      Vector2 treeTile =
        Game1.options.gamepadControls && Game1.timerUntilMouseFade <= 0 ? gamepadTile : mouseTile;

      if (
        Game1.currentLocation.terrainFeatures.TryGetValue(treeTile, out TerrainFeature? feature)
        && feature is Tree tree
        && tree.growthStage.Value >= 5
        && !tree.stump.Value
      )
      {
        float seedSpreadChance = tree.GetData()?.SeedSpreadChance ?? 0.15f;

        if (seedSpreadChance <= 0f)
        {
          string treeName = ShowTileTooltips.GetTreeDisplayName(tree.treeType.Value);
          _rangeTooltipInfo.Value = new RangeTooltipInfo
          {
            ObjectName = treeName,
            WarningMessage = I18n.NoSeedSpread(),
            SpriteTexture = _wildTreeTexture.Value,
            SpriteSourceRect = new Rectangle(0, 0, 12, 16),
          };
        }
        else if (Game1.currentLocation is Farm)
        {
          arrayToUse = GetDistanceArray(ObjectsWithDistance.WildTreeSeedSpread);

          string treeName = ButtonShowAllRanges
            ? I18n.WildTree()
            : ShowTileTooltips.GetTreeDisplayName(tree.treeType.Value);

          int tilesBeforeAdd = _effectiveAreaOther.Value.Count;
          AddTilesToHighlightedArea(
            arrayToUse,
            false,
            (int)treeTile.X,
            (int)treeTile.Y,
            skipOccupied: true
          );
          int reachableTiles = _effectiveAreaOther.Value.Count - tilesBeforeAdd;

          _rangeTooltipInfo.Value = new RangeTooltipInfo
          {
            ObjectName = treeName,
            SubHeader = I18n.SeedRange(),
            TrackOverlap = false,
            ObjectCount = 1,
            ShowingAll = ButtonShowAllRanges,
            OccupiedTiles = 0,
            RawTotalTiles = reachableTiles,
            SpriteTexture = _wildTreeTexture.Value,
            SpriteSourceRect = new Rectangle(0, 0, 12, 16),
          };

          if (ButtonShowAllRanges)
          {
            foreach (
              KeyValuePair<Vector2, TerrainFeature> pair in Game1
                .currentLocation
                .terrainFeatures
                .Pairs
            )
            {
              if (
                pair.Value is Tree otherTree
                && otherTree != tree
                && otherTree.growthStage.Value >= 5
                && !otherTree.stump.Value
                && (otherTree.GetData()?.SeedSpreadChance ?? 0.15f) > 0f
              )
              {
                _rangeTooltipInfo.Value.ObjectCount++;
                tilesBeforeAdd = _effectiveAreaOther.Value.Count;
                AddTilesToHighlightedArea(
                  arrayToUse,
                  false,
                  (int)pair.Key.X,
                  (int)pair.Key.Y,
                  skipOccupied: true
                );
                _rangeTooltipInfo.Value.RawTotalTiles +=
                  _effectiveAreaOther.Value.Count - tilesBeforeAdd;
              }
            }
          }
        }
        else
        {
          string treeName = ShowTileTooltips.GetTreeDisplayName(tree.treeType.Value);
          _rangeTooltipInfo.Value = new RangeTooltipInfo
          {
            ObjectName = treeName,
            WarningMessage = I18n.LocationNotFarm(),
            SpriteTexture = _wildTreeTexture.Value,
            SpriteSourceRect = new Rectangle(0, 0, 12, 16),
          };
        }
      }
    }

    // Placed objects (button-controlled range display)
    if (ButtonControlShow && (ButtonShowOneRange || ButtonShowAllRanges))
    {
      Vector2 gamepadTile =
        Game1.player.CurrentTool != null
          ? Utility.snapToInt(Game1.player.GetToolLocation() / Game1.tileSize)
          : Utility.snapToInt(Game1.player.GetGrabTile());
      Vector2 mouseTile = Game1.currentCursorTile;
      Vector2 tile =
        Game1.options.gamepadControls && Game1.timerUntilMouseFade <= 0 ? gamepadTile : mouseTile;
      if (Game1.currentLocation.Objects?.TryGetValue(tile, out Object? currentObject) ?? false)
      {
        if (currentObject != null)
        {
          Vector2 currentTile = Game1.GetPlacementGrabTile();
          Game1.isCheckingNonMousePlacement = !Game1.IsPerformingMousePlacement();
          Vector2 validTile =
            Utility.snapToInt(
              Utility.GetNearbyValidPlacementPosition(
                Game1.player,
                Game1.currentLocation,
                currentObject,
                (int)currentTile.X * Game1.tileSize,
                (int)currentTile.Y * Game1.tileSize
              )
            ) / Game1.tileSize;
          Game1.isCheckingNonMousePlacement = false;

          if (currentObject.IsScarecrow())
          {
            arrayToUse = GetDistanceArray(ObjectsWithDistance.Scarecrow, currentObject);

            int tilesBeforeAdd =
              _effectiveAreaOther.Value.Count + _effectiveAreaCurrent.Value.Count;
            AddTilesToHighlightedArea(
              arrayToUse,
              !ButtonShowAllRanges,
              (int)validTile.X,
              (int)validTile.Y,
              skipNonTillable: true
            );
            int reachableTiles =
              _effectiveAreaOther.Value.Count + _effectiveAreaCurrent.Value.Count - tilesBeforeAdd;

            _rangeTooltipInfo.Value = new RangeTooltipInfo
            {
              ObjectName = ButtonShowAllRanges ? I18n.Scarecrows() : currentObject.DisplayName,
              TrackOverlap = true,
              ObjectCount = 1,
              ShowingAll = ButtonShowAllRanges,
              OccupiedTiles = 0,
              RawTotalTiles = reachableTiles,
            };
            _rangeTooltipInfo.Value.SetSpriteFromObject(currentObject);

            if (ButtonShowAllRanges)
            {
              similarObjects = GetSimilarObjectsInLocation(static o => o.IsScarecrow());
              foreach (Object next in similarObjects)
              {
                if (!next.Equals(currentObject))
                {
                  _rangeTooltipInfo.Value.ObjectCount++;
                  int[][] arrayToUse_ = GetDistanceArray(ObjectsWithDistance.Scarecrow, next);
                  tilesBeforeAdd = _effectiveAreaOther.Value.Count;
                  AddTilesToHighlightedArea(
                    arrayToUse_,
                    false,
                    (int)next.TileLocation.X,
                    (int)next.TileLocation.Y,
                    skipNonTillable: true
                  );
                  _rangeTooltipInfo.Value.RawTotalTiles +=
                    _effectiveAreaOther.Value.Count - tilesBeforeAdd;
                }
              }
            }
          }
          else if (currentObject.IsSprinkler())
          {
            List<Vector2> sprinklerTilesList = currentObject.GetSprinklerTiles();

            IEnumerable<Vector2> unplacedSprinklerTiles = sprinklerTilesList;
            if (currentObject.TileLocation != validTile)
            {
              unplacedSprinklerTiles = unplacedSprinklerTiles.Select(tile =>
                tile - currentObject.TileLocation + validTile
              );
            }

            int tilesBeforeAdd = _effectiveAreaOther.Value.Count;
            AddTilesToHighlightedArea(
              unplacedSprinklerTiles,
              !ButtonShowAllRanges,
              skipNonTillable: true
            );
            int reachableTiles =
              _effectiveAreaOther.Value.Count + _effectiveAreaCurrent.Value.Count - tilesBeforeAdd;

            _rangeTooltipInfo.Value = new RangeTooltipInfo
            {
              ObjectName = ButtonShowAllRanges ? I18n.Sprinklers() : currentObject.DisplayName,
              TrackOverlap = true,
              ObjectCount = 1,
              ShowingAll = ButtonShowAllRanges,
              OccupiedTiles = 0,
              RawTotalTiles = reachableTiles,
            };
            _rangeTooltipInfo.Value.SetSpriteFromObject(currentObject);

            if (ButtonShowAllRanges)
            {
              similarObjects = GetSimilarObjectsInLocation(static o => o.IsSprinkler());
              foreach (Object next in similarObjects)
              {
                if (!next.Equals(currentObject))
                {
                  _rangeTooltipInfo.Value.ObjectCount++;
                  tilesBeforeAdd = _effectiveAreaOther.Value.Count;
                  AddTilesToHighlightedArea(next.GetSprinklerTiles(), false, skipNonTillable: true);
                  _rangeTooltipInfo.Value.RawTotalTiles +=
                    _effectiveAreaOther.Value.Count - tilesBeforeAdd;
                }
              }
            }
          }
          else if (
            currentObject.Name.IndexOf("bee house", StringComparison.OrdinalIgnoreCase) >= 0
            || currentObject.HasContextTag("bee_house")
          )
          {
            arrayToUse = GetDistanceArray(ObjectsWithDistance.Beehouse);
            _rangeTooltipInfo.Value = new RangeTooltipInfo
            {
              ObjectName = currentObject.DisplayName,
              TrackOverlap = false,
              ObjectCount = 1,
              OccupiedTiles = 1,
              RawTotalTiles = CountTilesInArray(arrayToUse),
            };
            _rangeTooltipInfo.Value.SetSpriteFromObject(currentObject);

            AddTilesToHighlightedArea(arrayToUse, false, (int)validTile.X, (int)validTile.Y);
          }
          else if (currentObject.QualifiedItemId == MushroomLogQualifiedId)
          {
            arrayToUse = GetDistanceArray(ObjectsWithDistance.MushroomLog);
            _rangeTooltipInfo.Value = new RangeTooltipInfo
            {
              ObjectName = currentObject.DisplayName,
              TrackOverlap = false,
              ObjectCount = 1,
              OccupiedTiles = 1,
              RawTotalTiles = CountTilesInArray(arrayToUse),
            };
            _rangeTooltipInfo.Value.SetSpriteFromObject(currentObject);

            AddTilesToHighlightedArea(arrayToUse, false, (int)validTile.X, (int)validTile.Y);
          }
          else if (currentObject.QualifiedItemId == MossySeedQualifiedId)
          {
            arrayToUse = GetDistanceArray(ObjectsWithDistance.MossySeed);
            _rangeTooltipInfo.Value = new RangeTooltipInfo
            {
              ObjectName = currentObject.DisplayName,
              TrackOverlap = false,
              ObjectCount = 1,
              OccupiedTiles = 1,
              RawTotalTiles = CountTilesInArray(arrayToUse),
            };
            _rangeTooltipInfo.Value.SetSpriteFromObject(currentObject);

            AddTilesToHighlightedArea(arrayToUse, false, (int)validTile.X, (int)validTile.Y);
          }
          // Last, so a vanilla match always wins
          else if (TryGetCustomRange(currentObject, out ItemEffectRangeData? customRange))
          {
            AddCustomRange(currentObject, customRange, validTile, true, ButtonShowAllRanges);

            if (ButtonShowAllRanges)
            {
              string customId = currentObject.QualifiedItemId;
              similarObjects = GetSimilarObjectsInLocation(o => o.QualifiedItemId == customId);
              foreach (Object next in similarObjects)
              {
                if (!next.Equals(currentObject))
                {
                  AddCustomRange(next, customRange, next.TileLocation, false, true);
                }
              }
            }
          }
        }
      }
    }
    if (Game1.player.CurrentItem is Object currentItem && currentItem.isPlaceable())
    {
      string itemName = currentItem.Name;

      // Match the game's ghost-preview tile: raw cursor for mouse, valid-placement snap for gamepad
      Vector2 grabTile = Game1.GetPlacementGrabTile();
      Vector2 cursorTile;
      if (Game1.IsPerformingMousePlacement())
      {
        cursorTile = Utility.snapToInt(grabTile);
      }
      else
      {
        Game1.isCheckingNonMousePlacement = true;
        cursorTile = Utility.snapToInt(
          Utility.GetNearbyValidPlacementPosition(
            Game1.player,
            Game1.currentLocation,
            currentItem,
            (int)grabTile.X * Game1.tileSize,
            (int)grabTile.Y * Game1.tileSize
          ) / Game1.tileSize
        );
        Game1.isCheckingNonMousePlacement = false;
      }

      if (_showItemEffectRanges)
      {
        if (currentItem.IsScarecrow())
        {
          arrayToUse = GetDistanceArray(ObjectsWithDistance.Scarecrow, currentItem);
          AddTilesToHighlightedArea(
            arrayToUse,
            true,
            (int)cursorTile.X,
            (int)cursorTile.Y,
            skipNonTillable: true
          );

          if (_showPlacedItemRanges)
          {
            similarObjects = GetSimilarObjectsInLocation(static o => o.IsScarecrow());
            foreach (Object next in similarObjects)
            {
              arrayToUse = GetDistanceArray(ObjectsWithDistance.Scarecrow, next);
              AddTilesToHighlightedArea(
                arrayToUse,
                false,
                (int)next.TileLocation.X,
                (int)next.TileLocation.Y,
                skipNonTillable: true
              );
            }
          }
        }
        else if (currentItem.IsSprinkler())
        {
          // GetSprinklerTiles returns absolute positions in 1.6+ - offset to valid placement tile
          IEnumerable<Vector2> unplacedSprinklerTiles = currentItem.GetSprinklerTiles();
          if (currentItem.TileLocation != cursorTile)
          {
            unplacedSprinklerTiles = unplacedSprinklerTiles.Select(tile =>
              tile - currentItem.TileLocation + cursorTile
            );
          }

          AddTilesToHighlightedArea(unplacedSprinklerTiles, true, skipNonTillable: true);

          if (_showPlacedItemRanges)
          {
            similarObjects = GetSimilarObjectsInLocation(static o => o.IsSprinkler());
            foreach (Object next in similarObjects)
            {
              AddTilesToHighlightedArea(next.GetSprinklerTiles(), false, skipNonTillable: true);
            }
          }
        }
        else if (
          itemName.IndexOf("bee house", StringComparison.OrdinalIgnoreCase) >= 0
          || currentItem.HasContextTag("bee_house")
        )
        {
          arrayToUse = GetDistanceArray(ObjectsWithDistance.Beehouse);
          AddTilesToHighlightedArea(arrayToUse, false, (int)cursorTile.X, (int)cursorTile.Y);
        }
        else if (currentItem.QualifiedItemId == MushroomLogQualifiedId)
        {
          arrayToUse = GetDistanceArray(ObjectsWithDistance.MushroomLog);
          AddTilesToHighlightedArea(arrayToUse, false, (int)cursorTile.X, (int)cursorTile.Y);
        }
        else if (currentItem.QualifiedItemId == MossySeedQualifiedId)
        {
          arrayToUse = GetDistanceArray(ObjectsWithDistance.MossySeed);
          AddTilesToHighlightedArea(arrayToUse, false, (int)cursorTile.X, (int)cursorTile.Y);
        }
        // Last, so a vanilla match always wins
        else if (TryGetCustomRange(currentItem, out ItemEffectRangeData? customRange))
        {
          arrayToUse = GetCustomRangeMask(customRange);
          AddTilesToHighlightedArea(
            arrayToUse,
            customRange.AffectsCrops,
            (int)cursorTile.X,
            (int)cursorTile.Y,
            skipNonTillable: customRange.AffectsCrops
          );

          // Overlap only matters for crop effects
          if (_showPlacedItemRanges && customRange.AffectsCrops)
          {
            string customId = currentItem.QualifiedItemId;
            similarObjects = GetSimilarObjectsInLocation(o => o.QualifiedItemId == customId);
            foreach (Object next in similarObjects)
            {
              AddTilesToHighlightedArea(
                arrayToUse,
                false,
                (int)next.TileLocation.X,
                (int)next.TileLocation.Y,
                skipNonTillable: true
              );
            }
          }
        }
      }

      if (ShowBombRange)
      {
        ObjectsWithDistance? bombType = currentItem.QualifiedItemId switch
        {
          MegaBombQualifiedId => ObjectsWithDistance.MegaBomb,
          CherryBombQualifiedId => ObjectsWithDistance.CherryBomb,
          BombQualifiedId => ObjectsWithDistance.Bomb,
          _ => null,
        };

        if (bombType.HasValue)
        {
          arrayToUse = GetDistanceArray(bombType.Value);
          AddTilesToHighlightedArea(arrayToUse, false, (int)cursorTile.X, (int)cursorTile.Y);
        }
      }
    }
  }

  private void AddTilesToHighlightedArea(
    IEnumerable<Vector2> tiles,
    bool overlap,
    int xPos = 0,
    int yPos = 0,
    bool skipNonTillable = false
  )
  {
    GameLocation? location = skipNonTillable ? Game1.currentLocation : null;

    // Viewport culling bounds
    xTile.Dimensions.Rectangle vp = Game1.viewport;
    int minTileX = vp.X / Game1.tileSize - 1;
    int minTileY = vp.Y / Game1.tileSize - 1;
    int maxTileX = (vp.X + vp.Width) / Game1.tileSize + 1;
    int maxTileY = (vp.Y + vp.Height) / Game1.tileSize + 1;

    foreach (Vector2 tile in tiles)
    {
      var point = tile.ToPoint();
      point.X += xPos;
      point.Y += yPos;

      if (point.X < minTileX || point.X > maxTileX || point.Y < minTileY || point.Y > maxTileY)
      {
        continue;
      }

      if (location != null)
      {
        var tileVec = new Vector2(point.X, point.Y);
        if (!IsTillable(location, point, tileVec) || IsTileBlocked(location, tileVec))
        {
          continue;
        }
      }

      if (overlap)
      {
        _effectiveAreaCurrent.Value.Add(point);
      }
      else
      {
        if (!_seenTiles.Value.Add(point))
        {
          _effectiveAreaIntersection.Value.Add(point);
        }

        _effectiveAreaOther.Value.Add(point);
      }
    }
  }

  private void AddTilesToHighlightedArea(
    int[][] tileMap,
    bool overlap,
    int xPos = 0,
    int yPos = 0,
    bool skipOccupied = false,
    bool skipNonTillable = false
  )
  {
    int xOffset = tileMap.Length / 2;
    GameLocation? location = (skipOccupied || skipNonTillable) ? Game1.currentLocation : null;

    // Viewport culling bounds
    xTile.Dimensions.Rectangle vp = Game1.viewport;
    int minTileX = vp.X / Game1.tileSize - 1;
    int minTileY = vp.Y / Game1.tileSize - 1;
    int maxTileX = (vp.X + vp.Width) / Game1.tileSize + 1;
    int maxTileY = (vp.Y + vp.Height) / Game1.tileSize + 1;

    for (var i = 0; i < tileMap.Length; ++i)
    {
      int yOffset = tileMap[i].Length / 2;
      for (var j = 0; j < tileMap[i].Length; ++j)
      {
        if (tileMap[i][j] == 1)
        {
          var point = new Point(xPos + i - xOffset, yPos + j - yOffset);

          if (point.X < minTileX || point.X > maxTileX || point.Y < minTileY || point.Y > maxTileY)
          {
            continue;
          }

          if (location != null)
          {
            var tileVec = new Vector2(point.X, point.Y);

            if (skipNonTillable)
            {
              if (!IsTillable(location, point, tileVec) || IsTileBlocked(location, tileVec))
              {
                continue;
              }
            }
            else if (skipOccupied)
            {
              if (
                location.IsTileOccupiedBy(tileVec, ignorePassables: CollisionMask.Farmers)
                || !location.isTileLocationOpen(tileVec)
              )
              {
                continue;
              }
            }
          }

          if (overlap)
          {
            _effectiveAreaCurrent.Value.Add(point);
          }
          else
          {
            if (!_seenTiles.Value.Add(point))
            {
              _effectiveAreaIntersection.Value.Add(point);
            }

            _effectiveAreaOther.Value.Add(point);
          }
        }
      }
    }
  }

  private static int CountTilesInArray(int[][] tileMap)
  {
    int count = 0;
    for (var i = 0; i < tileMap.Length; ++i)
    {
      for (var j = 0; j < tileMap[i].Length; ++j)
      {
        if (tileMap[i][j] == 1)
        {
          count++;
        }
      }
    }

    return count;
  }

  /// <summary>Whether a crop could occupy this tile. Slime hutches count throughout.</summary>
  private static bool IsTillable(GameLocation location, Point point, Vector2 tile)
  {
    return location is SlimeHutch
      || location.doesTileHaveProperty(point.X, point.Y, "Diggable", "Back") != null
      || location.isTileHoeDirt(tile)
      || (location.Objects.TryGetValue(tile, out Object? potObj) && potObj is IndoorPot);
  }

  /// <summary>
  /// Whether a tile is blocked. Ignores farmers and HoeDirt (tilled soil, with or without crops).
  /// </summary>
  private static bool IsTileBlocked(GameLocation location, Vector2 tile)
  {
    // Inside a Slime Hutch, sprinklers fill water troughs regardless of tile passability
    if (location is SlimeHutch)
    {
      return false;
    }

    // Map layout (cliffs, built-in fences, walls on Buildings layer)
    if (!location.isTilePassable(tile))
    {
      return true;
    }

    // Objects (fences, crafted items, etc.) - garden pots are valid planting targets
    if (
      location.Objects.TryGetValue(tile, out Object? obj)
      && !obj.isPassable()
      && obj is not IndoorPot
    )
    {
      return true;
    }

    if (location.GetFurnitureAt(tile) is { } furniture && !furniture.isPassable())
    {
      return true;
    }

    if (location.IsTileOccupiedBy(tile, CollisionMask.Buildings))
    {
      return true;
    }

    // Terrain features - allow HoeDirt (including trellis crops), block flooring/paths and non-passable features
    if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature? tf) && tf is not HoeDirt)
    {
      if (tf is Flooring || !tf.isPassable())
      {
        return true;
      }
    }

    // Large terrain features (bushes) and resource clumps
    foreach (LargeTerrainFeature ltf in location.largeTerrainFeatures)
    {
      if (
        ltf.getBoundingBox().Contains((int)tile.X * 64 + 32, (int)tile.Y * 64 + 32)
        && !ltf.isPassable()
      )
      {
        return true;
      }
    }

    foreach (ResourceClump clump in location.resourceClumps)
    {
      if (clump.occupiesTile((int)tile.X, (int)tile.Y))
      {
        return true;
      }
    }

    return false;
  }

  private static List<Object> GetSimilarObjectsInLocation(Func<Object, bool> predicate)
  {
    List<Object> result = [];

    OverlaidDictionary? objects = Game1.currentLocation.Objects;
    if (objects != null)
    {
      foreach (Object? nextThing in objects.Values)
      {
        if (nextThing != null && predicate(nextThing))
        {
          result.Add(nextThing);
        }
      }
    }

    return result;
  }

  /// <summary>Compute intersection and exclusive areas between current and other ranges.</summary>
  private void GetOverlapValue()
  {
    if (_effectiveAreaCurrent.Value.Count == 0)
    {
      if (_rangeTooltipInfo.Value != null)
      {
        // Show-all keybind mode: overlaps already detected via _seenTiles during tile addition
        _effectiveAreaOther.Value.ExceptWith(_effectiveAreaIntersection.Value);
      }
      else
      {
        // Held-item mode, all tiles filtered (e.g. flooring); placed-item intersections not meaningful
        _effectiveAreaIntersection.Value.Clear();
      }

      return;
    }

    // Show-one mode: compute overlap between hovered (current) and others
    var currentSet = new HashSet<Point>(_effectiveAreaCurrent.Value);
    _effectiveAreaIntersection.Value.Clear();
    foreach (Point p in currentSet)
    {
      if (_effectiveAreaOther.Value.Contains(p))
      {
        _effectiveAreaIntersection.Value.Add(p);
      }
    }

    // Remove intersection tiles from both sets so they only render once (as overlap)
    _effectiveAreaOther.Value.ExceptWith(_effectiveAreaIntersection.Value);
    _effectiveAreaCurrent.Value.RemoveAll(_effectiveAreaIntersection.Value.Contains);
  }

  #region Distance map
  private enum ObjectsWithDistance
  {
    JunimoHut,
    Beehouse,
    Scarecrow,
    MushroomLog,
    MossySeed,
    WildTreeSeedSpread,
    CherryBomb,
    Bomb,
    MegaBomb,
  }

  private int[][] GetDistanceArray(ObjectsWithDistance type, Object? instance = null)
  {
    return type switch
    {
      ObjectsWithDistance.JunimoHut => GetCircularMask(
        100,
        maxDisplaySquareRadius: _junimoHutRadius
      ),
      ObjectsWithDistance.Beehouse => GetCircularMask(4.19, 5, true),
      ObjectsWithDistance.Scarecrow => GetCircularMask(
        (instance?.GetRadiusForScarecrow() ?? 9) - 0.01
      ),
      ObjectsWithDistance.MushroomLog => GetCircularMask(100, maxDisplaySquareRadius: 3),
      ObjectsWithDistance.MossySeed => GetCircularMask(100, maxDisplaySquareRadius: 2),
      ObjectsWithDistance.WildTreeSeedSpread => GetCircularMask(100, maxDisplaySquareRadius: 3),
      ObjectsWithDistance.CherryBomb => GetCircularMask(3.39),
      ObjectsWithDistance.Bomb => GetCircularMask(5.52),
      ObjectsWithDistance.MegaBomb => GetCircularMask(7.45),
      _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
  }

  private static readonly Dictionary<(double, double?, bool?, int?), int[][]> CircularMaskCache =
  [];

  private static int[][] GetCircularMask(
    double maxDistance,
    double? exceptionalDistance = null,
    bool? onlyClearExceptions = null,
    int? maxDisplaySquareRadius = null
  )
  {
    var key = (maxDistance, exceptionalDistance, onlyClearExceptions, maxDisplaySquareRadius);
    if (CircularMaskCache.TryGetValue(key, out int[][]? cached))
    {
      return cached;
    }

    int radius = Math.Max(
      (int)Math.Ceiling(maxDistance),
      exceptionalDistance.HasValue ? (int)Math.Ceiling(exceptionalDistance.Value) : 0
    );
    radius = Math.Min(
      radius,
      maxDisplaySquareRadius.HasValue ? maxDisplaySquareRadius.Value : radius
    );
    int size = 2 * radius + 1;

    var result = new int[size][];
    for (var i = 0; i < size; i++)
    {
      result[i] = new int[size];
      for (var j = 0; j < size; j++)
      {
        double distance = GetDistance(i, j, radius);
        int val =
          IsInDistance(maxDistance, distance)
          || (
            IsDistanceDirectionOK(i, j, radius, onlyClearExceptions)
            && IsExceptionalDistanceOK(exceptionalDistance, distance)
          )
            ? 1
            : 0;
        result[i][j] = val;
      }
    }

    CircularMaskCache[key] = result;
    return result;
  }

  private static bool IsDistanceDirectionOK(int i, int j, int radius, bool? onlyClearExceptions)
  {
    return onlyClearExceptions.HasValue && onlyClearExceptions.Value
      ? radius - j == 0 || radius - i == 0
      : true;
  }

  private static bool IsExceptionalDistanceOK(double? exceptionalDistance, double distance)
  {
    return exceptionalDistance.HasValue && exceptionalDistance.Value == distance;
  }

  private static bool IsInDistance(double maxDistance, double distance)
  {
    return distance <= maxDistance;
  }

  private static double GetDistance(int i, int j, int radius)
  {
    return Math.Sqrt((radius - i) * (radius - i) + (radius - j) * (radius - j));
  }

  private static readonly Dictionary<int, int[][]> DiamondMaskCache = [];

  private static int[][] GetDiamondMask(int radius)
  {
    if (DiamondMaskCache.TryGetValue(radius, out int[][]? cached))
    {
      return cached;
    }

    int size = 2 * radius + 1;
    var result = new int[size][];
    for (var i = 0; i < size; i++)
    {
      result[i] = new int[size];
      for (var j = 0; j < size; j++)
      {
        result[i][j] = Math.Abs(radius - i) + Math.Abs(radius - j) <= radius ? 1 : 0;
      }
    }

    DiamondMaskCache[radius] = result;
    return result;
  }
  #endregion
  #endregion
}
