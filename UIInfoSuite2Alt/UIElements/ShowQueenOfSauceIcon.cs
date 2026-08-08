using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Infrastructure.Extensions;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowQueenOfSauceIcon : IDisposable
{
  private class QueenOfSauceTV : TV
  {
    public string[] GetWeeklyRecipe()
    {
      return base.getWeeklyRecipe();
    }
  }

  #region Properties
  private readonly Dictionary<string, string> _recipesByDescription = new();
  private Dictionary<string, string> _recipes = new();
  private CraftingRecipe? _todaysRecipe;

  private readonly PerScreen<bool> _drawQueenOfSauceIcon = new();
  private bool _showRecipeItemIcon;

  private readonly PerScreen<ClickableTextureComponent> _icon = new();

  // Meat Friday (Animal Husbandry mod) support
  private static readonly Dictionary<int, string> MeatFridayRecipes = new()
  {
    { 2, "Roast Duck" },
    { 3, "Bacon" },
    { 4, "Summer Sausage" },
    { 5, "Orange Chicken" },
    { 6, "Steak Fajitas" },
    { 7, "Rabbit au Vin" },
    { 8, "Winter Duck" },
  };

  private CraftingRecipe? _meatFridayRecipe;
  private readonly PerScreen<bool> _drawMeatFridayIcon = new();
  private readonly PerScreen<ClickableTextureComponent> _meatFridayIconComponent = new();
  private bool _hasAnimalHusbandry;

  private readonly IModHelper _helper;
  #endregion

  #region Life cycle
  public ShowQueenOfSauceIcon(IModHelper helper)
  {
    _helper = helper;
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool showQueenOfSauceIcon)
  {
    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.GameLoop.DayStarted -= OnDayStarted;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
    _helper.Events.GameLoop.SaveLoaded -= OnSaveLoaded;

    _hasAnimalHusbandry = _helper.ModRegistry.IsLoaded(ModCompat.AnimalHusbandry);

    if (showQueenOfSauceIcon)
    {
      LoadRecipes();
      CheckForNewRecipe();
      CheckForMeatFriday();

      _helper.Events.GameLoop.DayStarted += OnDayStarted;
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
      _helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
    }
  }

  public void ToggleShowRecipeItemIcon(bool showRecipeItemIcon)
  {
    _showRecipeItemIcon = showRecipeItemIcon;
  }
  #endregion

  #region Event subscriptions
  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    CheckForNewRecipe();
    CheckForMeatFriday();
  }

  private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
  {
    CheckForNewRecipe();
    CheckForMeatFriday();
  }

  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (!e.IsOneSecond)
    {
      return;
    }

    if (
      _drawQueenOfSauceIcon.Value
      && _todaysRecipe != null
      && Game1.player.knowsRecipe(_todaysRecipe.name)
    )
    {
      _drawQueenOfSauceIcon.Value = false;
    }

    if (
      _drawMeatFridayIcon.Value
      && _meatFridayRecipe != null
      && Game1.player.knowsRecipe(_meatFridayRecipe.name)
    )
    {
      _drawMeatFridayIcon.Value = false;
    }
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally())
    {
      return;
    }

    if (_drawQueenOfSauceIcon.Value && _todaysRecipe != null)
    {
      EnqueueRecipeIcon(
        "QueenOfSauce",
        _todaysRecipe,
        _icon,
        I18n.TodaysRecipe() + _todaysRecipe.DisplayName
      );
    }

    if (_drawMeatFridayIcon.Value)
    {
      string hoverText =
        _meatFridayRecipe != null
          ? I18n.TodaysMeatRecipe() + _meatFridayRecipe.DisplayName
          : I18n.TodaysMeatRecipe();

      EnqueueRecipeIcon("MeatFriday", _meatFridayRecipe, _meatFridayIconComponent, hoverText);
    }
  }

  private void EnqueueRecipeIcon(
    string iconKey,
    CraftingRecipe? recipe,
    PerScreen<ClickableTextureComponent> iconComponent,
    string hoverText
  )
  {
    IconHandler.Handler.EnqueueIcon(
      iconKey,
      (batch, pos) =>
      {
        var itemData =
          _showRecipeItemIcon && recipe != null ? recipe.GetItemData(useFirst: true) : null;
        if (itemData != null)
        {
          Texture2D itemTexture = itemData.GetTexture();
          Rectangle itemSourceRect = itemData.GetSourceRect();

          iconComponent.Value = new ClickableTextureComponent(
            new Rectangle(pos.X, pos.Y, 40, 40),
            itemTexture,
            itemSourceRect,
            2.5f
          );
          iconComponent.Value.draw(batch);

          batch.Draw(
            Game1.mouseCursors,
            new Vector2(pos.X + 18, pos.Y + 18),
            new Rectangle(609, 361, 28, 28),
            Color.White,
            0f,
            Vector2.Zero,
            0.8f,
            SpriteEffects.None,
            1f
          );
        }
        else
        {
          iconComponent.Value = new ClickableTextureComponent(
            new Rectangle(pos.X, pos.Y, 40, 40),
            Game1.mouseCursors,
            new Rectangle(609, 361, 28, 28),
            1.3f
          );
          iconComponent.Value.draw(batch);
        }
      },
      batch =>
      {
        if (!Game1.IsFakedBlackScreen() && AndroidHud.IsHovered(iconComponent.Value))
        {
          IClickableMenu.drawHoverText(batch, hoverText, Game1.smallFont);
        }
      }
    );
  }
  #endregion

  #region Logic
  private void LoadRecipes()
  {
    if (_recipes.Count == 0)
    {
      _recipes = Game1.content.Load<Dictionary<string, string>>("Data\\TV\\CookingChannel");
      foreach (KeyValuePair<string, string> next in _recipes)
      {
        string[] values = next.Value.Split('/');
        if (values.Length > 1)
        {
          _recipesByDescription[values[1]] = values[0];
        }
      }
    }
  }

  private void CheckForNewRecipe()
  {
    int recipiesKnownBeforeTvCall = Game1.player.cookingRecipes.Count();
    string[] dialogue = new QueenOfSauceTV().GetWeeklyRecipe();
    if (!_recipesByDescription.TryGetValue(dialogue[0], out string? recipeName))
    {
      _todaysRecipe = null;
      _drawQueenOfSauceIcon.Value = false;
      return;
    }

    _todaysRecipe = new CraftingRecipe(recipeName, true);

    if (Game1.player.cookingRecipes.Count() > recipiesKnownBeforeTvCall)
    {
      Game1.player.cookingRecipes.Remove(_todaysRecipe.name);
    }

    _drawQueenOfSauceIcon.Value =
      (Game1.dayOfMonth % 7 == 0 || (Game1.dayOfMonth - 3) % 7 == 0)
      && Game1.stats.DaysPlayed > 5
      && !Game1.player.knowsRecipe(_todaysRecipe.name);
  }

  private void CheckForMeatFriday()
  {
    _meatFridayRecipe = null;
    _drawMeatFridayIcon.Value = false;

    if (!_hasAnimalHusbandry)
    {
      return;
    }

    // Meat Friday airs on Fridays only
    if (Game1.Date.DayOfWeek != DayOfWeek.Friday)
    {
      return;
    }

    // Replicate Animal Husbandry rotation: (DaysPlayed % 112 / 14) + 1
    // Keys 2-8 map to recipes; key 1 means no episode this week
    int recipeNumber = (int)(Game1.stats.DaysPlayed % 112U / 14) + 1;
    if (recipeNumber < 2)
    {
      return;
    }

    if (!MeatFridayRecipes.TryGetValue(recipeNumber, out string? recipeName))
    {
      // Unknown recipe number - could be a new recipe added by the mod.
      // Show generic TV icon so the player still gets a reminder.
      _drawMeatFridayIcon.Value = true;
      return;
    }

    // Verify the recipe exists in game data (mod might have changed names)
    if (!CraftingRecipe.cookingRecipes.ContainsKey(recipeName))
    {
      _drawMeatFridayIcon.Value = true;
      return;
    }

    _meatFridayRecipe = new CraftingRecipe(recipeName, true);
    _drawMeatFridayIcon.Value = !Game1.player.knowsRecipe(_meatFridayRecipe.name);
  }
  #endregion
}
