using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Internal;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Compatibility.Helpers;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Infrastructure.Extensions;
using UIInfoSuite2Alt.Infrastructure.Helpers;

namespace UIInfoSuite2Alt.UIElements;

public class ShowTravelingMerchant : IDisposable
{
  #region Properties
  private bool _travelingMerchantIsHere;
  private bool _travelingMerchantIsVisited;
  private bool _merchantHasBundleItems;
  private readonly List<string> _bundleItemNames = [];
  private bool _merchantHasUbBundleItems;
  private readonly List<string> _ubBundleItemNames = [];
  private ClickableTextureComponent _travelingMerchantIcon = null!;
  private Texture2D _merchantTexture = null!;
  private int _bundlePulseTimer;
  private int _bundlePulseDelay;

  // The stock check runs from DayStarted, where mods that rewrite bundle data (e.g.
  // Challenging Community Center Bundles) may not have applied theirs yet. Defer it a tick.
  private bool _merchantCheckPending;

  private bool _rsvIsLoaded;
  private bool _rsvMerchantIsHere;
  private bool _rsvMerchantIsVisited;
  private Texture2D? _rsvIconTexture;
  private const string RsvModId = ModCompat.RidgesideVillage;
  private const string RsvMerchantLocation = "Custom_Ridgeside_RSVTheHike";
  private const float RsvHueShift = -60f;

  private bool Enabled { get; set; }
  private bool HideWhenVisited { get; set; }
  private bool ShowBundleIcon { get; set; }
  private bool ShowBundleItemNames { get; set; }

  private readonly IModHelper _helper;
  #endregion


  #region Lifecycle
  public ShowTravelingMerchant(IModHelper helper)
  {
    _helper = helper;
  }

  public void Dispose()
  {
    ToggleOption(false);
    _rsvIconTexture?.Dispose();
    _rsvIconTexture = null;
  }

  public void ToggleOption(bool showTravelingMerchant)
  {
    Enabled = showTravelingMerchant;

    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.GameLoop.DayStarted -= OnDayStarted;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
    _helper.Events.Display.MenuChanged -= OnMenuChanged;
    UnlockableBundleHelper.BundleStateChanged -= OnUbBundleStateChanged;

    if (showTravelingMerchant)
    {
      _merchantTexture = AssetHelper.TryLoadTexture(_helper, "assets/merchant.png");
      _rsvIconTexture?.Dispose();
      _rsvIconTexture = null;
      _rsvIsLoaded = _helper.ModRegistry.IsLoaded(RsvModId);
      UpdateTravelingMerchant();
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.GameLoop.DayStarted += OnDayStarted;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
      _helper.Events.Display.MenuChanged += OnMenuChanged;
      UnlockableBundleHelper.BundleStateChanged += OnUbBundleStateChanged;
    }
  }

  public void ToggleHideWhenVisitedOption(bool hideWhenVisited)
  {
    HideWhenVisited = hideWhenVisited;
  }

  public void ToggleShowBundleIconOption(bool showBundleIcon)
  {
    ShowBundleIcon = showBundleIcon;
  }

  public void ToggleShowBundleItemNamesOption(bool showBundleItemNames)
  {
    ShowBundleItemNames = showBundleItemNames;
  }
  #endregion


  #region Event subscriptions
  private void OnDayStarted(object? sender, EventArgs e)
  {
    UpdateTravelingMerchant();
  }

  private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
  {
    if (e.NewMenu is not ShopMenu menu)
      return;

    string locationName = Game1.currentLocation.Name;

    if (locationName == "Forest" && menu.forSale.Any(s => s is not Hat))
    {
      _travelingMerchantIsVisited = true;
      _merchantHasBundleItems = false;
      _bundleItemNames.Clear();
      _merchantHasUbBundleItems = false;
      _ubBundleItemNames.Clear();
    }
    else if (locationName == RsvMerchantLocation && menu.forSale.Any(s => s is not Hat))
    {
      _rsvMerchantIsVisited = true;
      _merchantHasBundleItems = false;
      _bundleItemNames.Clear();
      _merchantHasUbBundleItems = false;
      _ubBundleItemNames.Clear();
    }
  }

  private void OnUbBundleStateChanged()
  {
    if (!_travelingMerchantIsHere && !_rsvMerchantIsHere)
    {
      return;
    }

    CheckMerchantForBundleItems();

    // Re-show icon if merchant has new UB bundle items, even if already visited
    if (_merchantHasUbBundleItems)
    {
      if (_travelingMerchantIsHere)
      {
        _travelingMerchantIsVisited = false;
      }

      if (_rsvMerchantIsHere)
      {
        _rsvMerchantIsVisited = false;
      }
    }
  }

  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (_merchantCheckPending)
    {
      _merchantCheckPending = false;
      CheckMerchantForBundleItems();
    }

    if ((!_merchantHasBundleItems && !_merchantHasUbBundleItems) || !ShowBundleIcon)
    {
      return;
    }

    int elapsed = (int)Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;

    if (_bundlePulseTimer > 0)
    {
      _bundlePulseTimer -= elapsed;
    }
    else if (_bundlePulseDelay > 0)
    {
      _bundlePulseDelay -= elapsed;
    }
    else
    {
      _bundlePulseTimer = 1000;
      _bundlePulseDelay = 3000;
    }
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (UIElementUtils.IsRenderingNormally() && ShouldDrawIcon())
    {
      IconHandler.Handler.EnqueueIcon(
        "TravelingMerchant",
        (batch, pos) =>
        {
          bool useRsvIcon =
            _rsvMerchantIsHere
            && (!_rsvMerchantIsVisited || !HideWhenVisited)
            && (!_travelingMerchantIsHere || _travelingMerchantIsVisited);
          Texture2D iconTexture = useRsvIcon ? GetRsvIconTexture() : _merchantTexture;
          var iconSource = new Rectangle(0, 0, _merchantTexture.Width, _merchantTexture.Height);

          _travelingMerchantIcon = new ClickableTextureComponent(
            new Rectangle(pos.X, pos.Y, 40, 40),
            iconTexture,
            iconSource,
            2f
          );
          _travelingMerchantIcon.draw(batch);

          if ((_merchantHasBundleItems || _merchantHasUbBundleItems) && ShowBundleIcon)
          {
            float baseScale = 1.6f;
            float scale = baseScale;
            Vector2 shake = Vector2.Zero;

            if (_bundlePulseTimer > 0)
            {
              float pulseScale =
                1f / (Math.Max(300f, Math.Abs(_bundlePulseTimer % 1000 - 500)) / 500f);
              scale = baseScale * pulseScale;
              if (pulseScale > 1f)
              {
                shake = new Vector2(Game1.random.Next(-1, 2), Game1.random.Next(-1, 2));
              }
            }

            batch.Draw(
              Game1.mouseCursors,
              new Vector2(pos.X + 27 + 2.5f * baseScale, pos.Y + 11 + 7f * baseScale) + shake,
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
          if (AndroidHud.IsHovered(_travelingMerchantIcon))
          {
            List<string> lines = [];

            if (_travelingMerchantIsHere && (!_travelingMerchantIsVisited || !HideWhenVisited))
            {
              lines.Add(I18n.TravelingMerchantIsInTown());

              if (_merchantHasBundleItems && ShowBundleIcon)
              {
                lines.Add(I18n.TravelingMerchantHasBundleItem());

                if (ShowBundleItemNames && _bundleItemNames.Count > 0)
                {
                  lines.Add(string.Join(", ", _bundleItemNames));
                }
              }

              if (_merchantHasUbBundleItems && ShowBundleIcon)
              {
                lines.Add(I18n.TravelingMerchantHasUbBundleItem());

                if (ShowBundleItemNames && _ubBundleItemNames.Count > 0)
                {
                  lines.Add(string.Join(", ", _ubBundleItemNames));
                }
              }
            }

            if (_rsvMerchantIsHere && (!_rsvMerchantIsVisited || !HideWhenVisited))
            {
              lines.Add(I18n.RsvTravelingMerchantIsAtHike());

              if (!_travelingMerchantIsHere)
              {
                if (_merchantHasBundleItems && ShowBundleIcon)
                {
                  lines.Add(I18n.TravelingMerchantHasBundleItem());

                  if (ShowBundleItemNames && _bundleItemNames.Count > 0)
                  {
                    lines.Add(string.Join(", ", _bundleItemNames));
                  }
                }

                if (_merchantHasUbBundleItems && ShowBundleIcon)
                {
                  lines.Add(I18n.TravelingMerchantHasUbBundleItem());

                  if (ShowBundleItemNames && _ubBundleItemNames.Count > 0)
                  {
                    lines.Add(string.Join(", ", _ubBundleItemNames));
                  }
                }
              }
            }

            IClickableMenu.drawHoverText(batch, string.Join("\n", lines), Game1.smallFont);
          }
        }
      );
    }
  }
  #endregion


  #region Logic
  private void UpdateTravelingMerchant()
  {
    _travelingMerchantIsHere = (
      (Forest)Game1.getLocationFromName(nameof(Forest))
    ).ShouldTravelingMerchantVisitToday();
    _travelingMerchantIsVisited = false;
    _merchantHasBundleItems = false;
    _bundleItemNames.Clear();
    _merchantHasUbBundleItems = false;
    _ubBundleItemNames.Clear();

    _rsvMerchantIsHere = _rsvIsLoaded && Game1.dayOfMonth % 7 == 3;
    _rsvMerchantIsVisited = false;

    _merchantCheckPending = _travelingMerchantIsHere || _rsvMerchantIsHere;
  }

  private void CheckMerchantForBundleItems()
  {
    try
    {
      Dictionary<ISalable, ItemStockInformation> stock = ShopBuilder.GetShopStock("Traveler");
      _bundleItemNames.Clear();
      _ubBundleItemNames.Clear();

      foreach (ISalable salable in stock.Keys)
      {
        if (salable is Item item && BundleHelper.GetBundleItemIfNotDonated(item) != null)
        {
          _bundleItemNames.Add(item.DisplayName);
        }
      }

      _merchantHasBundleItems = _bundleItemNames.Count > 0;

      // Check Unlockable Bundles separately
      List<string> ubNames = UnlockableBundleHelper.GetMerchantBundleItemNames(stock.Keys);
      _ubBundleItemNames.AddRange(ubNames);
      _merchantHasUbBundleItems = _ubBundleItemNames.Count > 0;

      if (_merchantHasBundleItems || _merchantHasUbBundleItems)
      {
        ModEntry.MonitorObject.Log(
          $"ShowTravelingMerchant: bundle items in stock, cc=[{string.Join(", ", _bundleItemNames)}], ub=[{string.Join(", ", _ubBundleItemNames)}]",
          LogLevel.Trace
        );
      }
    }
    catch (Exception e)
    {
      ModEntry.MonitorObject.Log(
        $"ShowTravelingMerchant: merchant stock check failed, {e.Message}",
        LogLevel.Warn
      );
      _merchantHasBundleItems = false;
      _merchantHasUbBundleItems = false;
    }
  }

  private bool ShouldDrawIcon()
  {
    bool vanillaVisible =
      _travelingMerchantIsHere && (!_travelingMerchantIsVisited || !HideWhenVisited);
    bool rsvVisible = _rsvMerchantIsHere && (!_rsvMerchantIsVisited || !HideWhenVisited);
    return vanillaVisible || rsvVisible;
  }

  private Texture2D GetRsvIconTexture()
  {
    if (_rsvIconTexture == null)
    {
      var pixels = new Color[_merchantTexture.Width * _merchantTexture.Height];
      _merchantTexture.GetData(pixels);

      for (int i = 0; i < pixels.Length; i++)
      {
        if (pixels[i].A > 0)
        {
          pixels[i] = pixels[i].ShiftHue(RsvHueShift);
        }
      }

      _rsvIconTexture = new Texture2D(
        Game1.graphics.GraphicsDevice,
        _merchantTexture.Width,
        _merchantTexture.Height
      );
      _rsvIconTexture.SetData(pixels);
    }

    return _rsvIconTexture;
  }
  #endregion
}
