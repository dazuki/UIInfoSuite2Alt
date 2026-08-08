using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Quests;
using StardewValley.SpecialOrders;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowQuestLastDayReminder : IDisposable
{
  #region Properties
  // Clock glyph on mouseCursors, drawn over the quest journal icon (beside it on Android).
  private static readonly Rectangle ClockSourceRect = new(434, 475, 9, 9);
  private const float ClockScale = 3f;

  // Tiny digit counter. Keep this an integer so digits stay pixel perfect (3f matches ShowMailboxCount).
  private const float CounterScale = 2f;

  // Slow color flash between normal and a red tint (~2.5s full cycle at this period).
  private static readonly Color FlashColor = new(255, 100, 100);
  private const double FlashPeriodMs = 400.0;

  /// <summary>Gap above the clock when it stacks under the quest count badge on Android.</summary>
  private const int StackGap = 6;

  private static readonly int ClockSize = (int)(ClockSourceRect.Width * ClockScale);

  private readonly IModHelper _helper;
  private static bool _enabled;
  #endregion

  #region Lifecycle
  public ShowQuestLastDayReminder(IModHelper helper)
  {
    _helper = helper;
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool showReminder)
  {
    _helper.Events.Display.RenderedHud -= OnRenderedHud;
    _enabled = showReminder;

    if (showReminder)
    {
      _helper.Events.Display.RenderedHud += OnRenderedHud;
    }
  }

  /// <summary>Width the clock takes up in the badge column on Android, gap included. Zero on PC.</summary>
  internal static int GetAndroidSideWidth()
  {
    if (!AndroidHud.IsAndroid || !_enabled || !Game1.player.hasVisibleQuests)
    {
      return 0;
    }

    // Only reserves space when it takes the badge's slot; under the badge it is clear of the row
    if (ShowQuestCount.IsBadgeVisible || GetLastDayQuestCount() <= 0)
    {
      return 0;
    }

    return ClockSize + StackGap;
  }
  #endregion

  #region Event subscriptions
  // Draw after the HUD so the clock renders on top of the quest journal icon.
  private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally() || !Game1.player.hasVisibleQuests)
    {
      return;
    }

    int count = GetLastDayQuestCount();
    if (count <= 0)
    {
      return;
    }

    // Snap to a whole-pixel top-left (origin Vector2.Zero, integer scale) so every source pixel maps
    // to exactly ClockScale screen pixels - i.e. pixel perfect under PointClamp.
    Rectangle journal = Game1.dayTimeMoneyBox.questButton.bounds;
    float half = ClockSourceRect.Width * ClockScale / 2f;
    Vector2 clockPos;

    if (ShowQuestCount.TryGetAndroidSlot(out Vector2 slot, out int slotHeight))
    {
      // Under the count badge, or in its place when the badge is not drawn
      float centerY = ShowQuestCount.IsBadgeVisible
        ? slot.Y + slotHeight / 2f + StackGap + half
        : slot.Y;
      clockPos = new Vector2((int)Math.Round(slot.X - half), (int)Math.Round(centerY - half));
    }
    else
    {
      clockPos = new Vector2(
        (int)Math.Round(journal.X + 46 - half),
        (int)Math.Round(journal.Y + 23 - half)
      );
    }

    // journal bounds and the badge slot are in the date box's scaled space on Android
    bool scaled = AndroidHud.Begin(Game1.spriteBatch);

    float amount = (float)(
      (Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / FlashPeriodMs) + 1) / 2
    );
    Color color = Color.Lerp(Color.White, FlashColor, amount);

    // Shadow behind the clock.
    Game1.spriteBatch.Draw(
      Game1.mouseCursors,
      clockPos + new Vector2(0, 3),
      ClockSourceRect,
      Color.White * 0.3f,
      0f,
      Vector2.Zero,
      ClockScale,
      SpriteEffects.None,
      0.98f
    );

    Game1.spriteBatch.Draw(
      Game1.mouseCursors,
      clockPos,
      ClockSourceRect,
      color,
      0f,
      Vector2.Zero,
      ClockScale,
      SpriteEffects.None,
      0.99f
    );

    // Tiny counter over the clock, offset down-right. Drawn last so it sits on top.
    Vector2 counterPos = clockPos + new Vector2(16, 16);

    // Shadow: 2px down, white at 0.3 alpha, behind the digits.
    Utility.drawTinyDigits(
      count,
      Game1.spriteBatch,
      counterPos + new Vector2(0, 2),
      CounterScale,
      0.99f,
      Color.White * 0.3f
    );

    Utility.drawTinyDigits(
      count,
      Game1.spriteBatch,
      counterPos,
      CounterScale,
      1f,
      Color.White * 0.8f
    );

    var clockBounds = new Rectangle((int)clockPos.X, (int)clockPos.Y, ClockSize, ClockSize);
    bool hovered = AndroidHud.IsHovered(clockBounds);

    if (scaled)
    {
      AndroidHud.End(Game1.spriteBatch);
    }

    // Tooltip positions itself from the real cursor, so it draws outside the scaled batch
    if (hovered)
    {
      DrawLastDayTooltip(Game1.spriteBatch, GetLastDayQuestNames());
    }
  }

  // Custom tooltip so the header can be drawn in red - drawHoverText colors the whole body uniformly.
  private static void DrawLastDayTooltip(SpriteBatch b, List<string> questNames)
  {
    SpriteFont font = Game1.smallFont;
    const int padding = 16;
    string header = I18n.QuestLastDayTooltipHeader();
    float lineHeight = font.MeasureString("Wq").Y;

    float contentWidth = font.MeasureString(header).X;
    foreach (string name in questNames)
    {
      contentWidth = Math.Max(contentWidth, font.MeasureString(name).X);
    }

    int boxWidth = (int)contentWidth + padding * 2;
    int boxHeight = (int)(lineHeight * (questNames.Count + 1)) + padding * 2;

    int x = Math.Min(Game1.getMouseX() + 32, Game1.uiViewport.Width - boxWidth);
    int y = Math.Min(Game1.getMouseY() + 32, Game1.uiViewport.Height - boxHeight);

    IClickableMenu.drawTextureBox(
      b,
      Game1.menuTexture,
      new Rectangle(0, 256, 60, 60),
      x,
      y,
      boxWidth,
      boxHeight,
      Color.White
    );

    var textPos = new Vector2(x + padding, y + padding);
    Tools.DrawShadowedText(b, font, header, textPos, Tools.TooltipRed, Game1.textShadowColor);
    foreach (string name in questNames)
    {
      textPos.Y += lineHeight;
      Tools.DrawShadowedText(b, font, name, textPos, Game1.textColor, Game1.textShadowColor);
    }
  }
  #endregion

  #region Logic
  // Last day = value == 1 for both systems: journal quests decrement daysLeft at night and are
  // removed when <= 0; special orders are removed at newDay when GetDaysLeft() <= 0.
  private static int GetLastDayQuestCount()
  {
    return Game1.player.questLog.Count(IsLastDayQuest)
      + Game1.player.team.specialOrders.Count(IsLastDayOrder);
  }

  private static List<string> GetLastDayQuestNames()
  {
    List<string> names = Game1
      .player.questLog.Where(IsLastDayQuest)
      .Select(q => q.questTitle)
      .ToList();
    names.AddRange(
      Game1.player.team.specialOrders.Where(IsLastDayOrder).Select(so => so.GetName())
    );
    return names;
  }

  private static bool IsLastDayQuest(Quest quest) =>
    quest != null && quest.IsTimedQuest() && quest.daysLeft.Value == 1 && !quest.IsHidden();

  private static bool IsLastDayOrder(SpecialOrder order) =>
    order != null && !order.IsHidden() && order.GetDaysLeft() == 1;
  #endregion
}
