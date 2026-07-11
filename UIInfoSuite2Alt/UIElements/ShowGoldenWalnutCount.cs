using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Locations;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowGoldenWalnutCount : IDisposable
{
  #region Properties
  private const float TextScale = 1f;
  private const float IconScale = 2f;
  private const float PanelScale = 2f;
  private const int IconSize = 16;
  private const int TotalWalnuts = 130;
  private const int QiDoorThreshold = 100;

  /// <summary>Scaled corner size (6px source * 2x scale). Content is inset by this amount.</summary>
  private const int CornerScaled = 12;
  private const int PanelGap = 8;

  /// <summary>Golden Walnut sprite on objectSpriteSheet (item 73).</summary>
  private static readonly Rectangle WalnutSourceRect = new(16, 48, 16, 16);

  /// <summary>Qi Gem sprite on objectSpriteSheet.</summary>
  private static readonly Rectangle QiGemSourceRect = new(288, 561, 15, 14);
  private const float QiGemScale = 2f;

  /// <summary>Ticks before fade starts after mouse leaves counter panel.</summary>
  private const int FadeDelayTicks = 60; // 1 second at 60fps

  /// <summary>Ticks for the fade-out animation.</summary>
  private const int FadeDurationTicks = 60; // 1 second
  private const float MinFadeAlpha = 0f;

  private readonly IModHelper _helper;
  private readonly Dictionary<string, WalnutInfo> _walnutData;
  private bool _showAnywhere;
  private bool _fadeOutEnabled;
  private readonly PerScreen<int> _fadeTimer = new();
  private readonly PerScreen<bool> _isHovering = new();
  #endregion

  #region Area Definitions
  /// <summary>Walnut tracking keys grouped by island area, matching stardew.app categories.</summary>
  private static readonly AreaDefinition[] Areas =
  {
    new(
      "IslandJungle",
      I18n.WalnutArea_IslandJungle,
      [
        "Bush_IslandEast_17_37",
        "Bush_IslandShrine_23_34",
        "BananaShrine",
        "IslandShrinePuzzle",
        "TreeNut",
      ]
    ),
    new(
      "IslandSouth",
      I18n.WalnutArea_IslandSouth,
      [
        "Bush_IslandSouth_31_5",
        "Buried_IslandSouthEast_25_17",
        "Buried_IslandSouthEastCave_36_26",
        "StardropPool",
        "Mermaid",
        "Darts",
      ]
    ),
    new(
      "IslandNorth",
      I18n.WalnutArea_IslandNorth,
      [
        "Bush_IslandNorth_13_33",
        "Bush_IslandNorth_5_30",
        "Bush_IslandNorth_4_42",
        "Bush_IslandNorth_45_38",
        "Bush_IslandNorth_47_40",
        "Bush_IslandNorth_20_26",
        "Bush_IslandNorth_9_84",
        "Bush_IslandNorth_56_27",
        "Buried_IslandNorth_57_79",
        "Buried_IslandNorth_19_39",
        "Buried_IslandNorth_19_13",
        "Buried_IslandNorth_54_21",
        "Buried_IslandNorth_42_77",
        "Buried_IslandNorth_62_54",
        "Buried_IslandNorth_26_81",
        "TreeNutShot",
        "Island_N_BuriedTreasureNut",
      ]
    ),
    new(
      "IslandWest",
      I18n.WalnutArea_IslandWest,
      [
        "Bush_IslandWest_104_3",
        "Bush_IslandWest_31_24",
        "Bush_IslandWest_38_56",
        "Bush_IslandWest_75_29",
        "Bush_IslandWest_64_30",
        "Bush_IslandWest_54_18",
        "Bush_IslandWest_25_30",
        "Bush_IslandWest_15_3",
        "Bush_CaptainRoom_2_4",
        "Buried_IslandWest_21_81",
        "Buried_IslandWest_62_76",
        "Buried_IslandWest_39_24",
        "Buried_IslandWest_88_14",
        "Buried_IslandWest_43_74",
        "Buried_IslandWest_30_75",
        "IslandWestCavePuzzle",
        "SandDuggy",
        "TigerSlimeNut",
        "Island_W_BuriedTreasureNut",
        "Island_W_BuriedTreasureNut2",
        "MusselStone",
      ]
    ),
    new(
      "Volcano",
      I18n.WalnutArea_Volcano,
      [
        "Bush_Caldera_28_36",
        "Bush_Caldera_9_34",
        "VolcanoNormalChest",
        "VolcanoRareChest",
        "VolcanoBarrel",
        "VolcanoMining",
        "VolcanoMonsterDrop",
      ]
    ),
    new("Fishing", I18n.WalnutArea_Fishing, ["IslandFishing"]),
    new("Farming", I18n.WalnutArea_Farming, ["IslandFarming"]),
    new(
      "FieldOffice",
      I18n.WalnutArea_FieldOffice,
      [
        "IslandLeftPlantRestored",
        "IslandRightPlantRestored",
        "IslandCenterSkeletonRestored",
        "IslandSnakeRestored",
        "IslandBatRestored",
        "IslandFrogRestored",
      ]
    ),
    new("GoldenCoconut", I18n.WalnutArea_GoldenCoconut, ["GoldenCoconut"]),
    new(
      "GourmandFrog",
      I18n.WalnutArea_GourmandFrog,
      ["IslandGourmand1", "IslandGourmand2", "IslandGourmand3"]
    ),
    new("PiratesWife", I18n.WalnutArea_PiratesWife, ["Birdie"]),
  };

  private record WalnutInfo(int Count, string Name, string Description);

  private record AreaDefinition(string Id, Func<string> DisplayName, string[] Keys);
  #endregion

  #region Lifecycle
  public ShowGoldenWalnutCount(IModHelper helper)
  {
    _helper = helper;
    _walnutData =
      helper.Data.ReadJsonFile<Dictionary<string, WalnutInfo>>("assets/walnuts.json")
      ?? new Dictionary<string, WalnutInfo>();
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool enabled)
  {
    _helper.Events.Display.RenderedHud -= OnRenderedHud;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;

    if (enabled)
    {
      _helper.Events.Display.RenderedHud += OnRenderedHud;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    }
  }

  public void ToggleShowAnywhere(bool enabled)
  {
    _showAnywhere = enabled;
  }

  public void ToggleFadeOut(bool enabled)
  {
    _fadeOutEnabled = enabled;
    if (!enabled)
    {
      _fadeTimer.Value = 0;
    }
  }
  #endregion

  #region Event subscriptions
  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (!_fadeOutEnabled || _isHovering.Value)
    {
      return;
    }

    if (_fadeTimer.Value < FadeDelayTicks + FadeDurationTicks)
    {
      _fadeTimer.Value++;
    }
  }

  private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally())
    {
      return;
    }

    // Only show after first parrot walnut has been given
    if (!Game1.MasterPlayer.hasOrWillReceiveMail("Island_FirstParrot"))
    {
      return;
    }

    // Type check, not "Island" context: mods (SVE) reuse that context for non-island maps
    if (!_showAnywhere && Game1.currentLocation is not IslandLocation)
    {
      return;
    }

    int found = Game1.netWorldState.Value.GoldenWalnutsFound;

    // Always compute counter rect for hover detection, even when fully faded
    float alpha = GetCurrentAlpha();
    Rectangle counterRect = DrawCounterPanel(e.SpriteBatch, found, alpha);

    // Track hover state for fade logic
    bool hovering = counterRect.Contains(Game1.getOldMouseX(), Game1.getOldMouseY());
    if (hovering)
    {
      _isHovering.Value = true;
      _fadeTimer.Value = 0;

      // Redraw at full opacity if it was faded
      if (alpha < 1f)
      {
        DrawCounterPanel(e.SpriteBatch, found, 1f);
      }

      DrawInfoPanel(e.SpriteBatch, found, counterRect);
    }
    else
    {
      _isHovering.Value = false;
    }
  }

  private float GetCurrentAlpha()
  {
    if (!_fadeOutEnabled)
    {
      return 1f;
    }

    if (_fadeTimer.Value < FadeDelayTicks)
    {
      return 1f;
    }

    int fadeTicks = _fadeTimer.Value - FadeDelayTicks;
    if (fadeTicks >= FadeDurationTicks)
    {
      return MinFadeAlpha;
    }

    return 1f - (float)fadeTicks / FadeDurationTicks;
  }
  #endregion

  #region Logic
  private static float GetTopLeftYOffset()
  {
    if (Game1.currentLocation is VolcanoDungeon volcano && volcano.level.Value > 0)
    {
      return 16 + SpriteText.getHeightOfString("9") + PanelGap;
    }
    return 8f;
  }

  private int GetWalnutCount(string key)
  {
    return _walnutData.TryGetValue(key, out WalnutInfo? info) ? info.Count : 1;
  }

  private int GetAreaTotal(AreaDefinition area)
  {
    int total = 0;
    foreach (string key in area.Keys)
    {
      total += GetWalnutCount(key);
    }
    return total;
  }

  private int GetCollectedForArea(AreaDefinition area)
  {
    var tracker = Game1.player.team.collectedNutTracker;
    var limitedDrops = Game1.player.team.limitedNutDrops;
    int count = 0;

    foreach (string key in area.Keys)
    {
      // GoldenCoconut uses a dedicated flag, not collectedNutTracker
      if (key == "GoldenCoconut")
      {
        if (Game1.netWorldState.Value.GoldenCoconutCracked)
        {
          count += GetWalnutCount(key);
        }
        continue;
      }

      if (tracker.Contains(key))
      {
        // limitedNutDrops tracks actual count for repeatable activities
        if (limitedDrops.TryGetValue(key, out int dropCount))
        {
          count += dropCount;
        }
        else
        {
          count += GetWalnutCount(key);
        }
      }
      else if (limitedDrops.TryGetValue(key, out int dropCount))
      {
        // Some limited drops aren't in collectedNutTracker
        count += dropCount;
      }
    }

    return Math.Min(count, GetAreaTotal(area));
  }
  #endregion

  #region Rendering
  private Rectangle DrawCounterPanel(SpriteBatch batch, int found, float alpha)
  {
    string text = $"{found}/{TotalWalnuts}";
    Vector2 textSize = Game1.smallFont.MeasureString(text) * TextScale;

    int scaledIconSize = (int)(IconSize * IconScale);
    int gap = 4;
    int contentWidth = scaledIconSize + gap + (int)textSize.X;
    int contentHeight = Math.Max(scaledIconSize, (int)textSize.Y);
    int panelWidth = contentWidth + CornerScaled * 2;
    int panelHeight = contentHeight + CornerScaled * 2;

    // Top-left corner with margin; shift down when in Volcano dungeon to avoid overlapping the level counter
    float panelX = 8f;
    float panelY = GetTopLeftYOffset();

    var dest = new Rectangle((int)panelX, (int)panelY, panelWidth, panelHeight);
    NineSlice.Draw(batch, dest, PanelScale, 0.89f, Color.White * 0.9f * alpha);

    float iconX = panelX + CornerScaled;
    float iconY = panelY + CornerScaled + (contentHeight - scaledIconSize) / 2f;
    batch.Draw(
      Game1.objectSpriteSheet,
      new Vector2(iconX, iconY),
      WalnutSourceRect,
      Color.White * alpha,
      0f,
      Vector2.Zero,
      IconScale,
      SpriteEffects.None,
      0.9f
    );

    float textX = iconX + scaledIconSize + gap;
    float textY = panelY + CornerScaled + (contentHeight - textSize.Y) / 2f + 3f;
    DrawTextWithShadow(batch, text, new Vector2(textX, textY), Game1.textColor * alpha, 40);

    return dest;
  }

  private void DrawInfoPanel(SpriteBatch batch, int found, Rectangle counterRect)
  {
    // Qi's door uses found - 1 (first walnut not counted)
    int qiCount = Math.Min(Math.Max(0, found - 1), QiDoorThreshold);

    // Qi's Walnut Room line
    float qiGemWidth = QiGemSourceRect.Width * QiGemScale + 4; // gem + gap
    string qiLabel = I18n.WalnutArea_QiWalnutRoom();
    bool qiComplete = qiCount >= QiDoorThreshold;
    Color qiCountColor = qiComplete ? Tools.TooltipWalnutGreen : Tools.TooltipWalnutYellow;
    string qiCollectedText = $"{qiCount}";
    string qiTotalText = $"/{QiDoorThreshold}";
    float qiNameWidth = qiGemWidth + Game1.smallFont.MeasureString(qiLabel).X * TextScale;
    float qiCountWidth = qiComplete
      ? Game1.smallFont.MeasureString(qiCollectedText).X * TextScale
      : (
        Game1.smallFont.MeasureString(qiCollectedText).X
        + Game1.smallFont.MeasureString(qiTotalText).X
      ) * TextScale;

    // Area breakdowns
    var areaLines =
      new List<(
        string id,
        string name,
        string collectedText,
        string totalText,
        Color countColor,
        bool complete
      )>();
    float maxNameWidth = qiNameWidth;
    float maxCountWidth = qiCountWidth;

    foreach (AreaDefinition area in Areas)
    {
      int areaTotal = GetAreaTotal(area);
      int collected = GetCollectedForArea(area);
      bool complete = collected >= areaTotal;
      Color countColor = complete ? Tools.TooltipWalnutGreen : Tools.TooltipWalnutYellow;
      string collectedText = $"{collected}";
      string totalText = $"/{areaTotal}";
      float nameWidth = Game1.smallFont.MeasureString(area.DisplayName()).X * TextScale;
      float countWidth = complete
        ? Game1.smallFont.MeasureString(collectedText).X * TextScale
        : (
          Game1.smallFont.MeasureString(collectedText).X
          + Game1.smallFont.MeasureString(totalText).X
        ) * TextScale;
      areaLines.Add((area.Id, area.DisplayName(), collectedText, totalText, countColor, complete));
      maxNameWidth = Math.Max(maxNameWidth, nameWidth);
      maxCountWidth = Math.Max(maxCountWidth, countWidth);
    }

    float lineHeight = Game1.smallFont.MeasureString("X").Y * TextScale;
    int lineSpacing = 4;
    int columnGap = 12;
    float maxLineWidth = maxNameWidth + columnGap + maxCountWidth;
    float separatorHeight = (lineHeight / 2) + lineSpacing;
    float totalLineHeight =
      lineHeight
      + lineSpacing
      + separatorHeight
      + separatorHeight
      + (areaLines.Count * lineHeight)
      + ((areaLines.Count) * lineSpacing);

    int panelWidth = (int)maxLineWidth + CornerScaled * 2;
    int panelHeight = (int)Math.Ceiling(totalLineHeight) + CornerScaled * 2;

    float panelX = counterRect.X;
    float panelY = counterRect.Bottom + PanelGap;

    var dest = new Rectangle((int)panelX, (int)panelY, panelWidth, panelHeight);
    NineSlice.Draw(batch, dest, PanelScale, 0.89f, Color.White * 0.9f);

    float rightEdge = panelX + panelWidth - CornerScaled;

    // Draw Qi line: [gem] Qi's Walnut Room    (color)X(/black)/100
    float lineY = panelY + CornerScaled + 3f;
    float segX = panelX + CornerScaled;
    float gemY = lineY + (lineHeight - QiGemSourceRect.Height * QiGemScale) / 2f - 2f;
    batch.Draw(
      Game1.objectSpriteSheet,
      new Vector2(segX, gemY),
      QiGemSourceRect,
      Color.White,
      0f,
      Vector2.Zero,
      QiGemScale,
      SpriteEffects.None,
      0.9f
    );
    segX += qiGemWidth;
    DrawTextWithShadow(batch, qiLabel, new Vector2(segX, lineY), Game1.textColor, 40);

    if (qiComplete)
    {
      float cw = Game1.smallFont.MeasureString(qiCollectedText).X * TextScale;
      DrawTextWithShadow(
        batch,
        qiCollectedText,
        new Vector2(rightEdge - cw, lineY),
        qiCountColor,
        40
      );
    }
    else
    {
      float fw =
        (
          Game1.smallFont.MeasureString(qiCollectedText).X
          + Game1.smallFont.MeasureString(qiTotalText).X
        ) * TextScale;
      float cx = rightEdge - fw;
      DrawTextWithShadow(batch, qiCollectedText, new Vector2(cx, lineY), qiCountColor, 40);
      cx += Game1.smallFont.MeasureString(qiCollectedText).X * TextScale;
      DrawTextWithShadow(batch, qiTotalText, new Vector2(cx, lineY), Game1.textColor, 40);
    }
    lineY += lineHeight + lineSpacing;

    // Empty separator line
    lineY += (lineHeight / 2) + lineSpacing;

    // Draw area lines: name left-aligned, count right-aligned
    foreach (
      (
        string id,
        string name,
        string collectedText,
        string totalText,
        Color countColor,
        bool complete
      ) in areaLines
    )
    {
      Color nameColor = complete ? Tools.TooltipWalnutGreen : Game1.textColor;
      DrawTextWithShadow(batch, name, new Vector2(panelX + CornerScaled, lineY), nameColor, 40);

      if (complete)
      {
        float countWidth = Game1.smallFont.MeasureString(collectedText).X * TextScale;
        DrawTextWithShadow(
          batch,
          collectedText,
          new Vector2(rightEdge - countWidth, lineY),
          countColor,
          40
        );
      }
      else
      {
        float fullWidth =
          (
            Game1.smallFont.MeasureString(collectedText).X
            + Game1.smallFont.MeasureString(totalText).X
          ) * TextScale;
        float countX = rightEdge - fullWidth;
        DrawTextWithShadow(batch, collectedText, new Vector2(countX, lineY), countColor, 40);
        countX += Game1.smallFont.MeasureString(collectedText).X * TextScale;
        DrawTextWithShadow(batch, totalText, new Vector2(countX, lineY), Game1.textColor, 40);
      }

      lineY += lineHeight + lineSpacing;

      // Add separator after Volcano (geographic vs activity areas)
      if (id == "IslandWest")
      {
        lineY += separatorHeight;
      }
    }
  }

  private static void DrawTextWithShadow(
    SpriteBatch batch,
    string text,
    Vector2 position,
    Color color,
    int shadowAlpha = 120
  )
  {
    float alphaRatio = color.A / 255f;
    batch.DrawString(
      Game1.smallFont,
      text,
      position + new Vector2(1f, 1f),
      new Color(0, 0, 0, (int)(shadowAlpha * alphaRatio)),
      0f,
      Vector2.Zero,
      TextScale,
      SpriteEffects.None,
      0.89f
    );
    batch.DrawString(
      Game1.smallFont,
      text,
      position,
      color,
      0f,
      Vector2.Zero,
      TextScale,
      SpriteEffects.None,
      0.9f
    );
  }
  #endregion
}
