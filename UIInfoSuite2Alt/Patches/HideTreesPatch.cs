using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.Patches;

internal static class HideTreesPatch
{
  private const int WildTreeFullGrown = 5;
  private const int WildTreeMinStage = 1;
  private const int FruitTreeFullGrown = 4;
  private const int FruitTreeMinStage = 0;

  // ms per animation step (one growth stage transition)
  private const double MsPerStep = 100.0;

  // Animation direction: 1 = shrinking, -1 = growing back, 0 = idle
  private static int _animDirection;

  // Only meaningful when idle: 0 = fully visible, TotalSteps = fully hidden
  private static int _animStepsCompleted;

  // Elapsed ms since current animation started (counts up regardless of direction)
  private static double _animElapsedMs;

  // Last global step for which effects were spawned (prevents re-triggering)
  private static int _lastGlobalStep;

  // Total steps for the animation (same for both tree types: 4)
  private const int TotalSteps = 4;

  // Per-tree jitter range in ms (+/- this value)
  private const double JitterRangeMs = 40.0;

  // Effect count per tree per step
  private const int LeavesPerTree = 8;

  private static Texture2D _sparkleTexture = null!;

  // NetInt.Value marks the field dirty and broadcasts to every player; hiding trees is local-only.
  private static readonly AccessTools.FieldRef<NetInt, int>? NetIntValue = TryGetNetIntValueRef();

  private static AccessTools.FieldRef<NetInt, int>? TryGetNetIntValueRef()
  {
    try
    {
      return AccessTools.FieldRefAccess<NetInt, int>("value");
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"HideTreesPatch: could not access NetInt backing field, tree hiding will sync redundant net updates. {ex.Message}",
        LogLevel.Warn
      );
      return null;
    }
  }

  private static void SetLocalOnly(NetInt field, int value)
  {
    if (NetIntValue != null)
    {
      NetIntValue(field) = value;
      return;
    }

    field.Value = value;
  }

  public static bool IsFullyHidden => _animDirection == 0 && _animStepsCompleted == TotalSteps;
  public static bool IsFullyVisible => _animDirection == 0 && _animStepsCompleted == 0;
  public static bool IsAnimating => _animDirection != 0;

  public static void Initialize(Harmony harmony, IModHelper helper)
  {
    // Finalizers restore the real stage even if draw throws; postfixes are skipped on exception.
    harmony.Patch(
      original: AccessTools.Method(typeof(Tree), nameof(Tree.draw)),
      prefix: new HarmonyMethod(typeof(HideTreesPatch), nameof(Tree_Draw_Prefix)),
      postfix: new HarmonyMethod(typeof(HideTreesPatch), nameof(Tree_Draw_Postfix)),
      finalizer: new HarmonyMethod(typeof(HideTreesPatch), nameof(Tree_Draw_Finalizer))
    );

    harmony.Patch(
      original: AccessTools.Method(typeof(FruitTree), nameof(FruitTree.draw)),
      prefix: new HarmonyMethod(typeof(HideTreesPatch), nameof(FruitTree_Draw_Prefix)),
      postfix: new HarmonyMethod(typeof(HideTreesPatch), nameof(FruitTree_Draw_Postfix)),
      finalizer: new HarmonyMethod(typeof(HideTreesPatch), nameof(FruitTree_Draw_Finalizer))
    );

    harmony.Patch(
      original: AccessTools.Method(typeof(Tree), nameof(Tree.performToolAction)),
      prefix: new HarmonyMethod(typeof(HideTreesPatch), nameof(Tree_ToolAction_Prefix))
    );

    harmony.Patch(
      original: AccessTools.Method(typeof(FruitTree), nameof(FruitTree.performToolAction)),
      prefix: new HarmonyMethod(typeof(HideTreesPatch), nameof(FruitTree_ToolAction_Prefix))
    );

    _sparkleTexture = AssetHelper.TryLoadTexture(helper, "assets/sparkle_animation.png");

    ModEntry.MonitorObject.Log("HideTreesPatch: initialized", LogLevel.Trace);
  }

  /// <summary>Toggle the hide trees animation. Ignored if already animating.</summary>
  public static void Toggle()
  {
    if (_animDirection != 0)
    {
      return;
    }

    _animDirection = _animStepsCompleted == 0 ? 1 : -1;
    _animElapsedMs = 0;
    _lastGlobalStep = 0;
  }

  /// <summary>Reset to fully visible (no animation).</summary>
  public static void Reset()
  {
    _animDirection = 0;
    _animStepsCompleted = 0;
    _animElapsedMs = 0;
    _lastGlobalStep = 0;
  }

  #region Animation timing

  /// <summary>Deterministic jitter per tile, mapped to [-JitterRangeMs, +JitterRangeMs].</summary>
  private static double GetTileJitter(Vector2 tile)
  {
    // Spatial hash with large primes + bit mixing for good distribution
    uint hash = (uint)((int)tile.X * 73856093) ^ (uint)((int)tile.Y * 19349663);
    hash ^= hash >> 16;
    hash *= 0x45d9f3b;
    hash ^= hash >> 16;
    double normalized = (hash & 0xFFFF) / (double)0xFFFF * 2.0 - 1.0;
    return normalized * JitterRangeMs;
  }

  /// <summary>
  /// Per-tree steps completed, with jitter applied during animation.
  /// Returns 0 (fully visible) to TotalSteps (fully hidden).
  /// </summary>
  private static int GetTreeStepsCompleted(Vector2 tile)
  {
    if (_animDirection == 0)
    {
      return _animStepsCompleted;
    }

    double jitter = GetTileJitter(tile);
    double adjusted = Math.Clamp(_animElapsedMs + jitter, 0, TotalSteps * MsPerStep);
    int elapsedSteps = Math.Clamp((int)(adjusted / MsPerStep), 0, TotalSteps);

    return _animDirection > 0 ? elapsedSteps : TotalSteps - elapsedSteps;
  }

  private static void AdvanceAnimation()
  {
    if (_animDirection == 0)
    {
      return;
    }

    _animElapsedMs += Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;

    int globalElapsedSteps = Math.Clamp((int)(_animElapsedMs / MsPerStep), 0, TotalSteps);

    // Spawn effects for each new global step crossed
    while (_lastGlobalStep < globalElapsedSteps)
    {
      _lastGlobalStep++;
      int stepsCompleted = _animDirection > 0 ? _lastGlobalStep : TotalSteps - _lastGlobalStep;
      SpawnTreeEffects(_animDirection, stepsCompleted);
    }

    // All trees done (even the most jittered ones have finished)
    if (_animElapsedMs >= TotalSteps * MsPerStep + JitterRangeMs)
    {
      _animStepsCompleted = _animDirection > 0 ? TotalSteps : 0;
      if (_animStepsCompleted == TotalSteps)
      {
        _sparkleTickCounter = 0;
      }
      _animDirection = 0;
      _animElapsedMs = 0;
      _lastGlobalStep = 0;
    }
  }

  // Track whether we already advanced this frame (first tree draw call does it)
  private static long _lastAnimFrameTick = -1;

  private static void TryAdvanceAnimation()
  {
    long currentTick = Game1.ticks;
    if (_lastAnimFrameTick != currentTick)
    {
      _lastAnimFrameTick = currentTick;
      AdvanceAnimation();
    }
  }
  #endregion

  #region Visual effects
  private static void SpawnTreeEffects(int direction, int step)
  {
    GameLocation location = Game1.currentLocation;
    if (location == null)
    {
      return;
    }

    foreach (var pair in location.terrainFeatures.Pairs)
    {
      TerrainFeature feature = pair.Value;
      Vector2 tile = pair.Key;

      if (feature is Tree tree && !tree.stump.Value && tree.growthStage.Value >= WildTreeFullGrown)
      {
        SpawnEffectsAtTile(location, tile, direction, step);
      }
      else if (
        feature is FruitTree fruitTree
        && !fruitTree.stump.Value
        && fruitTree.growthStage.Value >= FruitTreeFullGrown
      )
      {
        SpawnEffectsAtTile(location, tile, direction, step);
      }
    }
  }

  private static void SpawnEffectsAtTile(
    GameLocation location,
    Vector2 tile,
    int direction,
    int step
  )
  {
    if (direction > 0)
    {
      SpawnGreenSparkles(location, tile);
      if (step == TotalSteps)
      {
        SpawnIdleSparkle(location, tile);
      }
    }
    else
    {
      SpawnLeaves(location, tile, step);
    }
  }

  private static void SpawnGreenSparkles(GameLocation location, Vector2 tile)
  {
    // Green sparkle effect (ShadowShaman heal animation) at the tree's stump tile
    location.temporarySprites.Add(
      new TemporaryAnimatedSprite(
        "TileSheets\\animations",
        new Rectangle(0, 256, 64, 64),
        40f,
        8,
        0,
        tile * 64f,
        flicker: false,
        flipped: false
      )
      {
        layerDepth = 1f,
        rotation = (float)(Game1.random.NextDouble() * Math.PI * 2.0),
      }
    );
  }

  private static readonly Color[][] SeasonalLeafColors =
  {
    // Spring - fresh greens
    new[] { new Color(0, 200, 0), new Color(60, 180, 30), new Color(30, 220, 50) },
    // Summer - deeper greens
    new[] { new Color(0, 160, 0), new Color(20, 140, 20), new Color(40, 180, 10) },
    // Fall - orange, red, yellow
    new[] { new Color(220, 140, 0), new Color(200, 60, 20), new Color(210, 190, 30) },
    // Winter - browns
    new[] { new Color(139, 90, 43), new Color(110, 80, 50), new Color(160, 110, 60) },
  };

  private static Color GetSeasonalLeafColor(GameLocation location)
  {
    int seasonIndex = Game1.GetSeasonIndexForLocation(location);
    Color[] palette = SeasonalLeafColors[seasonIndex];
    return palette[Game1.random.Next(palette.Length)];
  }

  private static void SpawnLeaves(GameLocation location, Vector2 tile, int step)
  {
    // Leaves spawn progressively higher as regrowth advances.
    // step goes from TotalSteps down to 1 (since direction is -1).
    // At step 4 (just started regrowing): leaves near base.
    // At step 1 (almost full): leaves at canopy top.
    float yOffset = step switch
    {
      4 => -0.1f,
      3 => -0.5f,
      2 => -1.15f,
      1 => -2.05f,
      _ => -2.7f, // step 0 - canopy burst at full growth
    };

    for (int i = 0; i < LeavesPerTree; i++)
    {
      location.temporarySprites.Add(
        new TemporaryAnimatedSprite(
          "TileSheets\\debris",
          new Rectangle(Game1.random.Next(2) * 16, 96, 16, 16),
          new Vector2(
            tile.X + (float)Game1.random.NextDouble() - 0.15f,
            tile.Y + yOffset + (float)Game1.random.NextDouble()
          ) * 64f,
          flipped: false,
          0.025f,
          GetSeasonalLeafColor(location)
        )
        {
          motion = new Vector2((float)Game1.random.Next(-10, 11) / 10f, -4f),
          acceleration = new Vector2(0f, 0.3f + (float)Game1.random.Next(-10, 11) / 200f),
          animationLength = 1,
          interval = 1000f,
          sourceRectStartingPos = new Vector2(0f, 96f),
          alpha = 1f,
          layerDepth = 1f,
          scale = 4f,
        }
      );
    }
  }
  #endregion

  #region Display stage helpers
  private static int GetWildTreeDisplayStage(int realStage, int stepsCompleted)
  {
    if (stepsCompleted == 0)
    {
      return realStage;
    }

    int displayStage = WildTreeFullGrown - stepsCompleted;
    return displayStage < WildTreeMinStage ? WildTreeMinStage : displayStage;
  }

  private static int GetFruitTreeDisplayStage(int realStage, int stepsCompleted)
  {
    if (stepsCompleted == 0)
    {
      return realStage;
    }

    int displayStage = FruitTreeFullGrown - stepsCompleted;
    return displayStage < FruitTreeMinStage ? FruitTreeMinStage : displayStage;
  }
  #endregion

  #region Tree patches
  private static void Tree_Draw_Prefix(Tree __instance, out int __state)
  {
    __state = __instance.growthStage.Value;

    if (_animStepsCompleted == 0 && _animDirection == 0)
    {
      return;
    }

    if (__instance.stump.Value || __state < WildTreeFullGrown)
    {
      return;
    }

    TryAdvanceAnimation();
    int treeSteps = GetTreeStepsCompleted(__instance.Tile);
    SetLocalOnly(__instance.growthStage, GetWildTreeDisplayStage(__state, treeSteps));
  }

  private static bool Tree_ToolAction_Prefix(Tree __instance)
  {
    if (
      !IsFullyVisible
      && !__instance.stump.Value
      && __instance.growthStage.Value >= WildTreeFullGrown
    )
    {
      return false;
    }

    return true;
  }

  private static void Tree_Draw_Postfix(Tree __instance, int __state)
  {
    SetLocalOnly(__instance.growthStage, __state);

    if (IsFullyHidden && !__instance.stump.Value && __state >= WildTreeFullGrown)
    {
      TrySpawnIdleSparkle(__instance.Tile);
    }
  }

  private static void Tree_Draw_Finalizer(Tree __instance, int __state)
  {
    SetLocalOnly(__instance.growthStage, __state);
  }
  #endregion

  #region FruitTree patches
  private static void FruitTree_Draw_Prefix(FruitTree __instance, out (int stage, int days) __state)
  {
    __state = (__instance.growthStage.Value, __instance.daysUntilMature.Value);

    if (_animStepsCompleted == 0 && _animDirection == 0)
    {
      return;
    }

    if (__instance.stump.Value || __state.stage < FruitTreeFullGrown)
    {
      return;
    }

    TryAdvanceAnimation();
    int treeSteps = GetTreeStepsCompleted(__instance.Tile);
    int displayStage = GetFruitTreeDisplayStage(__state.stage, treeSteps);
    SetLocalOnly(__instance.growthStage, displayStage);
    SetLocalOnly(__instance.daysUntilMature, FruitTree.GrowthStageToDaysUntilMature(displayStage));
  }

  private static bool FruitTree_ToolAction_Prefix(FruitTree __instance)
  {
    if (
      !IsFullyVisible
      && !__instance.stump.Value
      && __instance.growthStage.Value >= FruitTreeFullGrown
    )
    {
      return false;
    }

    return true;
  }

  private static void FruitTree_Draw_Postfix(FruitTree __instance, (int stage, int days) __state)
  {
    SetLocalOnly(__instance.growthStage, __state.stage);
    SetLocalOnly(__instance.daysUntilMature, __state.days);

    if (IsFullyHidden && !__instance.stump.Value && __state.stage >= FruitTreeFullGrown)
    {
      TrySpawnIdleSparkle(__instance.Tile);
    }
  }

  private static void FruitTree_Draw_Finalizer(FruitTree __instance, (int stage, int days) __state)
  {
    SetLocalOnly(__instance.growthStage, __state.stage);
    SetLocalOnly(__instance.daysUntilMature, __state.days);
  }
  #endregion

  #region HUD banner
  private const int BannerSparkleSize = 64;
  private const float BannerSparkleScale = 0.5f;
  private static int _bannerSparkleDrawSize = (int)(BannerSparkleSize * BannerSparkleScale);

  private static int GetBannerSparkleFrame()
  {
    int ticksPerFrame = (int)(SparkleFrameMs / 16.67);
    return (int)(Game1.ticks / ticksPerFrame) % SparkleFrameCount;
  }

  /// <summary>Draw a centered top-screen banner when trees are fully hidden.</summary>
  public static void DrawHiddenBanner(SpriteBatch batch, string keybindName)
  {
    if (!IsFullyHidden)
    {
      return;
    }

    string text = I18n.HUD_TreesHidden(keybind: keybindName);
    Vector2 textSize = Game1.smallFont.MeasureString(text);

    float scale = 4f;
    int padding = (int)(5 * scale);
    int sparkleGap = 4;
    int contentWidth = _bannerSparkleDrawSize + sparkleGap + (int)textSize.X;
    int boxWidth = contentWidth + padding * 2;
    int boxHeight = (int)textSize.Y + padding * 2;
    int x = (Game1.uiViewport.Width - boxWidth) / 2;
    int y = 108;

    var dest = new Rectangle(x, y, boxWidth, boxHeight);
    NineSlice.Draw(batch, dest, scale, 1f);

    // Sparkle icon to the left of text, vertically centered
    int frame = GetBannerSparkleFrame();
    var sparkleSrc = new Rectangle(
      frame * BannerSparkleSize,
      0,
      BannerSparkleSize,
      BannerSparkleSize
    );
    int sparkleX = x + padding;
    int sparkleY = y + padding + ((int)textSize.Y - _bannerSparkleDrawSize) / 2;
    var sparkleDest = new Rectangle(
      sparkleX,
      sparkleY,
      _bannerSparkleDrawSize,
      _bannerSparkleDrawSize
    );
    // Shadow layers at smallFont-style offsets (-2, 2)
    Color shadowColor = Color.Black * 0.5f;
    var shadowOffsets = new Vector2[] { new(-2, 2), new(0, 2), new(-2, 0) };
    foreach (Vector2 offset in shadowOffsets)
    {
      var shadowDest = new Rectangle(
        sparkleDest.X + (int)offset.X,
        sparkleDest.Y + (int)offset.Y,
        sparkleDest.Width,
        sparkleDest.Height
      );
      batch.Draw(
        _sparkleTexture,
        shadowDest,
        sparkleSrc,
        shadowColor,
        0f,
        Vector2.Zero,
        SpriteEffects.None,
        0.99f
      );
    }

    batch.Draw(
      _sparkleTexture,
      sparkleDest,
      sparkleSrc,
      new Color(120, 230, 100),
      0f,
      Vector2.Zero,
      SpriteEffects.None,
      1f
    );

    Vector2 textPos = new(sparkleX + _bannerSparkleDrawSize + sparkleGap, y + padding + 2);
    Utility.drawTextWithShadow(batch, text, Game1.smallFont, textPos, Game1.textColor);
  }
  #endregion

  #region Idle sparkle effect
  // Custom sparkle: 6 frames, 64x64 each
  private const float SparkleFrameMs = 80f;
  private const int SparkleFrameCount = 6;
  private const double SparkleIntervalMs = SparkleFrameMs * SparkleFrameCount;

  // Global frame counter to space out sparkle spawns
  private static long _lastSparkleFrameTick = -1;
  private static int _sparkleTickCounter;

  private static void SpawnIdleSparkle(GameLocation location, Vector2 tile)
  {
    float rotation = (float)(Game1.random.NextDouble() * Math.PI * 2.0);
    Vector2 position = tile * 64f;

    // Shadow sprite at smallFont-style offset (-2, 2)
    location.temporarySprites.Add(
      new TemporaryAnimatedSprite(
        null,
        new Rectangle(0, 0, 64, 64),
        SparkleFrameMs,
        SparkleFrameCount,
        0,
        position + new Vector2(-2f, 2f),
        flicker: false,
        flipped: false
      )
      {
        texture = _sparkleTexture,
        color = Color.Black * 0.5f,
        layerDepth = 0.99f,
        scale = 1f,
        rotation = rotation,
      }
    );

    location.temporarySprites.Add(
      new TemporaryAnimatedSprite(
        null,
        new Rectangle(0, 0, 64, 64),
        SparkleFrameMs,
        SparkleFrameCount,
        0,
        position,
        flicker: false,
        flipped: false
      )
      {
        texture = _sparkleTexture,
        color = new Color(120, 230, 100),
        layerDepth = 1f,
        scale = 1f,
        rotation = rotation,
      }
    );
  }

  private static void TrySpawnIdleSparkle(Vector2 tile)
  {
    // Only evaluate once per frame
    if (_lastSparkleFrameTick != Game1.ticks)
    {
      _lastSparkleFrameTick = Game1.ticks;
      _sparkleTickCounter++;
    }

    // Spawn a new sparkle every ~SparkleIntervalMs worth of frames (~480ms at 60fps = ~29 ticks)
    int tickInterval = (int)(SparkleIntervalMs / 16.67);
    if (_sparkleTickCounter % tickInterval != 0)
    {
      return;
    }

    GameLocation location = Game1.currentLocation;
    if (location == null)
    {
      return;
    }

    SpawnIdleSparkle(location, tile);
  }
  #endregion
}
