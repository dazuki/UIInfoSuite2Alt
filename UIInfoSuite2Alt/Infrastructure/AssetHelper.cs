using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace UIInfoSuite2Alt.Infrastructure;

/// <summary>
/// Safe asset loading with fallback behavior. Textures fall back to a 12x12
/// texture from <see cref="Game1.mouseCursors"/> and Sounds fall back to null (silently skipped on play).
/// </summary>
public static class AssetHelper
{
  private static readonly Rectangle ErrorIconSource = new(322, 498, 12, 12);
  private static Texture2D? _fallbackTexture;

  /// <summary>Error icon texture extracted from Cursors(<see cref="ErrorIconSource"/>) as fallback texture.</summary>
  public static Texture2D FallbackTexture
  {
    get
    {
      if (_fallbackTexture is null || _fallbackTexture.IsDisposed)
      {
        var source = Game1.mouseCursors;
        var pixels = new Color[ErrorIconSource.Width * ErrorIconSource.Height];
        source.GetData(0, ErrorIconSource, pixels, 0, pixels.Length);
        _fallbackTexture = new Texture2D(
          Game1.graphics.GraphicsDevice,
          ErrorIconSource.Width,
          ErrorIconSource.Height
        );
        _fallbackTexture.SetData(pixels);
      }

      return _fallbackTexture;
    }
  }

  /// <summary>Returns true if the texture is the shared <see cref="_fallbackTexture"/>(caller can skip drawing).</summary>
  public static bool IsFallback(Texture2D? texture)
  {
    return texture is null || ReferenceEquals(texture, _fallbackTexture);
  }

  /// <summary>
  /// Try to load any asset via a caller-provided loader function.
  /// Returns null on failure, logs a warning.
  /// </summary>
  public static T? TryLoadAsset<T>(string assetName, Func<T> loader)
    where T : class
  {
    try
    {
      return loader();
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"AssetHelper: failed to load asset '{assetName}', {ex.Message}",
        LogLevel.Error
      );
      return null;
    }
  }

  /// <summary>
  /// Try to load a texture. Returns the shared <see cref="FallbackTexture"/> on failure.
  /// Safe to use everywhere(callers never get null).
  /// </summary>
  public static Texture2D TryLoadTexture(string assetName, Func<Texture2D> loader)
  {
    return TryLoadAsset(assetName, loader) ?? FallbackTexture;
  }

  /// <summary>Try to load a texture via <see cref="IModContentHelper.Load{T}"/>.</summary>
  public static Texture2D TryLoadTexture(IModHelper helper, string assetPath)
  {
    return TryLoadTexture(assetPath, () => helper.ModContent.Load<Texture2D>(assetPath));
  }

  /// <summary>Try to load a texture via <see cref="Texture2D.FromFile"/>.</summary>
  public static Texture2D TryLoadTextureFromFile(string filePath)
  {
    return TryLoadTexture(
      filePath,
      () => Texture2D.FromFile(Game1.graphics.GraphicsDevice, filePath)
    );
  }

  /// <summary>Try to load a sound effect from a file path. Returns null on failure.</summary>
  public static SoundEffect? TryLoadSound(string filePath)
  {
    try
    {
      bool isOgg = Path.GetExtension(filePath).Equals(".ogg", StringComparison.OrdinalIgnoreCase);
      using var stream = new FileStream(filePath, FileMode.Open);
      return SoundEffect.FromStream(stream, isOgg);
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"AssetHelper: failed to load asset '{Path.GetFileName(filePath)}', {ex.Message}",
        LogLevel.Error
      );
      return null;
    }
  }
}
