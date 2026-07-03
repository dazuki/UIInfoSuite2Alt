using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.GameData.FarmAnimals;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Network;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowWhenAnimalNeedsPet : IDisposable
{
  #region Properties
  private readonly PerScreen<float> _yMovementPerDraw = new();
  private readonly PerScreen<float> _alpha = new();

  private bool Enabled { get; set; }
  private bool HideOnMaxFriendship { get; set; }

  private readonly IModHelper _helper;
  private readonly bool _betterRanchingInstalled;
  #endregion


  #region Lifecycle
  public ShowWhenAnimalNeedsPet(IModHelper helper, bool hasBetterRanching)
  {
    _helper = helper;
    _betterRanchingInstalled = hasBetterRanching;
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool showWhenAnimalNeedsPet)
  {
    Enabled = showWhenAnimalNeedsPet;

    _helper.Events.Display.RenderingHud -= OnRenderingHud_DrawAnimalHasProduct;
    _helper.Events.Display.RenderedWorld -= OnRenderedWorld_DrawNeedsPetTooltip;
    _helper.Events.GameLoop.UpdateTicked -= UpdateTicked;

    if (showWhenAnimalNeedsPet)
    {
      if (!_betterRanchingInstalled)
      {
        _helper.Events.Display.RenderingHud += OnRenderingHud_DrawAnimalHasProduct;
      }
      _helper.Events.Display.RenderedWorld += OnRenderedWorld_DrawNeedsPetTooltip;
      _helper.Events.GameLoop.UpdateTicked += UpdateTicked;
    }
  }

  public void ToggleDisableOnMaxFriendshipOption(bool hideOnMaxFriendship)
  {
    if (HideOnMaxFriendship == hideOnMaxFriendship)
    {
      return;
    }

    HideOnMaxFriendship = hideOnMaxFriendship;
    ToggleOption(Enabled);
  }
  #endregion


  #region Event subscriptions
  private void OnRenderedWorld_DrawNeedsPetTooltip(object? sender, RenderedWorldEventArgs e)
  {
    if (UIElementUtils.IsRenderingNormally() && Game1.activeClickableMenu == null)
    {
      DrawIconForFarmAnimals();
      DrawIconForPets();
    }
  }

  private void OnRenderingHud_DrawAnimalHasProduct(object? sender, RenderingHudEventArgs e)
  {
    if (UIElementUtils.IsRenderingNormally() && Game1.activeClickableMenu == null)
    {
      DrawAnimalHasProduct();
    }
  }

  private void UpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally() || Game1.activeClickableMenu != null)
    {
      return;
    }

    var sine = (float)Math.Sin(e.Ticks / 20.0);
    _yMovementPerDraw.Value = -6f + 6f * sine;
    _alpha.Value = 0.8f + 0.2f * sine;
  }
  #endregion

  #region Logic
  private void DrawAnimalHasProduct()
  {
    NetLongDictionary<FarmAnimal, NetRef<FarmAnimal>>? animalsInCurrentLocation =
      GetAnimalsInCurrentLocation();

    if (animalsInCurrentLocation == null)
    {
      return;
    }

    foreach (KeyValuePair<long, FarmAnimal> animal in animalsInCurrentLocation.Pairs)
    {
      FarmAnimalHarvestType? harvestType = animal.Value.GetHarvestType();
      FarmAnimalData? animalData = animal.Value.GetAnimalData();
      if (
        harvestType is FarmAnimalHarvestType.DropOvernight or FarmAnimalHarvestType.DigUp
        || animal.Value.IsEmoting
        || animal.Value.currentProduce.Value == null
        || (animalData != null && animal.Value.age.Value < animalData.DaysToMature)
      )
      {
        continue;
      }

      string produceItemId = animal.Value.currentProduce.Value;
      ParsedItemData? produceData = ItemRegistry.GetData(produceItemId);
      if (produceData == null)
      {
        continue;
      }

      Vector2 positionAboveAnimal = animal.Value.getLocalPosition(Game1.viewport);
      positionAboveAnimal.X += 10;
      positionAboveAnimal.Y -= 34;
      positionAboveAnimal.Y += _yMovementPerDraw.Value;
      Game1.spriteBatch.Draw(
        Game1.emoteSpriteSheet,
        Utility.ModifyCoordinatesForUIScale(
          new Vector2(positionAboveAnimal.X - 4f, positionAboveAnimal.Y - 16f)
        ),
        new Rectangle(
          3 * (Game1.tileSize / 4) % Game1.emoteSpriteSheet.Width,
          3 * (Game1.tileSize / 4) / Game1.emoteSpriteSheet.Width * (Game1.tileSize / 4),
          Game1.tileSize / 4,
          Game1.tileSize / 4
        ),
        Color.White * 0.9f,
        0.0f,
        Vector2.Zero,
        4f,
        SpriteEffects.None,
        1f
      );

      Rectangle sourceRectangle = produceData.GetSourceRect();
      Game1.spriteBatch.Draw(
        produceData.GetTexture(),
        Utility.ModifyCoordinatesForUIScale(
          new Vector2(positionAboveAnimal.X + 16f, positionAboveAnimal.Y - 6f)
        ),
        sourceRectangle,
        Color.White * 0.9f,
        0.0f,
        Vector2.Zero,
        2f,
        SpriteEffects.None,
        1f
      );
    }
  }

  private void DrawIconForFarmAnimals()
  {
    NetLongDictionary<FarmAnimal, NetRef<FarmAnimal>>? animalsInCurrentLocation =
      GetAnimalsInCurrentLocation();

    if (animalsInCurrentLocation == null)
    {
      return;
    }

    foreach (KeyValuePair<long, FarmAnimal> animal in animalsInCurrentLocation.Pairs)
    {
      if (
        animal.Value.IsEmoting
        || animal.Value.wasPet.Value
        || (animal.Value.friendshipTowardFarmer.Value >= 1000 && HideOnMaxFriendship)
      )
      {
        continue;
      }

      Vector2 positionAboveAnimal = GetPositionAboveAnimal(animal.Value);

      if (animal.Value.GetSpriteWidthForPositioning() > 16)
      {
        positionAboveAnimal.X += 50f;
        positionAboveAnimal.Y += 50f;
      }

      float yBob = _yMovementPerDraw.Value;
      float alpha = _alpha.Value;

      // Hand icon
      Game1.spriteBatch.Draw(
        Game1.mouseCursors,
        Game1.GlobalToLocal(
          Game1.viewport,
          new Vector2(positionAboveAnimal.X, positionAboveAnimal.Y + yBob)
        ),
        new Rectangle(32, 0, 16, 16),
        Color.White * alpha,
        0.0f,
        Vector2.Zero,
        4f,
        SpriteEffects.None,
        1f
      );

      // Heart overlay
      Game1.spriteBatch.Draw(
        Game1.mouseCursors,
        Game1.GlobalToLocal(
          Game1.viewport,
          new Vector2(positionAboveAnimal.X + 32f, positionAboveAnimal.Y + yBob + 32f)
        ),
        new Rectangle(211, 428, 7, 6),
        Color.White * alpha,
        0.0f,
        Vector2.Zero,
        3f,
        SpriteEffects.None,
        1f
      );
    }
  }

  private void DrawIconForPets()
  {
    if (Game1.currentLocation == null)
    {
      return;
    }

    foreach (NPC? character in Game1.currentLocation.characters)
    {
      if (
        character is not Pet pet
        || PetWasPettedToday(pet)
        || (pet.friendshipTowardFarmer.Value >= 1000 && HideOnMaxFriendship)
      )
      {
        continue;
      }

      Vector2 positionAboveAnimal = GetPositionAboveAnimal(character);

      positionAboveAnimal.X += 50f;
      positionAboveAnimal.Y += 20f;

      float yBob = _yMovementPerDraw.Value;
      float alpha = _alpha.Value;

      // Hand icon
      Game1.spriteBatch.Draw(
        Game1.mouseCursors,
        Game1.GlobalToLocal(
          Game1.viewport,
          new Vector2(positionAboveAnimal.X, positionAboveAnimal.Y + yBob)
        ),
        new Rectangle(32, 0, 16, 16),
        Color.White * alpha,
        0.0f,
        Vector2.Zero,
        4f,
        SpriteEffects.None,
        1f
      );

      // Heart overlay
      Game1.spriteBatch.Draw(
        Game1.mouseCursors,
        Game1.GlobalToLocal(
          Game1.viewport,
          new Vector2(positionAboveAnimal.X + 32f, positionAboveAnimal.Y + yBob + 32f)
        ),
        new Rectangle(211, 428, 7, 6),
        Color.White * alpha,
        0.0f,
        Vector2.Zero,
        3f,
        SpriteEffects.None,
        1f
      );
    }
  }

  private static bool PetWasPettedToday(Pet pet)
  {
    int today = Game1.Date.TotalDays;
    foreach (int day in pet.lastPetDay.Values)
    {
      if (day == today)
      {
        return true;
      }
    }
    return false;
  }

  private Vector2 GetPositionAboveAnimal(Character animal)
  {
    Vector2 animalPosition = animal.Position;
    animalPosition.X += 10;
    animalPosition.Y -= 34;
    return animalPosition;
  }

  private static NetLongDictionary<FarmAnimal, NetRef<FarmAnimal>>? GetAnimalsInCurrentLocation()
  {
    return Game1.currentLocation?.animals;
  }
  #endregion
}
