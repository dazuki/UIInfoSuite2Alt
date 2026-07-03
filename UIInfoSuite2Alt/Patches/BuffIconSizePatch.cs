using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.Menus;

namespace UIInfoSuite2Alt.Patches;

/// <summary>
/// Resizes and re-packs the vanilla buff icon display, or hides it entirely.
/// Smaller mode shrinks each icon to half size and tightens the grid; Hidden mode
/// suppresses the whole buff display draw.
/// </summary>
internal static class BuffIconSizePatch
{
  public const int ModeNormal = 0;
  public const int ModeSmaller = 1;
  public const int ModeHidden = 2;

  private const float SmallerScale = 2f; // vanilla is 4f
  private const int SmallerCell = 32; // vanilla cell is 64
  private const int SmallerBuffer = 4; // vanilla buffer is 8
  private const int SmallerPerRow = 10; // vanilla wraps at 5

  public static int Mode { get; private set; } = ModeNormal;

  public static void SetMode(int mode)
  {
    Mode = mode;
    if (Game1.buffsDisplay != null)
    {
      Game1.buffsDisplay.dirty = true; // force re-layout at the new size
    }
  }

  public static void Initialize(Harmony harmony)
  {
    // Degrade to the vanilla display rather than crashing Entry if signatures ever change.
    try
    {
      MethodInfo? resetIcons = AccessTools.Method(typeof(BuffsDisplay), "resetIcons");
      MethodInfo? draw = AccessTools.Method(
        typeof(BuffsDisplay),
        nameof(BuffsDisplay.draw),
        new[] { typeof(SpriteBatch) }
      );

      if (resetIcons == null || draw == null)
      {
        ModEntry.MonitorObject.Log(
          $"BuffIconSizePatch: target methods not found (resetIcons={resetIcons != null}, draw={draw != null}); buff icon sizing disabled",
          LogLevel.Warn
        );
        return;
      }

      harmony.Patch(
        original: resetIcons,
        postfix: new HarmonyMethod(typeof(BuffIconSizePatch), nameof(AfterResetIcons))
      );
      harmony.Patch(
        original: draw,
        prefix: new HarmonyMethod(typeof(BuffIconSizePatch), nameof(BeforeDraw))
      );

      ModEntry.MonitorObject.Log("BuffIconSizePatch: initialized", LogLevel.Trace);
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"BuffIconSizePatch: failed to initialize; buff icon sizing disabled, {ex.Message}",
        LogLevel.Warn
      );
    }
  }

  private static bool BeforeDraw()
  {
    return Mode != ModeHidden;
  }

  private static void AfterResetIcons(
    BuffsDisplay __instance,
    Dictionary<ClickableTextureComponent, Buff> ___buffs
  )
  {
    if (Mode != ModeSmaller || ___buffs == null || ___buffs.Count == 0)
    {
      return;
    }

    // Runs inside the game's update loop; never let a resize hiccup break vanilla rendering.
    try
    {
      foreach (ClickableTextureComponent icon in ___buffs.Keys)
      {
        // Both must change: baseScale drives the centering offset in ClickableTextureComponent.draw.
        icon.baseScale = SmallerScale;
        icon.scale = SmallerScale;
      }

      // Keep the block's right edge anchored where vanilla puts it (top-right of screen).
      int rightEdge = __instance.xPositionOnScreen + __instance.width / 64 * 64 - 8;
      int rectX = rightEdge + SmallerBuffer - (SmallerPerRow - 1) * SmallerCell;
      __instance.arrangeTheseComponentsInThisRectangle(
        rectX,
        __instance.yPositionOnScreen,
        SmallerPerRow - 1, // arrange wraps when index exceeds this, so N-1 yields N per row
        SmallerCell,
        SmallerCell,
        SmallerBuffer,
        true
      );
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.LogOnce(
        $"BuffIconSizePatch: failed to resize buff icons, {ex.Message}",
        LogLevel.Warn
      );
    }
  }
}
