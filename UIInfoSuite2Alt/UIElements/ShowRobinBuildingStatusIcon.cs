using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowRobinBuildingStatusIcon : IDisposable
{
  #region Types
  private sealed class BuilderStatus
  {
    public readonly string BuilderName;
    public readonly string IconKey;
    public readonly bool IsRobin;
    public readonly string? SpriteAsset;
    public readonly Rectangle HeadRect;
    public readonly Func<string> BuildingStatusText;

    public bool InProgress;
    public string HoverText = "";
    public Texture2D? IconSheet;
    public readonly PerScreen<ClickableTextureComponent> Icon = new();

    public BuilderStatus(
      string builderName,
      string iconKey,
      bool isRobin,
      string? spriteAsset,
      Rectangle headRect,
      Func<string> buildingStatusText
    )
    {
      BuilderName = builderName;
      IconKey = iconKey;
      IsRobin = isRobin;
      SpriteAsset = spriteAsset;
      HeadRect = headRect;
      BuildingStatusText = buildingStatusText;
    }
  }
  #endregion

  #region Properties
  // 1px edits for better alignment with other icons
  private static readonly Rectangle RobinHeadRect = new(0, 196, 15, 14);

  private static readonly Rectangle ApprenticeHeadRect = new(48, 36, 13, 14);

  private readonly List<BuilderStatus> _builders = new();
  private readonly IModHelper _helper;
  #endregion

  #region Lifecycle
  public ShowRobinBuildingStatusIcon(IModHelper helper)
  {
    _helper = helper;

    _builders.Add(
      new BuilderStatus(
        "Robin",
        "RobinBuilding",
        true,
        null,
        RobinHeadRect,
        I18n.RobinBuildingStatus
      )
    );

    if (helper.ModRegistry.IsLoaded(ModCompat.CarpentersApprentice))
    {
      _builders.Add(
        new BuilderStatus(
          "RobinApprentice",
          "RobinApprenticeBuilding",
          false,
          "CarpentersApprentice/RobinApprenticeSprite",
          ApprenticeHeadRect,
          I18n.ApprenticeBuildingStatus
        )
      );
      _builders.Add(
        new BuilderStatus(
          "RobinApprentice2",
          "RobinApprentice2Building",
          false,
          "CarpentersApprentice/RobinApprentice2Sprite",
          ApprenticeHeadRect,
          I18n.ApprenticeBuildingStatus
        )
      );
      _builders.Add(
        new BuilderStatus(
          "RobinApprentice3",
          "RobinApprentice3Building",
          false,
          "CarpentersApprentice/RobinApprentice3Sprite",
          ApprenticeHeadRect,
          I18n.ApprenticeBuildingStatus
        )
      );
    }
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool showRobinBuildingStatus)
  {
    _helper.Events.GameLoop.DayStarted -= OnDayStarted;
    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.GameLoop.OneSecondUpdateTicked -= OnTickInRobinHouse;

    if (showRobinBuildingStatus)
    {
      // Logged here, not in UpdateBuildingStatusData: OnTickInRobinHouse calls it every second.
      UpdateBuildingStatusData();
      foreach (BuilderStatus builder in _builders)
      {
        ModEntry.MonitorObject.Log(
          $"ShowRobinBuildingStatusIcon: {builder.BuilderName} status updated, inProgress={builder.InProgress}",
          LogLevel.Trace
        );
      }

      _helper.Events.GameLoop.DayStarted += OnDayStarted;
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.GameLoop.OneSecondUpdateTicked += OnTickInRobinHouse;
    }
  }
  #endregion

  #region Event subscriptions
  public void OnTickInRobinHouse(object? sender, OneSecondUpdateTickedEventArgs e)
  {
    if (Game1.currentLocation?.Name != "ScienceHouse")
    {
      return;
    }

    UpdateBuildingStatusData();
  }

  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    UpdateBuildingStatusData();
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally())
    {
      return;
    }

    foreach (BuilderStatus builder in _builders)
    {
      if (!builder.InProgress || builder.IconSheet is null)
      {
        continue;
      }

      BuilderStatus current = builder;
      IconHandler.Handler.EnqueueIcon(
        current.IconKey,
        (batch, pos) =>
        {
          current.Icon.Value = new ClickableTextureComponent(
            new Rectangle(pos.X, pos.Y, 40, 40),
            current.IconSheet,
            current.HeadRect,
            8 / 3f
          );
          current.Icon.Value.draw(batch);
        },
        batch =>
        {
          if (
            (current.Icon.Value?.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ?? false)
            && !string.IsNullOrEmpty(current.HoverText)
          )
          {
            IClickableMenu.drawHoverText(batch, current.HoverText, Game1.smallFont);
          }
        }
      );
    }
  }
  #endregion

  #region Logic
  private void UpdateBuildingStatusData()
  {
    foreach (BuilderStatus builder in _builders)
    {
      if (GetBuilderMessage(builder, out builder.HoverText))
      {
        builder.InProgress = true;
        ResolveIconSheet(builder);
      }
      else
      {
        builder.InProgress = false;
      }
    }
  }

  private static bool GetBuilderMessage(BuilderStatus builder, out string hoverText)
  {
    if (builder.IsRobin)
    {
      int remainingDays = Game1.player.daysUntilHouseUpgrade.Value;
      if (remainingDays > 0)
      {
        hoverText = string.Format(I18n.RobinHouseUpgradeStatus(), remainingDays);
        return true;
      }
    }

    Building? building = Game1.GetBuildingUnderConstruction(builder.BuilderName);
    if (building is not null)
    {
      int days = Math.Max(building.daysOfConstructionLeft.Value, building.daysUntilUpgrade.Value);
      hoverText = string.Format(builder.BuildingStatusText(), days);
      return true;
    }

    hoverText = string.Empty;
    return false;
  }

  private void ResolveIconSheet(BuilderStatus builder)
  {
    if (builder.IsRobin)
    {
      Texture2D? robinTexture = Game1.getCharacterFromName("Robin")?.Sprite?.Texture;
      if (robinTexture != null)
      {
        builder.IconSheet = robinTexture;
      }
      else
      {
        ModEntry.MonitorObject.Log(
          "ShowRobinBuildingStatusIcon: Robin spritesheet not found",
          LogLevel.Warn
        );
      }

      return;
    }

    // Loaded fresh (not cached) so an asset invalidation can't leave a disposed texture behind.
    try
    {
      builder.IconSheet = Game1.content.Load<Texture2D>(builder.SpriteAsset!);
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"ShowRobinBuildingStatusIcon: could not load apprentice sprite '{builder.SpriteAsset}' - {ex.Message}",
        LogLevel.Trace
      );
    }
  }
  #endregion
}
