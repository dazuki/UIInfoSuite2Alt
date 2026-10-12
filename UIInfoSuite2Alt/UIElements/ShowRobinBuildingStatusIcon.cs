using System;
using System.Collections.Generic;
using System.Linq;
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

  private sealed class FlexibleProject
  {
    public string Kind = "";
    public Texture2D? IconSheet;
    public string HoverText = "";
    public ClickableTextureComponent? Icon;
  }
  #endregion

  #region Properties
  // 1px edits for better alignment with other icons
  private static readonly Rectangle RobinHeadRect = new(0, 196, 15, 14);

  private static readonly Rectangle ApprenticeHeadRect = new(48, 36, 13, 14);

  private readonly List<BuilderStatus> _builders = new();
  private readonly PerScreen<List<FlexibleProject>> _flexibleProjects = new(() => []);
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
    _flexibleProjects.Value.Clear();
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
      UpdateFlexibleProjects();
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
          if (AndroidHud.IsHovered(current.Icon.Value) && !string.IsNullOrEmpty(current.HoverText))
          {
            IClickableMenu.drawHoverText(batch, current.HoverText, Game1.smallFont);
          }
        }
      );
    }

    foreach (FlexibleProject project in _flexibleProjects.Value)
    {
      if (project.IconSheet is null)
      {
        continue;
      }

      IconHandler.Handler.EnqueueIcon(
        "RobinBuilding",
        (batch, pos) =>
        {
          project.Icon = new ClickableTextureComponent(
            new Rectangle(pos.X, pos.Y, 40, 40),
            project.IconSheet,
            RobinHeadRect,
            8 / 3f
          );
          project.Icon.draw(batch);
        },
        batch =>
        {
          if (AndroidHud.IsHovered(project.Icon))
          {
            IClickableMenu.drawHoverText(batch, project.HoverText, Game1.smallFont);
          }
        }
      );
    }
  }
  #endregion

  #region Logic
  private void UpdateBuildingStatusData()
  {
    UpdateFlexibleProjects();

    bool flexibleBuildings = _flexibleProjects.Value.Any(project => project.Kind != "Community");

    foreach (BuilderStatus builder in _builders)
    {
      if (builder.IsRobin && flexibleBuildings)
      {
        builder.InProgress = false;
        continue;
      }

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

  private void UpdateFlexibleProjects()
  {
    _flexibleProjects.Value.Clear();

    if (!ApiManager.GetApi(ModCompat.FlexibleUpgrades, out IFlexibleUpgradesApi? api))
    {
      return;
    }

    Texture2D? robinSheet = ResolveRobinSheet();
    if (robinSheet is null)
    {
      return;
    }

    foreach (var project in api.GetRobinProjects())
    {
      if (project.Kind == "House")
      {
        continue;
      }

      _flexibleProjects.Value.Add(
        new FlexibleProject
        {
          Kind = project.Kind,
          IconSheet = robinSheet,
          HoverText = string.Format(I18n.FlexibleBuildingStatus(), project.Name, project.DaysLeft),
        }
      );
    }
  }

  private static Texture2D? ResolveRobinSheet()
  {
    Texture2D? robinTexture = Game1.getCharacterFromName("Robin")?.Sprite?.Texture;
    if (robinTexture == null)
    {
      ModEntry.MonitorObject.Log(
        "ShowRobinBuildingStatusIcon: Robin spritesheet not found",
        LogLevel.Warn
      );
    }

    return robinTexture;
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
      builder.IconSheet = ResolveRobinSheet();
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
