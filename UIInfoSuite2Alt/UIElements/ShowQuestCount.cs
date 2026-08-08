using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowQuestCount : IDisposable
{
  #region Properties
  private const float DigitScale = 3f;
  private static readonly Rectangle BgSourceRect = new(432, 439, 9, 9);
  private readonly IModHelper _helper;
  #endregion

  #region Lifecycle
  public ShowQuestCount(IModHelper helper)
  {
    _helper = helper;
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool showQuestCount)
  {
    _helper.Events.Display.RenderingHud -= OnRenderingHud;

    if (showQuestCount)
    {
      _helper.Events.Display.RenderingHud += OnRenderingHud;
    }
  }
  #endregion

  #region Event subscriptions
  // Draw background and number BEFORE HUD so journal icon renders on top
  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally() || !Game1.player.hasVisibleQuests)
    {
      return;
    }

    int questCount = GetVisibleQuestCount();
    if (questCount <= 0)
    {
      return;
    }

    Rectangle bounds = Game1.dayTimeMoneyBox.questButton.bounds;
    GetPositionAndSize(
      bounds,
      questCount,
      out float centerX,
      out float y,
      out int bgWidth,
      out int bgHeight
    );

    // Android questButton.bounds is in the date box's scaled space
    bool scaled = AndroidHud.Begin(Game1.spriteBatch);

    // Draw background
    var bgDest = new Rectangle(
      (int)(centerX - bgWidth / 2f),
      (int)(y - bgHeight / 2f) + 3,
      bgWidth,
      bgHeight
    );
    Game1.spriteBatch.Draw(Game1.mouseCursors, bgDest, BgSourceRect, Color.White);

    // Draw number centered on background
    int digitStringWidth = Utility.getWidthOfTinyDigitString(questCount, DigitScale);
    float numberX = centerX - digitStringWidth / 2f;
    float numberY = y - 8;

    Color questColor = new(255, 255, 255, 145);

    Utility.drawTinyDigits(
      questCount,
      Game1.spriteBatch,
      new Vector2(numberX, numberY),
      DigitScale,
      0.99f,
      questColor
    );

    if (scaled)
    {
      AndroidHud.End(Game1.spriteBatch);
    }
  }
  #endregion

  #region Logic
  private static int GetVisibleQuestCount()
  {
    return Game1.player.questLog.Count(q => q != null && !q.IsHidden())
      + Game1.player.team.specialOrders.Count(so => !so.IsHidden());
  }

  private static void GetPositionAndSize(
    Rectangle bounds,
    int questCount,
    out float centerX,
    out float y,
    out int bgWidth,
    out int bgHeight
  )
  {
    int scaledWidth = Utility.getWidthOfTinyDigitString(questCount, DigitScale);
    int scaledHeight = (int)(7f * DigitScale); // tinyDigits are 5x7px

    centerX = bounds.X + bounds.Width / 2f;
    y = bounds.Y + bounds.Height + 20;

    int padding = 6;
    bgWidth = scaledWidth + padding * 2 + 3;
    bgHeight = scaledHeight + padding * 2;
  }
  #endregion
}
