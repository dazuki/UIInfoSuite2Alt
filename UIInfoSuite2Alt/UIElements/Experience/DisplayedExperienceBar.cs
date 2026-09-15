using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements.Experience;

public class DisplayedExperienceBar
{
  // private const int DefaultBoxWidth = 240;
  private const int WideBoxWidth = 310;

  // private const int DefaultMaxBarWidth = 175;
  private const int WideMaxBarWidth = 245;

  public void Draw(
    Color experienceFillColor,
    Rectangle experienceIconPosition,
    int experienceEarnedThisLevel,
    int experienceDifferenceBetweenLevels,
    int currentLevel,
    Texture2D? iconTexture = null,
    bool isWide = false,
    float iconScale = 2.9f,
    int yOffset = 0,
    int accumulatedExperience = 0,
    float comboAlpha = 1f,
    int comboShakeTicks = 0
  )
  {
    //int maxBarWidth = isWide ? WideMaxBarWidth : DefaultMaxBarWidth;
    int maxBarWidth = WideMaxBarWidth;
    //int boxWidth = isWide ? WideBoxWidth : DefaultBoxWidth;
    int boxWidth = WideBoxWidth;
    int barWidth = GetBarWidth(
      experienceEarnedThisLevel,
      experienceDifferenceBetweenLevels,
      maxBarWidth
    );
    yOffset += AndroidHud.ToolbarBottomInset;
    float leftSide = GetExperienceBarLeftSide();
    int bottom = Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea.Bottom - yOffset;

    Game1.drawDialogueBox((int)leftSide, bottom - 160, boxWidth, 160, false, true);

    Game1.spriteBatch.Draw(
      Game1.staminaRect,
      new Rectangle((int)leftSide + 32, bottom - 64, barWidth, 31),
      experienceFillColor
    );

    Game1.spriteBatch.Draw(
      Game1.staminaRect,
      new Rectangle((int)leftSide + 32, bottom - 64, Math.Min(4, barWidth), 31),
      experienceFillColor
    );

    Game1.spriteBatch.Draw(
      Game1.staminaRect,
      new Rectangle((int)leftSide + 32, bottom - 64, barWidth, 4),
      experienceFillColor
    );

    Game1.spriteBatch.Draw(
      Game1.staminaRect,
      new Rectangle((int)leftSide + 32, bottom - 36, barWidth, 4),
      experienceFillColor
    );

    if (IsMouseOverExperienceBar(leftSide, boxWidth, yOffset))
    {
      Vector2 pos = new Vector2(leftSide + 36, bottom - 62);

      string text = experienceEarnedThisLevel + "/" + experienceDifferenceBetweenLevels;

      // Shadow
      Game1.spriteBatch.DrawString(
        Game1.smallFont,
        text,
        pos + new Vector2(1f, 1f),
        Color.Black * 0.4f
      );

      // Text
      Game1.spriteBatch.DrawString(Game1.smallFont, text, pos, new Color(28, 28, 28, 255));
    }
    else
    {
      string levelText = currentLevel.ToString();
      float levelTextWidth = Game1.smallFont.MeasureString(levelText).X;
      float iconX = leftSide + 38 + levelTextWidth;

      Game1.spriteBatch.Draw(
        iconTexture ?? Game1.mouseCursors,
        new Vector2(iconX, bottom - 62),
        experienceIconPosition,
        Color.White,
        0,
        Vector2.Zero,
        iconScale,
        SpriteEffects.None,
        0.85f
      );

      Vector2 levelPos = new Vector2(leftSide + 36, bottom - 62);

      // Shadow
      Game1.spriteBatch.DrawString(
        Game1.smallFont,
        levelText,
        levelPos + new Vector2(1f, 1f),
        Color.Black * 0.4f
      );

      // Text
      Game1.spriteBatch.DrawString(
        Game1.smallFont,
        levelText,
        levelPos,
        new Color(28, 28, 28, 255)
      );
    }

    // Accumulated XP counter (right-aligned, with fade and shake)
    if (accumulatedExperience > 0 && comboAlpha > 0f)
    {
      string comboText = "+" + accumulatedExperience;
      float textWidth = Game1.smallFont.MeasureString(comboText).X;
      float rightEdge = leftSide + 32 + maxBarWidth;

      float shakeX =
        comboShakeTicks > 0 ? MathF.Sin(comboShakeTicks * 1.5f) * (comboShakeTicks / 15f) * 2f : 0f;

      Vector2 comboPos = new Vector2(rightEdge - textWidth - 12 + shakeX, bottom - 62);

      // Shadow
      Game1.spriteBatch.DrawString(
        Game1.smallFont,
        comboText,
        comboPos + new Vector2(1f, 1f),
        Color.Black * (0.4f * comboAlpha)
      );

      // Text
      Game1.spriteBatch.DrawString(
        Game1.smallFont,
        comboText,
        comboPos,
        new Color(28, 28, 28, 255) * comboAlpha
      );
    }
  }

  #region Static helpers
  private static int GetBarWidth(
    int experienceEarnedThisLevel,
    int experienceDifferenceBetweenLevels,
    int maxBarWidth
  )
  {
    if (experienceDifferenceBetweenLevels <= 0)
    {
      return maxBarWidth;
    }

    return (int)(
      (double)experienceEarnedThisLevel / experienceDifferenceBetweenLevels * maxBarWidth
    );
  }

  private static float GetExperienceBarLeftSide()
  {
    float leftSide =
      Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea.Left + AndroidHud.ToolbarLeftInset;

    if (Game1.isOutdoorMapSmallerThanViewport())
    {
      int num3 = Game1.currentLocation.map.Layers[0].LayerWidth * Game1.tileSize;
      leftSide += (Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea.Right - num3) / 2;
    }

    return leftSide;
  }

  private static bool IsMouseOverExperienceBar(float leftSide, int boxWidth, int yOffset = 0)
  {
    int mouseX = Game1.getMouseX();
    int mouseY = Game1.getMouseY();
    int x = (int)leftSide - 36;
    int bottom = Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea.Bottom - yOffset;
    int y = bottom - 66;
    return mouseX >= x && mouseX < x + boxWidth + 20 && mouseY >= y && mouseY < bottom;
  }
  #endregion
}
