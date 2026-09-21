using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace UIInfoSuite2Alt.UIElements.Menus;

/// <summary>Yes/no prompt shown as a child menu, so closing it returns to the menu underneath.</summary>
internal class ConfirmDialog : IClickableMenu
{
  private const int OkSnapId = 101;
  private const int CancelSnapId = 102;
  private const int MenuPadding = 32;
  private const int TitleBottomMargin = 16;
  private const int ButtonSize = 64;
  private const int ButtonTopMargin = 24;
  private const int MaxTextWidth = 640;

  private readonly string _title;
  private readonly Action _onConfirm;
  private string _message = "";
  private ClickableTextureComponent _okButton = null!;
  private ClickableTextureComponent _cancelButton = null!;

  public ConfirmDialog(string title, string message, Action onConfirm)
    : base(0, 0, 0, 0)
  {
    _title = title;
    _onConfirm = onConfirm;

    BuildLayout(message);
    Game1.playSound("bigSelect");
  }

  private void BuildLayout(string rawMessage)
  {
    SpriteFont font = Game1.dialogueFont;
    _message = Game1.parseText(rawMessage, font, MaxTextWidth);

    Vector2 messageSize = font.MeasureString(_message);
    int titleWidth = SpriteText.getWidthOfString(_title);
    int titleHeight = SpriteText.getHeightOfString(_title);
    int contentWidth = Math.Max(titleWidth, (int)messageSize.X);

    width = contentWidth + MenuPadding * 2 + borderWidth * 2;
    height =
      titleHeight
      + TitleBottomMargin
      + (int)messageSize.Y
      + ButtonTopMargin
      + ButtonSize
      + MenuPadding * 2
      + borderWidth * 2;

    Vector2 center = Utility.getTopLeftPositionForCenteringOnScreen(width, height);
    xPositionOnScreen = (int)center.X;
    yPositionOnScreen = (int)center.Y;

    int buttonY =
      yPositionOnScreen + height - borderWidth - MenuPadding - ButtonSize + Game1.pixelZoom * 2;
    int buttonsRight = xPositionOnScreen + width - borderWidth - MenuPadding;

    _okButton = new ClickableTextureComponent(
      "OK",
      new Rectangle(buttonsRight - ButtonSize * 2 - 8, buttonY, ButtonSize, ButtonSize),
      null,
      null,
      Game1.mouseCursors,
      Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 46),
      1f
    )
    {
      myID = OkSnapId,
      rightNeighborID = CancelSnapId,
      leftNeighborID = -99998,
    };

    _cancelButton = new ClickableTextureComponent(
      "Cancel",
      new Rectangle(buttonsRight - ButtonSize, buttonY, ButtonSize, ButtonSize),
      null,
      null,
      Game1.mouseCursors,
      Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 47),
      1f
    )
    {
      myID = CancelSnapId,
      leftNeighborID = OkSnapId,
      rightNeighborID = -99998,
    };

    populateClickableComponentList();
    allClickableComponents.Add(_okButton);
    allClickableComponents.Add(_cancelButton);
  }

  public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
  {
    base.gameWindowSizeChanged(oldBounds, newBounds);
    BuildLayout(_message);
  }

  public override void snapToDefaultClickableComponent()
  {
    currentlySnappedComponent = _cancelButton;
    snapCursorToCurrentSnappedComponent();
  }

  public override void performHoverAction(int x, int y)
  {
    base.performHoverAction(x, y);
    _okButton.tryHover(x, y);
    _cancelButton.tryHover(x, y);
  }

  public override void receiveLeftClick(int x, int y, bool playSound = true)
  {
    if (_okButton.containsPoint(x, y))
    {
      Confirm();
    }
    else if (_cancelButton.containsPoint(x, y))
    {
      Cancel();
    }
  }

  public override void receiveKeyPress(Keys key)
  {
    if (Game1.options.doesInputListContain(Game1.options.menuButton, key))
    {
      Cancel();
      return;
    }

    base.receiveKeyPress(key);
  }

  public override void receiveGamePadButton(Buttons b)
  {
    base.receiveGamePadButton(b);

    if (b == Buttons.B)
    {
      Cancel();
    }
    else if (b == Buttons.A && currentlySnappedComponent != null)
    {
      if (currentlySnappedComponent.myID == OkSnapId)
      {
        Confirm();
      }
      else if (currentlySnappedComponent.myID == CancelSnapId)
      {
        Cancel();
      }
    }
  }

  private void Confirm()
  {
    // Close first, so the confirmed action sees the menu underneath rather than this dialog
    exitThisMenu();
    _onConfirm();
  }

  private void Cancel()
  {
    exitThisMenu();
  }

  public override void draw(SpriteBatch b)
  {
    b.Draw(
      Game1.fadeToBlackRect,
      Game1.graphics.GraphicsDevice.Viewport.Bounds,
      Color.Black * 0.75f
    );

    drawTextureBox(
      b,
      Game1.menuTexture,
      new Rectangle(0, 256, 60, 60),
      xPositionOnScreen,
      yPositionOnScreen,
      width,
      height,
      Color.White
    );

    int contentX = xPositionOnScreen + borderWidth + MenuPadding;
    int contentY = yPositionOnScreen + borderWidth + MenuPadding;

    SpriteText.drawString(b, _title, contentX, contentY);

    int titleHeight = SpriteText.getHeightOfString(_title);
    Utility.drawTextWithShadow(
      b,
      _message,
      Game1.dialogueFont,
      new Vector2(contentX, contentY + titleHeight + TitleBottomMargin),
      Game1.textColor
    );

    _okButton.draw(b);
    _cancelButton.draw(b);

    base.draw(b);
    drawMouse(b);
  }
}
