using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Patches;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowBuffTimers : IDisposable
{
  private const int ColonPadding = 2; // padding on each side of the colon dots
  private const int ColonDotGap = 4; // pixel width of the colon region (dot + inner spacing)
  private static readonly Color ShadowColor = Color.Black * 0.35f;
  private static readonly Color DigitColor = Color.White * 0.8f;
  private static readonly Color DotColor = Color.White * 0.8f;
  private static readonly Color FadeColor = new(255, 75, 75, 255);
  private static readonly Color FadingDigitColor = FadeColor * 0.9f;
  private static readonly Color FadingDotColor = FadeColor * 0.9f;

  private readonly IModHelper _helper;
  private readonly PerScreen<HashSet<string>> _previousBuffIds = new(() => []);
  private readonly PerScreen<bool> _playExpireSound = new();

  public ShowBuffTimers(IModHelper helper)
  {
    _helper = helper;
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool showBuffTimers)
  {
    _helper.Events.Display.RenderedHud -= OnRenderedHud;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;

    if (showBuffTimers)
    {
      _helper.Events.Display.RenderedHud += OnRenderedHud;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    }
  }

  public void ToggleExpireSound(bool playExpireSound)
  {
    _playExpireSound.Value = playExpireSound;
  }

  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (!e.IsMultipleOf(15))
    {
      return;
    }

    if (!Context.IsWorldReady)
    {
      _previousBuffIds.Value.Clear();
      return;
    }

    // Play sound if any previously-tracked buff is no longer applied
    if (_playExpireSound.Value && _previousBuffIds.Value.Count > 0)
    {
      foreach (string id in _previousBuffIds.Value)
      {
        if (!Game1.player.buffs.AppliedBuffs.ContainsKey(id))
        {
          SoundHelper.Play(Sounds.BuffExpired);
          break; // one sound even if multiple buffs expire simultaneously
        }
      }
    }

    _previousBuffIds.Value.Clear();
    foreach (KeyValuePair<string, Buff> pair in Game1.player.buffs.AppliedBuffs)
    {
      // Only track non-permanent buffs
      if (pair.Value.millisecondsDuration != -2)
      {
        _previousBuffIds.Value.Add(pair.Key);
      }
    }
  }

  private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally())
    {
      return;
    }

    // No icons drawn when buff display is hidden, so timers have nothing to anchor to.
    if (BuffIconSizePatch.Mode == BuffIconSizePatch.ModeHidden)
    {
      return;
    }

    Dictionary<ClickableTextureComponent, Buff>? buffs = GetBuffComponents();
    if (buffs == null || buffs.Count == 0)
    {
      return;
    }

    SpriteBatch b = e.SpriteBatch;

    // In Smaller mode the icons are half size, so a full "M:SS" overflows; use a compact form.
    bool compact = BuffIconSizePatch.Mode == BuffIconSizePatch.ModeSmaller;

    // Android buff icon bounds are in the date box's scaled space
    bool scaled = AndroidHud.Begin(b);

    foreach (KeyValuePair<ClickableTextureComponent, Buff> pair in buffs)
    {
      Buff buff = pair.Value;

      // Skip permanent buffs (duration -2)
      if (buff.millisecondsDuration == -2)
      {
        continue;
      }

      ClickableTextureComponent icon = pair.Key;
      int totalSeconds = Math.Max(0, buff.millisecondsDuration / 1000);
      int minutes = totalSeconds / 60;
      int seconds = totalSeconds % 60;

      int totalWidth = GetTimerWidth(minutes, seconds, compact);

      // Center below the buff icon, nudged down 2px
      float x = icon.bounds.X + icon.bounds.Width / 2f - totalWidth / 2f;
      float y = icon.bounds.Y + icon.bounds.Height + 2;

      float alpha =
        buff.displayAlphaTimer > 0f
          ? (float)(Math.Cos(buff.displayAlphaTimer / 100f) + 3.0) / 4f
          : 1f;

      bool isFading = buff.displayAlphaTimer > 0f;

      DrawTimer(b, minutes, seconds, new Vector2(x, y), alpha, isFading, compact);
    }

    if (scaled)
    {
      AndroidHud.End(b);
    }
  }

  /// <summary>Draws a timer as "M:SS" using the game's tiny digit sprites with a colon separator.</summary>
  private static void DrawTimer(
    SpriteBatch b,
    int minutes,
    int seconds,
    Vector2 position,
    float alpha,
    bool isFading,
    bool compact
  )
  {
    float xOffset = 0;
    int scaledDigitStep = Tools.TinyDigitStep;
    Color digitColor = (isFading ? FadingDigitColor : DigitColor) * alpha;
    Color dotColor = (isFading ? FadingDotColor : DotColor) * alpha;
    Color shadowColor = ShadowColor * alpha;

    if (compact)
    {
      // Color-coded number: blue for minutes, yellow for seconds, red pulse near expiry.
      int value = minutes >= 1 ? minutes : seconds;
      Color compactColor = isFading
        ? FadingDigitColor * alpha
        : (minutes >= 1 ? Tools.TooltipBuffBlue : Tools.TooltipBuffYellow) * alpha;

      Tools.DrawTinyDigits(
        b,
        value,
        position,
        ref xOffset,
        scaledDigitStep,
        compactColor,
        shadowColor
      );

      return;
    }

    // Draw minutes
    Tools.DrawTinyDigits(
      b,
      minutes,
      position,
      ref xOffset,
      scaledDigitStep,
      digitColor,
      shadowColor
    );

    // Draw colon (two dots) with padding
    xOffset += ColonPadding;
    Tools.DrawTinyColon(b, position, xOffset, ColonDotGap, dotColor, shadowColor);
    xOffset += ColonDotGap + ColonPadding;

    // Draw seconds (always 2 digits)
    Tools.DrawTinyDigit(
      b,
      seconds / 10,
      position,
      ref xOffset,
      scaledDigitStep,
      digitColor,
      shadowColor
    );
    Tools.DrawTinyDigit(
      b,
      seconds % 10,
      position,
      ref xOffset,
      scaledDigitStep,
      digitColor,
      shadowColor
    );
  }

  private static int GetTimerWidth(int minutes, int seconds, bool compact)
  {
    int digitStep = Tools.TinyDigitStep;

    if (compact)
    {
      // Just the minute or second digits (color encodes which).
      return DigitCount(minutes >= 1 ? minutes : seconds) * digitStep;
    }

    int colonStep = ColonPadding + ColonDotGap + ColonPadding;
    const int secondDigits = 2;

    return (DigitCount(minutes) + secondDigits) * digitStep + colonStep;
  }

  private static int DigitCount(int n)
  {
    return n < 10 ? 1 : (int)Math.Floor(Math.Log10(n)) + 1;
  }

  private Dictionary<ClickableTextureComponent, Buff>? GetBuffComponents()
  {
    try
    {
      return _helper
        .Reflection.GetField<Dictionary<ClickableTextureComponent, Buff>>(
          Game1.buffsDisplay,
          "buffs"
        )
        .GetValue();
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"ShowBuffTimers: failed to reflect buffs dictionary, {ex.Message}",
        LogLevel.Trace
      );
      return null;
    }
  }
}
