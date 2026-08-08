using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace UIInfoSuite2Alt.Infrastructure;

/// <summary>
///   Android draws the date box and buff bar through a sprite batch scaled by the "Date box size"
///   option, so their bounds are in unscaled space. Anchoring to them needs the same scale. Inert on PC.
/// </summary>
internal static class AndroidHud
{
  private static Func<float>? _dateTimeScale;
  private static bool _resolved;
  private static FieldInfo? _toolbarPressed;
  private static bool _toolbarPressedResolved;
  private static readonly List<Rectangle> HudBounds = [];

  public static bool IsAndroid => Constants.TargetPlatform == GamePlatform.Android;

  /// <summary>The "Date box size" option, 0.5-2.0. Always 1 on PC.</summary>
  public static float Scale
  {
    get
    {
      if (!IsAndroid)
      {
        return 1f;
      }

      float scale = ResolveScaleGetter()?.Invoke() ?? 1f;
      return scale > 0f ? scale : 1f;
    }
  }

  /// <summary>Cursor in the scaled HUD's space, for bounds set during a scaled draw.</summary>
  public static int MouseX => (int)(Game1.getMouseX() / Scale);

  /// <inheritdoc cref="MouseX" />
  public static int MouseY => (int)(Game1.getMouseY() / Scale);

  /// <summary>
  ///   Whether the cursor is over <paramref name="component" />. Android needs a held touch, like the
  ///   buff icons, and claims it so tap-to-move ignores it. Plain hover on PC.
  /// </summary>
  public static bool IsHovered(ClickableComponent? component)
  {
    if (component == null)
    {
      return false;
    }

    if (!IsAndroid)
    {
      return component.containsPoint(Game1.getMouseX(), Game1.getMouseY());
    }

    // containsPoint also gates on visible, so keep that here
    return component.visible && IsHovered(component.bounds);
  }

  /// <inheritdoc cref="IsHovered(ClickableComponent)" />
  public static bool IsHovered(Rectangle bounds)
  {
    if (!IsAndroid)
    {
      return bounds.Contains(Game1.getMouseX(), Game1.getMouseY());
    }

    HudBounds.Add(bounds);

    if (!IsTouchHeld || !bounds.Contains(MouseX, MouseY))
    {
      return false;
    }

    ClaimTouch();
    return true;
  }

  /// <summary>
  ///   Claims a touch on any HUD element drawn last frame, so tap-to-move ignores it. Must run before
  ///   the game's update, which picks the tap target on the press itself.
  /// </summary>
  public static void ClaimTouchOverHud()
  {
    if (!IsAndroid)
    {
      return;
    }

    if (IsTouchHeld)
    {
      int x = MouseX;
      int y = MouseY;
      foreach (Rectangle bounds in HudBounds)
      {
        if (bounds.Contains(x, y))
        {
          ClaimTouch();
          break;
        }
      }
    }

    HudBounds.Clear();
  }

  private static bool IsTouchHeld => Game1.input.GetMouseState().LeftButton == ButtonState.Pressed;

  /// <summary>
  ///   Marks the touch as taken by the HUD so TapToMove ignores it. The game clears the flag on
  ///   release, in VirtualJoypad.releaseLeftClick.
  /// </summary>
  private static void ClaimTouch()
  {
    if (!_toolbarPressedResolved)
    {
      _toolbarPressedResolved = true;

      // Android-only, so it cannot be referenced directly
      _toolbarPressed = typeof(Toolbar).GetField(
        "toolbarPressed",
        BindingFlags.Public | BindingFlags.Static
      );

      if (_toolbarPressed == null)
      {
        ModEntry.MonitorObject.Log(
          "AndroidHud: Toolbar.toolbarPressed not found, HUD taps will move the player",
          LogLevel.Warn
        );
      }
    }

    _toolbarPressed?.SetValue(null, true);
  }

  /// <summary>Restarts the batch under the date box scale. Only call <see cref="End" /> when true.</summary>
  public static bool Begin(SpriteBatch b)
  {
    float scale = Scale;
    if (!IsAndroid || Math.Abs(scale - 1f) < 0.001f)
    {
      return false;
    }

    b.End();
    b.Begin(
      SpriteSortMode.Deferred,
      BlendState.AlphaBlend,
      SamplerState.PointClamp,
      null,
      null,
      null,
      Matrix.CreateScale(scale)
    );
    return true;
  }

  /// <summary>Restores the game's mobile HUD batch state.</summary>
  public static void End(SpriteBatch b)
  {
    b.End();
    b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
  }

  private static Func<float>? ResolveScaleGetter()
  {
    if (_resolved)
    {
      return _dateTimeScale;
    }

    _resolved = true;

    // Android-only, so it cannot be referenced directly from a desktop build
    MethodInfo? getter = typeof(Game1)
      .GetProperty("DateTimeScale", BindingFlags.Public | BindingFlags.Static)
      ?.GetGetMethod();

    if (getter == null)
    {
      ModEntry.MonitorObject.Log(
        "AndroidHud: Game1.DateTimeScale not found, HUD scaling disabled",
        LogLevel.Warn
      );
      return null;
    }

    _dateTimeScale = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), getter);
    return _dateTimeScale;
  }
}
