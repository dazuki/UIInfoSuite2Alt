using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Patches;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowRainyDayIcon : IDisposable
{
  #region Properties
  private class LocationWeather
  {
    internal bool IsRainyTomorrow { get; set; }
    internal Rectangle? SpriteLocation { get; set; }
    internal string HoverText { get; set; } = "";
    internal ClickableTextureComponent? IconComponent { get; set; }

    // Texture is loaded per draw; a cached one goes stale on content reload
    internal string? CustomTextureAsset { get; set; }
  }

  private readonly LocationWeather _valleyWeather = new();
  private readonly LocationWeather _islandWeather = new();
  private bool _hasVisitedIsland;
  private bool _refreshPending = true;
  private Texture2D _iconSheet = null!;

  private Color[] _weatherIconColors = null!;
  private const int WeatherSheetWidth = 15 * 4 + 18 * 3;
  private const int WeatherSheetHeight = 18;

  private Texture2D _weatherBorderTexture = null!;

  private bool _requireTv;
  private readonly IModHelper _helper;
  #endregion

  #region Lifecycle
  public ShowRainyDayIcon(IModHelper helper)
  {
    _helper = helper;
    CreateTileSheet();
  }

  public void Dispose()
  {
    ToggleOption(false);
    _iconSheet.Dispose();

    if (!AssetHelper.IsFallback(_weatherBorderTexture))
    {
      _weatherBorderTexture.Dispose();
    }
  }

  public void ToggleRequireTvOption(bool requireTv)
  {
    _requireTv = requireTv;
  }

  public void ToggleOption(bool showRainyDay)
  {
    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
    _helper.Events.GameLoop.DayStarted -= OnDayStarted;

    if (showRainyDay)
    {
      _refreshPending = true;
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
      _helper.Events.GameLoop.DayStarted += OnDayStarted;
    }
  }
  #endregion

  #region Event subscriptions
  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    // Deferred: weather lookups load content
    _refreshPending = true;
  }

  // Polled: a Rain Totem can change tomorrow's weather mid-day
  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if ((!_refreshPending && !e.IsOneSecond) || !Context.IsWorldReady)
    {
      return;
    }

    _refreshPending = false;
    RefreshWeather();
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (
      !UIElementUtils.IsRenderingNormally()
      || (_requireTv && !TvChannelWatcher.HasWatchedWeather.Value)
    )
    {
      return;
    }

    EnqueueWeatherIcon(_valleyWeather);
    if (_hasVisitedIsland)
    {
      EnqueueWeatherIcon(_islandWeather);
    }
  }

  private void EnqueueWeatherIcon(LocationWeather weather)
  {
    if (!weather.IsRainyTomorrow || !weather.SpriteLocation.HasValue)
    {
      return;
    }

    IconHandler.Handler.EnqueueIcon(
      "Weather",
      (batch, pos) =>
      {
        var bounds = new Rectangle(pos.X, pos.Y, 40, 40);
        Rectangle sourceRect = weather.SpriteLocation.Value;

        if (weather.CustomTextureAsset != null)
        {
          Texture2D? customTexture = TryLoadCustomTexture(weather.CustomTextureAsset);
          if (customTexture == null)
          {
            return;
          }

          batch.Draw(_weatherBorderTexture, bounds, Color.White);
          var iconRect = new Rectangle(bounds.X + 3, bounds.Y + 3, 34, 34);
          batch.Draw(customTexture, iconRect, sourceRect, Color.White);
          UpdateIconComponent(weather, bounds, customTexture, sourceRect, 1f);
        }
        else
        {
          UpdateIconComponent(weather, bounds, _iconSheet, sourceRect, 8 / 3f).draw(batch);
        }
      },
      batch =>
      {
        bool hasMouse = AndroidHud.IsHovered(weather.IconComponent);
        bool hasText = !string.IsNullOrEmpty(weather.HoverText);
        if (weather.IsRainyTomorrow && hasMouse && hasText)
        {
          IClickableMenu.drawHoverText(batch, weather.HoverText, Game1.smallFont);
        }
      }
    );
  }

  private static ClickableTextureComponent UpdateIconComponent(
    LocationWeather weather,
    Rectangle bounds,
    Texture2D texture,
    Rectangle sourceRect,
    float scale
  )
  {
    ClickableTextureComponent icon = weather.IconComponent ??= new ClickableTextureComponent(
      bounds,
      texture,
      sourceRect,
      scale
    );
    icon.bounds = bounds;
    icon.texture = texture;
    icon.sourceRect = sourceRect;
    icon.scale = scale;
    icon.baseScale = scale;
    return icon;
  }
  #endregion

  #region Logic
  private static bool HasVisitedIsland()
  {
    return Game1.MasterPlayer.mailReceived.Contains("willyBoatFixed");
  }

  private static string GetWeatherForTomorrow()
  {
    var date = new WorldDate(Game1.Date);
    ++date.TotalDays;
    string tomorrowWeather = Game1.IsMasterGame
      ? Game1.weatherForTomorrow
      : Game1.netWorldState.Value.WeatherForTomorrow;
    return Game1.getWeatherModificationsForDate(date, tomorrowWeather);
  }

  private static string GetIslandWeatherForTomorrow()
  {
    return Game1.netWorldState.Value.GetWeatherForLocation("Island").WeatherForTomorrow;
  }

  /// <summary>Builds a composite weather icon sheet from TV border + individual weather sprites.</summary>
  private void CreateTileSheet()
  {
    // Build composite sheet (separate copy to avoid disturbing existing sprites)
    _iconSheet = new Texture2D(
      Game1.graphics.GraphicsDevice,
      WeatherSheetWidth,
      WeatherSheetHeight
    );
    _weatherIconColors = new Color[WeatherSheetWidth * WeatherSheetHeight];
    _weatherBorderTexture = AssetHelper.TryLoadTextureFromFile(
      Path.Combine(_helper.DirectoryPath, "assets", "weatherbox.png")
    );
    Texture2D weatherBorderTexture = _weatherBorderTexture;
    var subTextureColors = new Color[15 * 15];

    // Copy TV border to each icon slot
    weatherBorderTexture.GetData(0, new Rectangle(0, 0, 15, 15), subTextureColors, 0, 15 * 15);
    // Copy to each slot
    for (var i = 0; i < 4; i++)
    {
      Tools.SetSubTexture(
        subTextureColors,
        _weatherIconColors,
        WeatherSheetWidth,
        new Rectangle(i * 15, 0, 15, 15)
      );
    }

    // Island parrot expanded sprites
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(60, 0, 15, 15)
    );
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(78, 0, 15, 15)
    );
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(96, 0, 15, 15)
    );

    subTextureColors = new Color[13 * 13];
    // Rainy Weather
    Game1.mouseCursors.GetData(0, new Rectangle(504, 333, 13, 13), subTextureColors, 0, 13 * 13);
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(1, 1, 13, 13)
    );
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(61, 1, 13, 13)
    );

    // Stormy Weather
    Game1.mouseCursors.GetData(0, new Rectangle(426, 346, 13, 13), subTextureColors, 0, 13 * 13);
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(16, 1, 13, 13)
    );
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(79, 1, 13, 13)
    );

    // Snowy Weather
    Game1.mouseCursors.GetData(0, new Rectangle(465, 346, 13, 13), subTextureColors, 0, 13 * 13);
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(31, 1, 13, 13)
    );

    // Green Rain
    Game1.mouseCursors_1_6.GetData(
      0,
      new Rectangle(178, 363, 13, 13),
      subTextureColors,
      0,
      13 * 13
    );
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(46, 1, 13, 13)
    );
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(97, 1, 13, 13)
    );

    subTextureColors = new Color[9 * 14];
    Game1.mouseCursors.GetData(0, new Rectangle(146, 149, 9, 14), subTextureColors, 0, 9 * 14);
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(69, 4, 9, 14),
      true
    );
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(87, 4, 9, 14),
      true
    );
    Tools.SetSubTexture(
      subTextureColors,
      _weatherIconColors,
      WeatherSheetWidth,
      new Rectangle(105, 4, 9, 14),
      true
    );

    _iconSheet.SetData(_weatherIconColors);
  }

  private void RefreshWeather()
  {
    string valleyWeatherId = GetWeatherForTomorrow();
    SetValleyWeatherSprite(valleyWeatherId);

    _hasVisitedIsland = HasVisitedIsland();
    string? islandWeatherId = null;
    if (_hasVisitedIsland)
    {
      islandWeatherId = GetIslandWeatherForTomorrow();
      SetIslandWeatherSprite(islandWeatherId);
    }

    ModEntry.MonitorObject.LogOnce(
      $"ShowRainyDayIcon: tomorrow's weather, valley={valleyWeatherId}, island={islandWeatherId ?? "n/a"}",
      LogLevel.Trace
    );
  }

  private void SetValleyWeatherSprite(string weatherId)
  {
    _valleyWeather.CustomTextureAsset = null;

    switch (weatherId)
    {
      case Game1.weather_sunny:
      case Game1.weather_debris:
      case Game1.weather_festival:
      case Game1.weather_wedding:
        _valleyWeather.IsRainyTomorrow = false;
        break;

      case Game1.weather_rain:
        _valleyWeather.IsRainyTomorrow = true;
        _valleyWeather.SpriteLocation = new Rectangle(0, 0, 15, 15);
        _valleyWeather.HoverText = I18n.RainNextDay();
        break;

      case Game1.weather_lightning:
        _valleyWeather.IsRainyTomorrow = true;
        _valleyWeather.SpriteLocation = new Rectangle(15, 0, 15, 15);
        _valleyWeather.HoverText = I18n.ThunderstormNextDay();
        break;

      case Game1.weather_snow:
        _valleyWeather.IsRainyTomorrow = true;
        _valleyWeather.SpriteLocation = new Rectangle(30, 0, 15, 15);
        _valleyWeather.HoverText = I18n.SnowNextDay();
        break;

      case Game1.weather_green_rain:
        _valleyWeather.IsRainyTomorrow = true;
        _valleyWeather.SpriteLocation = new Rectangle(45, 0, 15, 15);
        _valleyWeather.HoverText = I18n.RainNextDay();
        break;

      default:
        TrySetCustomWeather(_valleyWeather, weatherId);
        break;
    }
  }

  private void SetIslandWeatherSprite(string weatherId)
  {
    _islandWeather.CustomTextureAsset = null;

    switch (weatherId)
    {
      case Game1.weather_rain:
        _islandWeather.IsRainyTomorrow = true;
        _islandWeather.SpriteLocation = new Rectangle(60, 0, 18, 18);
        _islandWeather.HoverText = I18n.IslandRainNextDay();
        break;

      case Game1.weather_lightning:
        _islandWeather.IsRainyTomorrow = true;
        _islandWeather.SpriteLocation = new Rectangle(78, 0, 18, 18);
        _islandWeather.HoverText = I18n.IslandThunderstormNextDay();
        break;

      case Game1.weather_green_rain:
        _islandWeather.IsRainyTomorrow = true;
        _islandWeather.SpriteLocation = new Rectangle(96, 0, 18, 18);
        _islandWeather.HoverText = I18n.IslandRainNextDay();
        break;

      default:
        _islandWeather.IsRainyTomorrow = false;
        break;
    }
  }

  private void TrySetCustomWeather(LocationWeather weather, string weatherId)
  {
    if (!ApiManager.GetApi(ModCompat.CloudySkies, out ICloudySkiesApi? api))
    {
      weather.IsRainyTomorrow = false;
      return;
    }

    IWeatherData? data = api.GetAllCustomWeather().FirstOrDefault(w => w.Id == weatherId);
    if (data == null || string.IsNullOrEmpty(data.IconTexture))
    {
      weather.IsRainyTomorrow = false;
      return;
    }

    if (TryLoadCustomTexture(data.IconTexture) == null)
    {
      weather.IsRainyTomorrow = false;
      return;
    }

    weather.IsRainyTomorrow = true;
    weather.CustomTextureAsset = data.IconTexture;
    weather.SpriteLocation = new Rectangle(data.IconSource.X, data.IconSource.Y, 12, 8);
    weather.HoverText = I18n.CustomWeatherNextDay(weatherName: data.DisplayName);
  }

  private Texture2D? TryLoadCustomTexture(string assetName)
  {
    try
    {
      return _helper.GameContent.Load<Texture2D>(assetName);
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.LogOnce(
        $"ShowRainyDayIcon: failed to load weather texture '{assetName}', {ex.Message}",
        LogLevel.Warn
      );
      return null;
    }
  }
  #endregion
}
