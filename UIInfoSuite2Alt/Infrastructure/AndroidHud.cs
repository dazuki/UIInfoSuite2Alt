using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace UIInfoSuite2Alt.Infrastructure;

/// <summary>
///   Android draws the date box and buff bar through a sprite batch scaled by the "Date box size"
///   option, so their bounds are in unscaled space. Anchoring to them needs the same scale. Inert on PC.
/// </summary>
internal static class AndroidHud
{
  private static Func<float>? _dateTimeScale;
  private static bool _resolved;

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
