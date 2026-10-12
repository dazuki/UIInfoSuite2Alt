using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValley.Tools;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowToolUpgradeStatus : IDisposable
{
  #region Logic
  private void UpdateToolInfo()
  {
    UpdateExtraUpgrades();

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

  private void UpdateExtraUpgrades()
  {
    _extraUpgrades.Value.Clear();

    if (!ApiManager.GetApi(ModCompat.FlexibleUpgrades, out IFlexibleUpgradesApi? api))
    {
      return;
    }

    foreach (var order in api.GetToolOrders())
    {
      if (order.IsVanilla)
      {
        continue;
      }

      string hoverText =
        order.DaysLeft > 0
          ? string.Format(I18n.DaysUntilToolIsUpgraded(), order.DaysLeft, order.DisplayName)
          : string.Format(I18n.ToolIsFinishedBeingUpgraded(), order.DisplayName);
      _extraUpgrades.Value.Add(
        new ExtraUpgrade(ResolveExtraIcon(order.QualifiedItemId), hoverText)
      );
    }
  }

  private static ClickableTextureComponent ResolveExtraIcon(string qualifiedItemId)
  {
    if (string.IsNullOrEmpty(qualifiedItemId))
    {
      return new ClickableTextureComponent(
        new Rectangle(0, 0, 40, 40),
        Game1.mouseCursors,
        new Rectangle(322, 498, 12, 12),
        40 / 12f
      );
    }

    ParsedItemData itemData = ItemRegistry.GetDataOrErrorItem(qualifiedItemId);
    Texture2D itemTexture = itemData.GetTexture();
    Rectangle itemTextureLocation = itemData.GetSourceRect();
    return new ClickableTextureComponent(
      new Rectangle(0, 0, 40, 40),
      itemTexture,
      itemTextureLocation,
      40.0f / itemTextureLocation.Width
    );
  }
  #endregion

  #region Properties
  private readonly PerScreen<string> _hoverText = new();
  private readonly PerScreen<Tool?> _toolBeingUpgraded = new();
  private readonly PerScreen<List<ExtraUpgrade>> _extraUpgrades = new(() => []);

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

  private readonly record struct ExtraUpgrade(ClickableTextureComponent Icon, string HoverText);
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
    _extraUpgrades.Value.Clear();
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
    if (!e.IsOneSecond)
    {
      return;
    }

    if (_toolBeingUpgraded.Value != Game1.player.toolBeingUpgraded.Value)
    {
      UpdateToolInfo();
    }
    else
    {
      UpdateExtraUpgrades();
    }
  }

  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    UpdateToolInfo();
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally())
    {
      return;
    }

    if (_toolBeingUpgraded.Value != null)
    {
      EnqueueUpgradeIcon(_toolUpgradeIcon.Value, _hoverText.Value);
    }

    foreach (ExtraUpgrade extra in _extraUpgrades.Value)
    {
      EnqueueUpgradeIcon(extra.Icon, extra.HoverText);
    }
  }

  private static void EnqueueUpgradeIcon(ClickableTextureComponent icon, string hoverText)
  {
    IconHandler.Handler.EnqueueIcon(
      "ToolUpgrade",
      (batch, pos) =>
      {
        icon.bounds.X = pos.X;
        icon.bounds.Y = pos.Y;
        icon.draw(batch);
      },
      batch =>
      {
        if (AndroidHud.IsHovered(icon))
        {
          IClickableMenu.drawHoverText(batch, hoverText, Game1.smallFont);
        }
      }
    );
  }
  #endregion
}
