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
  private const int Padding = 6;
  private const int BackgroundHeight = (int)(7f * DigitScale) + Padding * 2; // tinyDigits are 5x7px

  /// <summary>Gap between the journal button and the badge beside it on Android.</summary>
  private const int SideGap = 8;

  /// <summary>How far the badge sits above the journal button's centre on Android.</summary>
  private const int SideRaise = 14;

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

  /// <summary>Whether the badge is on screen right now.</summary>
  internal static bool IsBadgeVisible =>
    IconHandler.Handler.ShowQuestCount
    && Game1.player.hasVisibleQuests
    && GetVisibleQuestCount() > 0;

  /// <summary>Width the badge takes up left of the journal button, gap included. Zero on PC.</summary>
  internal static int GetAndroidSideWidth()
  {
    if (!AndroidHud.IsAndroid || !IsBadgeVisible)
    {
      return 0;
    }

    return GetBackgroundWidth(GetVisibleQuestCount()) + SideGap;
  }

  /// <summary>
  ///   The badge's slot beside the journal button, whether or not it is drawn, so other elements can
  ///   stack under it or take its place. False on PC.
  /// </summary>
  internal static bool TryGetAndroidSlot(out Vector2 center, out int height)
  {
    center = Vector2.Zero;
    height = BackgroundHeight;

    if (!AndroidHud.IsAndroid || Game1.dayTimeMoneyBox?.questButton == null)
    {
      return false;
    }

    Rectangle bounds = Game1.dayTimeMoneyBox.questButton.bounds;
    int width = GetBackgroundWidth(Math.Max(1, GetVisibleQuestCount()));
    center = new Vector2(
      bounds.X - SideGap - width / 2f,
      bounds.Y + bounds.Height / 2f - SideRaise
    );
    return true;
  }

  private static int GetBackgroundWidth(int questCount)
  {
    return Utility.getWidthOfTinyDigitString(questCount, DigitScale) + Padding * 2 + 3;
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
    bgWidth = GetBackgroundWidth(questCount);
    bgHeight = BackgroundHeight;

    if (TryGetAndroidSlot(out Vector2 center, out _))
    {
      // Beside the journal button, since the menu button sits directly below it
      centerX = center.X;
      y = center.Y;
      return;
    }

    centerX = bounds.X + bounds.Width / 2f;
    y = bounds.Y + bounds.Height + 20;
  }
  #endregion
}
