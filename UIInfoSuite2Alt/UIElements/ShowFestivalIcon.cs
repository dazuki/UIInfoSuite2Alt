using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData;
using StardewValley.Menus;
using StardewValley.TokenizableStrings;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowFestivalIcon : IDisposable
{
  #region Properties
  private enum FestivalType
  {
    None,
    Regular,
    Passive,
    FishingDerby,
  }

  private static readonly HashSet<string> FishingDerbyIds = ["TroutDerby", "SquidFest"];

  private readonly PerScreen<FestivalType> _todayType = new();
  private readonly PerScreen<string> _todayHoverText = new();
  private readonly PerScreen<FestivalType> _tomorrowType = new();
  private readonly PerScreen<string> _tomorrowHoverText = new();

  // Deferred festival time loading — avoids content loads during DayStarted
  // which can trigger Content Patcher token re-evaluation and cascading
  // texture invalidations that break mods like CP Animations.
  private readonly PerScreen<List<(string key, bool isToday)>> _pendingRegularFestivals = new(() =>
    []
  );

  private Texture2D? _billboardTexture;

  // Flag icon for regular festivals (from Billboard spritesheet)
  private static readonly Rectangle FlagSourceRect = new(1, 399, 13, 11);

  // Purple star icon for passive festivals (from Cursors spritesheet)
  private static readonly Rectangle StarSourceRect = new(346, 392, 8, 8);

  // Fishing derby icon (from Cursors_1_6 spritesheet)
  private static readonly Rectangle FishingDerbySourceRect = new(103, 2, 10, 11);

  private readonly PerScreen<ClickableTextureComponent> _todayIcon = new(() =>
    new ClickableTextureComponent(new Rectangle(0, 0, 40, 40), null, FlagSourceRect, 40 / 13f)
  );

  private readonly PerScreen<ClickableTextureComponent> _tomorrowIcon = new(() =>
    new ClickableTextureComponent(new Rectangle(0, 0, 40, 40), null, FlagSourceRect, 40 / 13f)
  );

  private readonly IModHelper _helper;
  #endregion


  #region Life cycle
  public ShowFestivalIcon(IModHelper helper)
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
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;

    if (enabled)
    {
      CheckForFestival();
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.GameLoop.DayStarted += OnDayStarted;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    }
  }
  #endregion


  #region Logic
  private void CheckForFestival()
  {
    _todayType.Value = FestivalType.None;
    _todayHoverText.Value = "";
    _tomorrowType.Value = FestivalType.None;
    _tomorrowHoverText.Value = "";
    _pendingRegularFestivals.Value.Clear();

    // Collect all festivals for today
    List<(string name, string time)> todayFestivals = [];
    FestivalType todayIconType = FestivalType.None;

    if (Utility.isFestivalDay())
    {
      todayIconType = FestivalType.Regular;
      Dictionary<string, string> festivalDates = DataLoader.Festivals_FestivalDates(
        Game1.temporaryContent
      );
      string todayKey = $"{Utility.getSeasonKey(Game1.season)}{Game1.dayOfMonth}";
      string name = festivalDates.TryGetValue(todayKey, out string? n) ? n : todayKey;
      todayFestivals.Add((name, ""));
      _pendingRegularFestivals.Value.Add((todayKey, true));
    }

    foreach (
      (string id, PassiveFestivalData data) in GetActivePassiveFestivals(
        Game1.dayOfMonth,
        Game1.season
      )
    )
    {
      todayFestivals.Add((GetPassiveFestivalName(id, data), GetPassiveFestivalTime(data)));
      if (todayIconType == FestivalType.None)
      {
        todayIconType = FishingDerbyIds.Contains(id)
          ? FestivalType.FishingDerby
          : FestivalType.Passive;
      }
    }

    if (todayFestivals.Count > 0)
    {
      _todayType.Value = todayIconType;
      _todayHoverText.Value = string.Join(
        Environment.NewLine,
        todayFestivals.ConvertAll(f => FormatFestivalEntry(I18n.FestivalToday(), f.name, f.time))
      );
    }

    // Calculate tomorrow
    int tomorrowDay = Game1.dayOfMonth + 1;
    Season tomorrowSeason = Game1.season;

    if (tomorrowDay > 28)
    {
      tomorrowDay = 1;
      tomorrowSeason = tomorrowSeason switch
      {
        Season.Spring => Season.Summer,
        Season.Summer => Season.Fall,
        Season.Fall => Season.Winter,
        Season.Winter => Season.Spring,
        _ => tomorrowSeason,
      };
    }

    // Collect all festivals for tomorrow
    List<(string name, string time)> tomorrowFestivals = [];
    FestivalType tomorrowIconType = FestivalType.None;

    if (Utility.isFestivalDay(tomorrowDay, tomorrowSeason))
    {
      tomorrowIconType = FestivalType.Regular;
      Dictionary<string, string> festivalDates = DataLoader.Festivals_FestivalDates(
        Game1.temporaryContent
      );
      string festivalKey = $"{Utility.getSeasonKey(tomorrowSeason)}{tomorrowDay}";
      string name = festivalDates.TryGetValue(festivalKey, out string? n) ? n : festivalKey;
      tomorrowFestivals.Add((name, ""));
      _pendingRegularFestivals.Value.Add((festivalKey, false));
    }

    // Passive festivals — first day only for tomorrow
    foreach (
      (string id, PassiveFestivalData data) in GetPassiveFestivalsStartingOn(
        tomorrowDay,
        tomorrowSeason
      )
    )
    {
      tomorrowFestivals.Add((GetPassiveFestivalName(id, data), GetPassiveFestivalTime(data)));
      if (tomorrowIconType == FestivalType.None)
      {
        tomorrowIconType = FishingDerbyIds.Contains(id)
          ? FestivalType.FishingDerby
          : FestivalType.Passive;
      }
    }

    if (tomorrowFestivals.Count > 0)
    {
      _tomorrowType.Value = tomorrowIconType;
      _tomorrowHoverText.Value = string.Join(
        Environment.NewLine,
        tomorrowFestivals.ConvertAll(f =>
          FormatFestivalEntry(I18n.FestivalTomorrow(), f.name, f.time)
        )
      );
    }
  }

  /// <summary>Load festival times from data files, deferred to avoid breaking CP Animations etc.</summary>
  private void ResolvePendingFestivalTimes()
  {
    List<(string key, bool isToday)> pending = _pendingRegularFestivals.Value;
    if (pending.Count == 0)
    {
      return;
    }

    foreach ((string festivalKey, bool isToday) in pending)
    {
      string time = GetRegularFestivalTime(festivalKey);
      if (string.IsNullOrEmpty(time))
      {
        continue;
      }

      if (isToday && HasToday)
      {
        _todayHoverText.Value += Environment.NewLine + time;
      }
      else if (!isToday && HasTomorrow)
      {
        _tomorrowHoverText.Value += Environment.NewLine + time;
      }
    }
    pending.Clear();
  }

  // Get all active passive festivals for a given day (including mid-festival)
  private static IEnumerable<(string id, PassiveFestivalData data)> GetActivePassiveFestivals(
    int day,
    Season season
  )
  {
    Dictionary<string, PassiveFestivalData> allPassive = DataLoader.PassiveFestivals(Game1.content);

    foreach (KeyValuePair<string, PassiveFestivalData> entry in allPassive)
    {
      if (
        entry.Value.Season == season
        && day >= entry.Value.StartDay
        && day <= entry.Value.EndDay
        && GameStateQuery.CheckConditions(entry.Value.Condition)
      )
      {
        yield return (entry.Key, entry.Value);
      }
    }
  }

  // Get all passive festivals starting on a specific day (not mid-festival)
  private static IEnumerable<(string id, PassiveFestivalData data)> GetPassiveFestivalsStartingOn(
    int day,
    Season season
  )
  {
    Dictionary<string, PassiveFestivalData> allPassive = DataLoader.PassiveFestivals(Game1.content);

    foreach (KeyValuePair<string, PassiveFestivalData> entry in allPassive)
    {
      if (
        entry.Value.Season == season
        && entry.Value.StartDay == day
        && GameStateQuery.CheckConditions(entry.Value.Condition)
      )
      {
        yield return (entry.Key, entry.Value);
      }
    }
  }

  // Get display name from data, then localized strings, falling back to humanized ID
  private static string GetPassiveFestivalName(string festivalId, PassiveFestivalData data)
  {
    string parsed = TokenParser.ParseText(data.DisplayName);
    if (!string.IsNullOrWhiteSpace(parsed))
    {
      return parsed;
    }

    // Some passive festivals (TroutDerby, SquidFest) have empty DisplayName but store
    // their localized name in Strings/1_6_Strings using the festival ID as the key
    Dictionary<string, string> strings = Game1.content.Load<Dictionary<string, string>>(
      "Strings\\1_6_Strings"
    );
    if (
      strings.TryGetValue(festivalId, out string? localizedName)
      && !string.IsNullOrWhiteSpace(localizedName)
    )
    {
      return localizedName;
    }

    // Insert spaces before uppercase letters: "TroutDerby" → "Trout Derby"
    return System.Text.RegularExpressions.Regex.Replace(festivalId, "(?<=.)([A-Z])", " $1");
  }

  private static string GetRegularFestivalTime(string festivalKey)
  {
    try
    {
      // Load via Game1.temporaryContent (already used for FestivalDates in this method)
      // instead of Event.tryToLoadFestivalData which creates a static FestivalReadContentLoader
      // that persists for the session and may cause side effects with other mods' content patches.
      string assetName = "Data\\Festivals\\" + festivalKey;
      Dictionary<string, string> data = Game1.temporaryContent.Load<Dictionary<string, string>>(
        assetName
      );
      if (data.TryGetValue("conditions", out string? conditions))
      {
        string[] parts = conditions.Split('/');
        if (parts.Length >= 2)
        {
          string[] times = ArgUtility.SplitBySpace(parts[1]);
          if (
            ArgUtility.TryGetInt(times, 0, out int startTime, out _)
            && ArgUtility.TryGetInt(times, 1, out int endTime, out _)
          )
          {
            return string.Format(
              I18n.FestivalTimeRange(),
              Game1.getTimeOfDayString(startTime),
              Game1.getTimeOfDayString(endTime)
            );
          }
        }
      }
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"ShowFestivalIcon: failed to load festival data for '{festivalKey}', {ex.Message}",
        LogLevel.Trace
      );
    }
    return "";
  }

  private static string GetPassiveFestivalTime(PassiveFestivalData data)
  {
    if (data.StartTime > 0)
    {
      return string.Format(I18n.FestivalTimeFrom(), Game1.getTimeOfDayString(data.StartTime));
    }
    return "";
  }

  private static string FormatFestivalEntry(string format, string name, string time)
  {
    string entry = string.Format(format, name);
    if (!string.IsNullOrEmpty(time))
    {
      entry += Environment.NewLine + time;
    }
    return entry;
  }

  private bool HasToday => _todayType.Value != FestivalType.None;
  private bool HasTomorrow => _tomorrowType.Value != FestivalType.None;
  #endregion


  #region Event subscriptions
  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    CheckForFestival();
  }

  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (_pendingRegularFestivals.Value.Count > 0)
    {
      ResolvePendingFestivalTimes();
    }
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally() || (!HasToday && !HasTomorrow))
    {
      return;
    }

    if (HasToday)
    {
      EnqueueFestivalIcon(_todayIcon.Value, _todayType.Value, _todayHoverText.Value, true);
    }

    if (HasTomorrow)
    {
      EnqueueFestivalIcon(
        _tomorrowIcon.Value,
        _tomorrowType.Value,
        _tomorrowHoverText.Value,
        false
      );
    }
  }

  private void EnqueueFestivalIcon(
    ClickableTextureComponent icon,
    FestivalType type,
    string hoverText,
    bool isToday
  )
  {
    switch (type)
    {
      case FestivalType.Passive:
        icon.texture = Game1.mouseCursors;
        icon.sourceRect = StarSourceRect;
        icon.scale = 40 / 8f;
        break;
      case FestivalType.FishingDerby:
        icon.texture = Game1.mouseCursors_1_6;
        icon.sourceRect = FishingDerbySourceRect;
        icon.scale = 40 / 11f;
        break;
      default:
        _billboardTexture ??= _helper.GameContent.Load<Texture2D>("LooseSprites/Billboard");
        icon.texture = _billboardTexture;
        icon.sourceRect = FlagSourceRect;
        icon.scale = 40 / 13f;
        break;
    }

    IconHandler.Handler.EnqueueIcon(
      "Festival",
      (batch, pos) =>
      {
        icon.bounds.X = pos.X;
        icon.bounds.Y = pos.Y;

        // Offset icons to center them in the icon slot
        if (type == FestivalType.Passive)
        {
          icon.bounds.X += 8;
          icon.bounds.Y += 8;
        }
        else if (type == FestivalType.FishingDerby)
        {
          icon.bounds.X += 3;
          icon.bounds.Y += 3;
        }

        icon.draw(batch);

        // Draw static exclamation mark overlay for "today"
        if (isToday)
        {
          float scale = 1.6f;
          batch.Draw(
            Game1.mouseCursors,
            new Vector2(pos.X + 30 + 2.5f * scale, pos.Y + 16 + 7f * scale),
            new Rectangle(403, 496, 5, 14),
            Color.White,
            0f,
            new Vector2(2.5f, 7f),
            scale,
            SpriteEffects.None,
            1f
          );
        }
      },
      batch =>
      {
        if (icon.containsPoint(AndroidHud.MouseX, AndroidHud.MouseY))
        {
          IClickableMenu.drawHoverText(batch, hoverText, Game1.smallFont);
        }
      }
    );
  }
  #endregion
}
