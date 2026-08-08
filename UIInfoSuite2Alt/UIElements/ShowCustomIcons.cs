using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowCustomIcons : IDisposable
{
  private const int MaxVisibleIcons = 15;

  private readonly IModHelper _helper;
  private readonly PerScreen<Dictionary<string, CustomIconData>> _activeIcons = new(() => []);
  private readonly PerScreen<Dictionary<string, ClickableTextureComponent>> _iconComponents = new(
    () =>
      []
  );
  private readonly PerScreen<bool> _needsReload = new(() => true);

  public ShowCustomIcons(IModHelper helper)
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
    _helper.Events.GameLoop.DayStarted -= OnDayStarted;
    _helper.Events.Content.AssetsInvalidated -= OnAssetsInvalidated;

    if (enabled)
    {
      _needsReload.Value = true;
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.GameLoop.DayStarted += OnDayStarted;
      _helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;
    }
  }

  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    _needsReload.Value = true;
  }

  private void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
  {
    foreach (IAssetName name in e.NamesWithoutLocale)
    {
      if (name.IsEquivalentTo(ModEntry.CustomIconsAssetName))
      {
        _needsReload.Value = true;
        break;
      }
    }
  }

  private void ReloadData()
  {
    _activeIcons.Value.Clear();
    _iconComponents.Value.Clear();

    Dictionary<string, CustomIconData> data;
    try
    {
      data = _helper.GameContent.Load<Dictionary<string, CustomIconData>>(
        ModEntry.CustomIconsAssetName
      );
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"ShowCustomIcons: failed to load custom icons asset, {ex.Message}",
        LogLevel.Error
      );
      _needsReload.Value = false;
      return;
    }

    foreach ((string key, CustomIconData iconData) in data)
    {
      if (string.IsNullOrWhiteSpace(iconData.Texture))
      {
        ModEntry.MonitorObject.LogOnce(
          $"ShowCustomIcons: icon '{key}' has no texture, skipping",
          LogLevel.Warn
        );
        continue;
      }

      _activeIcons.Value[key] = iconData;
    }

    _needsReload.Value = false;

    if (_activeIcons.Value.Count > 0)
    {
      ModEntry.MonitorObject.Log(
        $"ShowCustomIcons: loaded custom icons, count={_activeIcons.Value.Count}, keys=[{string.Join(", ", _activeIcons.Value.Keys)}]",
        LogLevel.Trace
      );
    }
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally())
    {
      return;
    }

    if (_needsReload.Value)
    {
      ReloadData();
    }

    if (_activeIcons.Value.Count == 0)
    {
      return;
    }

    int count = 0;
    foreach ((string key, CustomIconData iconData) in _activeIcons.Value)
    {
      if (count >= MaxVisibleIcons)
      {
        break;
      }

      string capturedKey = key;
      CustomIconData captured = iconData;

      IconHandler.Handler.EnqueueIcon(
        "CustomIcons",
        (batch, pos) => DrawIcon(batch, pos, capturedKey, captured),
        string.IsNullOrEmpty(captured.HoverText)
          ? null
          : batch => DrawHover(batch, capturedKey, captured.HoverText!)
      );

      count++;
    }
  }

  private void DrawIcon(SpriteBatch batch, Point pos, string key, CustomIconData iconData)
  {
    Texture2D texture;
    try
    {
      texture = _helper.GameContent.Load<Texture2D>(iconData.Texture);
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.LogOnce(
        $"ShowCustomIcons: failed to load texture '{iconData.Texture}' for icon '{key}', {ex.Message}",
        LogLevel.Warn
      );
      _iconComponents.Value.Remove(key);
      return;
    }

    // Draw at 40x40 to match weather/bookseller icon positioning
    const int drawnSize = 40;
    float scaleX = drawnSize / (float)iconData.SourceRect.Width;
    float scaleY = drawnSize / (float)iconData.SourceRect.Height;
    float scale = Math.Min(scaleX, scaleY);
    int drawWidth = (int)(iconData.SourceRect.Width * scale);
    int drawHeight = (int)(iconData.SourceRect.Height * scale);

    batch.Draw(
      texture,
      new Vector2(pos.X, pos.Y),
      iconData.SourceRect,
      Color.White,
      0f,
      Vector2.Zero,
      scale,
      SpriteEffects.None,
      1f
    );

    if (!_iconComponents.Value.TryGetValue(key, out ClickableTextureComponent? comp))
    {
      comp = new ClickableTextureComponent(Rectangle.Empty, null, Rectangle.Empty, 1f);
      _iconComponents.Value[key] = comp;
    }

    comp.bounds = new Rectangle(pos.X, pos.Y, drawWidth, drawHeight);
  }

  private void DrawHover(SpriteBatch batch, string key, string hoverText)
  {
    if (
      _iconComponents.Value.TryGetValue(key, out ClickableTextureComponent? comp)
      && comp.containsPoint(AndroidHud.MouseX, AndroidHud.MouseY)
    )
    {
      IClickableMenu.drawHoverText(batch, hoverText, Game1.smallFont);
    }
  }
}
