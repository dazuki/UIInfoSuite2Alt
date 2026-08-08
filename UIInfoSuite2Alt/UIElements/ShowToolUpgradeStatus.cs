using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValley.Tools;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowToolUpgradeStatus : IDisposable
{
  #region Logic
  private void UpdateToolInfo()
  {
    Tool toolBeingUpgraded = _toolBeingUpgraded.Value = Game1.player.toolBeingUpgraded.Value;

    if (toolBeingUpgraded == null)
    {
      return;
    }

    if (toolBeingUpgraded is Axe or Pickaxe or Hoe or WateringCan or Pan or GenericTool)
    {
      ParsedItemData? itemData = ItemRegistry.GetDataOrErrorItem(toolBeingUpgraded.QualifiedItemId);
      Texture2D? itemTexture = itemData.GetTexture();
      Rectangle itemTextureLocation = itemData.GetSourceRect();
      float scaleFactor = 40.0f / itemTextureLocation.Width;
      _toolUpgradeIcon.Value = new ClickableTextureComponent(
        new Rectangle(0, 0, 40, 40),
        itemTexture,
        itemTextureLocation,
        scaleFactor
      );
    }

    if (Game1.player.daysLeftForToolUpgrade.Value > 0)
    {
      _hoverText.Value = string.Format(
        I18n.DaysUntilToolIsUpgraded(),
        Game1.player.daysLeftForToolUpgrade.Value,
        toolBeingUpgraded.DisplayName
      );
    }
    else
    {
      _hoverText.Value = string.Format(
        I18n.ToolIsFinishedBeingUpgraded(),
        toolBeingUpgraded.DisplayName
      );
    }
  }
  #endregion

  #region Properties
  private readonly PerScreen<string> _hoverText = new();
  private readonly PerScreen<Tool?> _toolBeingUpgraded = new();

  private readonly PerScreen<ClickableTextureComponent> _toolUpgradeIcon = new(() =>
  {
    return new ClickableTextureComponent(
      new Rectangle(0, 0, 40, 40),
      Game1.mouseCursors,
      new Rectangle(322, 498, 12, 12),
      40 / 12f
    );
  });

  private readonly IModHelper _helper;
  #endregion


  #region Life cycle
  public ShowToolUpgradeStatus(IModHelper helper)
  {
    _helper = helper;
  }

  public void Dispose()
  {
    ToggleOption(false);
    _toolBeingUpgraded.Value = null;
  }

  public void ToggleOption(bool showToolUpgradeStatus)
  {
    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.GameLoop.DayStarted -= OnDayStarted;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;

    if (showToolUpgradeStatus)
    {
      UpdateToolInfo();
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.GameLoop.DayStarted += OnDayStarted;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    }
  }
  #endregion


  #region Event subscriptions
  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (e.IsOneSecond && _toolBeingUpgraded.Value != Game1.player.toolBeingUpgraded.Value)
    {
      UpdateToolInfo();
    }
  }

  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    UpdateToolInfo();
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally() || _toolBeingUpgraded.Value == null)
    {
      return;
    }

    IconHandler.Handler.EnqueueIcon(
      "ToolUpgrade",
      (batch, pos) =>
      {
        _toolUpgradeIcon.Value.bounds.X = pos.X;
        _toolUpgradeIcon.Value.bounds.Y = pos.Y;
        _toolUpgradeIcon.Value.draw(batch);
      },
      batch =>
      {
        if (_toolUpgradeIcon.Value.containsPoint(AndroidHud.MouseX, AndroidHud.MouseY))
        {
          IClickableMenu.drawHoverText(batch, _hoverText.Value, Game1.smallFont);
        }
      }
    );
  }
  #endregion
}
