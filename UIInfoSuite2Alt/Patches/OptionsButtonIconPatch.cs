using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace UIInfoSuite2Alt.Patches;

/// <summary>
///   Android draws OptionsButton with a hardcoded null icon, and its draw cannot be overridden from a
///   desktop build because Android drops the IClickableMenu parameter. So the mod's own entry in the
///   native Options page is drawn here instead. Android only.
/// </summary>
internal static class OptionsButtonIconPatch
{
  private static readonly Rectangle BoxSource = new(256, 256, 10, 10);
  private const int IconSize = 16;
  private const float MaxIconScale = 3f;
  private const int EdgePadding = 16;
  private const int IconGap = 12;

  /// <summary>MeasureString overstates the height above the glyphs; the game's own buttons nudge by 3.</summary>
  private const int TextBaselineNudge = 3;

  /// <summary>Extra button width the icon needs beside the label.</summary>
  internal const int IconWidth = (int)(IconSize * MaxIconScale) + IconGap;

  /// <summary>The injected button, drawn with an icon. Every other OptionsButton is left alone.</summary>
  internal static OptionsButton? Target { get; set; }

  internal static Texture2D? Icon { get; set; }

  internal static void Initialize(Harmony harmony)
  {
    // Overloaded, so the parameter types are required to pick one
    MethodInfo? draw = AccessTools.Method(
      typeof(OptionsButton),
      "draw",
      [typeof(SpriteBatch), typeof(int), typeof(int)]
    );

    if (draw == null)
    {
      ModEntry.MonitorObject.Log(
        "OptionsButtonIconPatch: OptionsButton.draw not found, options entry will have no icon",
        LogLevel.Warn
      );
      return;
    }

    harmony.Patch(
      draw,
      prefix: new HarmonyMethod(typeof(OptionsButtonIconPatch), nameof(Draw_Prefix))
    );
  }

  private static bool Draw_Prefix(OptionsButton __instance, SpriteBatch b, int slotX, int slotY)
  {
    if (Icon == null || !ReferenceEquals(__instance, Target))
    {
      return true;
    }

    Rectangle bounds = __instance.bounds;
    int x = slotX + bounds.X;
    int y = slotY + bounds.Y;

    IClickableMenu.drawTextureBox(
      b,
      Game1.mouseCursors,
      BoxSource,
      x,
      y,
      bounds.Width,
      bounds.Height,
      Color.White,
      Game1.pixelZoom
    );

    // Sized off the button the way drawTextureBoxWithIconAndText does
    float iconScale = Math.Min(MaxIconScale, bounds.Height * 3f / 4f / IconSize);
    float iconPixels = IconSize * iconScale;

    b.Draw(
      Icon,
      new Vector2(x + EdgePadding, y + (bounds.Height - iconPixels) / 2f),
      new Rectangle(0, 0, IconSize, IconSize),
      Color.White,
      0f,
      Vector2.Zero,
      iconScale,
      SpriteEffects.None,
      0.08f
    );

    // Plain and unshadowed, as the other buttons draw it (they pass bold: false)
    Vector2 textSize = Game1.dialogueFont.MeasureString(__instance.label);
    b.DrawString(
      Game1.dialogueFont,
      __instance.label,
      new Vector2(
        x + EdgePadding + iconPixels + IconGap,
        y + (bounds.Height - textSize.Y) / 2f + TextBaselineNudge
      ),
      Game1.textColor,
      0f,
      Vector2.Zero,
      1f,
      SpriteEffects.None,
      0.08f
    );

    return false;
  }
}
