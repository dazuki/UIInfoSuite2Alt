using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowSeasonalBerry : IDisposable
{
  #region Properties
  private readonly IModHelper _helper;
  private Texture2D? _cursors16;
  private bool Enabled { get; set; }
  private bool ShowHazelnut { get; set; }
  #endregion

  #region Lifecycle
  public ShowSeasonalBerry(IModHelper helper)
  {
    _helper = helper;

    _cursors16 = Game1.content.Load<Texture2D>("LooseSprites\\Cursors_1_6");
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool showSeasonalBerry)
  {
    Enabled = showSeasonalBerry;

    _helper.Events.Display.RenderingHud -= OnRenderingHud;

    if (showSeasonalBerry)
    {
      _helper.Events.Display.RenderingHud += OnRenderingHud;
    }
  }

  public void ToggleHazelnutOption(bool showHazelnut)
  {
    ShowHazelnut = showHazelnut;
    ToggleOption(Enabled);
  }
  #endregion

  #region Event subscriptions
  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally() || !Enabled)
    {
      return;
    }

    string season = Game1.currentSeason;
    int day = Game1.dayOfMonth;

    if (season == "spring" && day is >= 15 and <= 18)
      AddIcon("SeasonalBerry", new Rectangle(128, 193, 15, 15), I18n.CanFindSalmonberry(), 8 / 3f);
    else if (season == "summer" && day is >= 12 and <= 14)
      AddIcon("SeasonalBerry", new Rectangle(144, 256, 16, 16), I18n.CanFindBeachForage(), 20 / 8f);
    else if (season == "fall" && day is >= 8 and <= 11)
      AddIcon("SeasonalBerry", new Rectangle(32, 272, 16, 16), I18n.CanFindBlackberry(), 20 / 8f);
    else if (season == "fall" && day >= 15 && ShowHazelnut)
      AddIcon("SeasonalBerry", new Rectangle(1, 274, 14, 14), I18n.CanFindHazelnut(), 20 / 7f);

    if (season == "spring" && day == 17)
    {
      if (IsPotOfGoldStillThere())
      {
        AddIcon(
          "SeasonalBerry",
          new Rectangle(131, 0, 16, 16),
          I18n.CanFindPotOfGold(),
          20 / 8f,
          _cursors16
        );
      }
    }
  }

  private bool IsPotOfGoldStillThere()
  {
    GameLocation forest = Game1.getLocationFromName("Forest");
    if (forest == null)
      return false;

    // Pot of Gold Position in Forest
    Vector2 potPosition = new Vector2(52f, 98f);

    if (forest.objects.TryGetValue(potPosition, out StardewValley.Object obj))
    {
      return obj.QualifiedItemId == "(O)PotOfGold";
    }

    return false;
  }
  #endregion

  #region Logic
  private void AddIcon(
    string id,
    Rectangle sourceRect,
    string hoverText,
    float scale,
    Texture2D? customTexture = null
  )
  {
    ClickableTextureComponent? currentIcon = null;

    Texture2D textureToUse = customTexture ?? Game1.objectSpriteSheet;

    IconHandler.Handler.EnqueueIcon(
      id,
      (batch, pos) =>
      {
        currentIcon = new ClickableTextureComponent(
          new Rectangle(pos.X, pos.Y, 40, 40),
          textureToUse,
          sourceRect,
          scale
        );
        currentIcon.draw(batch);
      },
      batch =>
      {
        if (AndroidHud.IsHovered(currentIcon))
        {
          IClickableMenu.drawHoverText(batch, hoverText, Game1.smallFont);
        }
      }
    );
  }
  #endregion
}
