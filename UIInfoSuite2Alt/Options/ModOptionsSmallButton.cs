using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace UIInfoSuite2Alt.Options;

internal class ModOptionsSmallButton : ModOptionsElement
{
  private readonly Action _onClick;
  private readonly bool _isCentered;
  private readonly bool _playClickSound;
  private bool _boundsInitialized;

  public ModOptionsSmallButton(
    string label,
    int whichOption,
    Action onClick,
    bool isCentered = false,
    bool playClickSound = true
  )
    : base(label, whichOption)
  {
    _onClick = onClick;
    _isCentered = isCentered;
    _playClickSound = playClickSound;
  }

  private void EnsureBounds()
  {
    if (_boundsInitialized)
    {
      return;
    }

    _boundsInitialized = true;
    var textSize = Game1.smallFont.MeasureString(_label);
    int buttonWidth = (int)textSize.X + 64;
    int buttonX = Bounds.X;

    if (_isCentered)
    {
      int slotWidth = Game1.activeClickableMenu?.width ?? Game1.uiViewport.Width;
      buttonX = (slotWidth - Game1.tileSize / 2 - buttonWidth) / 2;
    }

    Bounds = new Rectangle(buttonX, Bounds.Y, buttonWidth, (int)textSize.Y + 20);
  }

  public override int Height
  {
    get
    {
      EnsureBounds();
      int slotHeight =
        Game1.activeClickableMenu != null
          ? (Game1.activeClickableMenu.height - Game1.tileSize * 2) / 7 + Game1.pixelZoom
          : 68;
      return System.Math.Max(slotHeight, Bounds.Height + Game1.pixelZoom * 2 + Bounds.Y);
    }
  }

  public override void ReceiveLeftClick(int x, int y)
  {
    if (Bounds.Contains(x, y))
    {
      if (_playClickSound)
      {
        Game1.playSound("drumkit6");
      }

      _onClick();
    }
  }

  public override void Draw(SpriteBatch batch, int slotX, int slotY)
  {
    EnsureBounds();

    float layerDepth = 0.8f - (slotY + Bounds.Y) * 1E-06f;

    IClickableMenu.drawTextureBox(
      batch,
      Game1.mouseCursors,
      new Rectangle(432, 439, 9, 9),
      slotX + Bounds.X,
      slotY + Bounds.Y,
      Bounds.Width,
      Bounds.Height,
      Color.White,
      4f,
      false,
      layerDepth
    );

    var textSize = Game1.smallFont.MeasureString(_label) / 2f;
    textSize.X = (int)(textSize.X / 4f) * 4;
    textSize.Y = (int)(textSize.Y / 4f) * 4;

    Utility.drawTextWithShadow(
      batch,
      _label,
      Game1.smallFont,
      new Vector2(slotX + Bounds.Center.X, slotY + Bounds.Center.Y) - textSize,
      Game1.textColor,
      1f,
      layerDepth + 1E-06f,
      -1,
      -1,
      0f
    );
  }

  public override Point? GetRelativeSnapPoint(Rectangle slotBounds)
  {
    EnsureBounds();
    return new Point(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
  }
}
