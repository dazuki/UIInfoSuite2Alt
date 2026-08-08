using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements.Menus;

internal class SpecialOrdersBoardSelector : IClickableMenu
{
  private const int BaseSnapId = 88880;
  private const int RowHeight = 64;
  private const int MenuPadding = 32;
  private const int TitleBottomMargin = 16;

  private readonly record struct BoardOption(string BoardType, string DisplayName);

  private readonly List<BoardOption> _options = [];
  private readonly List<ClickableComponent> _optionComponents = [];
  private readonly Action<string>? _onBoardSelected;
  private readonly HashSet<string> _viewedBoardTypes;
  private readonly bool _returnToInventory;
  private int _hoveredIndex = -1;

  public SpecialOrdersBoardSelector(
    List<(string BoardType, string DisplayName)> modBoards,
    Action<string>? onBoardSelected = null,
    HashSet<string>? viewedBoardTypes = null,
    bool returnToInventory = false
  )
    : base(0, 0, 0, 0, showUpperRightCloseButton: true)
  {
    _onBoardSelected = onBoardSelected;
    _viewedBoardTypes = viewedBoardTypes ?? [];
    _returnToInventory = returnToInventory;

    // Vanilla first, then mod boards
    _options.Add(new BoardOption("", I18n.SpecialOrdersVanilla()));
    foreach ((string boardType, string displayName) in modBoards)
    {
      _options.Add(new BoardOption(boardType, displayName));
    }

    BuildLayout();
    Game1.playSound("bigSelect");
  }

  private void BuildLayout()
  {
    SpriteFont font = Game1.dialogueFont;
    string title = I18n.SpecialOrdersBoardSelector();

    int titleWidth = SpriteText.getWidthOfString(title);
    int titleHeight = SpriteText.getHeightOfString(title);
    int maxTextWidth = titleWidth;
    foreach (BoardOption option in _options)
    {
      int w = (int)font.MeasureString(option.DisplayName).X;
      if (w > maxTextWidth)
        maxTextWidth = w;
    }

    width = maxTextWidth + MenuPadding * 2 + borderWidth * 2;
    height =
      titleHeight
      + TitleBottomMargin
      + RowHeight * _options.Count
      + MenuPadding * 2
      + borderWidth * 2;

    Vector2 center = Utility.getTopLeftPositionForCenteringOnScreen(width, height);
    xPositionOnScreen = (int)center.X;
    yPositionOnScreen = (int)center.Y;

    _optionComponents.Clear();
    int contentX = xPositionOnScreen + borderWidth + MenuPadding;
    int contentY = yPositionOnScreen + borderWidth + MenuPadding + titleHeight + TitleBottomMargin;

    for (int i = 0; i < _options.Count; i++)
    {
      var comp = new ClickableComponent(
        new Rectangle(contentX, contentY + i * RowHeight, maxTextWidth, RowHeight),
        _options[i].DisplayName
      )
      {
        myID = BaseSnapId + i,
        upNeighborID = i > 0 ? BaseSnapId + i - 1 : -99998,
        downNeighborID = i < _options.Count - 1 ? BaseSnapId + i + 1 : -99998,
        leftNeighborID = -99998,
        rightNeighborID = -99998,
      };
      _optionComponents.Add(comp);
    }

    initializeUpperRightCloseButton();
    populateClickableComponentList();
    allClickableComponents.AddRange(_optionComponents);
  }

  public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
  {
    base.gameWindowSizeChanged(oldBounds, newBounds);
    BuildLayout();
  }

  public override void snapToDefaultClickableComponent()
  {
    if (_optionComponents.Count > 0)
    {
      currentlySnappedComponent = _optionComponents[0];
      snapCursorToCurrentSnappedComponent();
    }
  }

  public override void performHoverAction(int x, int y)
  {
    base.performHoverAction(x, y);
    _hoveredIndex = -1;
    for (int i = 0; i < _optionComponents.Count; i++)
    {
      if (_optionComponents[i].bounds.Contains(x, y))
      {
        _hoveredIndex = i;
        break;
      }
    }
  }

  public override void receiveLeftClick(int x, int y, bool playSound = true)
  {
    base.receiveLeftClick(x, y, playSound);

    for (int i = 0; i < _optionComponents.Count; i++)
    {
      if (_optionComponents[i].bounds.Contains(x, y))
      {
        SelectBoard(i);
        return;
      }
    }
  }

  public override void receiveGamePadButton(Buttons b)
  {
    base.receiveGamePadButton(b);

    if (b == Buttons.A && currentlySnappedComponent != null)
    {
      int index = currentlySnappedComponent.myID - BaseSnapId;
      if (index >= 0 && index < _options.Count)
      {
        SelectBoard(index);
      }
    }
  }

  private void SelectBoard(int index)
  {
    Game1.playSound("bigSelect");
    _onBoardSelected?.Invoke(_options[index].BoardType);
    var board = new SpecialOrdersBoard(_options[index].BoardType);
    if (_returnToInventory)
    {
      board.exitFunction = ShowCalendarAndBillboardOnGameMenuButton.ReturnToInventory;
    }
    Game1.activeClickableMenu = board;
  }

  public override void draw(SpriteBatch b)
  {
    // Dim background
    b.Draw(
      Game1.fadeToBlackRect,
      Game1.graphics.GraphicsDevice.Viewport.Bounds,
      Color.Black * 0.75f
    );

    // Draw menu box
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

    SpriteFont font = Game1.dialogueFont;
    string title = I18n.SpecialOrdersBoardSelector();

    int contentX = xPositionOnScreen + borderWidth + MenuPadding;
    int contentY = yPositionOnScreen + borderWidth + MenuPadding;

    // Draw title in SpriteText (chunky header font)
    SpriteText.drawString(b, title, contentX, contentY);

    int titleHeight = SpriteText.getHeightOfString(title);
    int optionsStartY = contentY + titleHeight + TitleBottomMargin;

    // Draw options
    for (int i = 0; i < _options.Count; i++)
    {
      Color textColor = i == _hoveredIndex ? Color.Wheat : Game1.textColor;
      int rowY = optionsStartY + i * RowHeight;
      float textY = rowY + (RowHeight - font.MeasureString(_options[i].DisplayName).Y) / 2;

      Utility.drawTextWithShadow(
        b,
        _options[i].DisplayName,
        font,
        new Vector2(contentX, textY),
        textColor
      );

      // Draw static exclamation mark next to unviewed boards
      if (!_viewedBoardTypes.Contains(_options[i].BoardType))
      {
        const float exclamationScale = 2.5f;
        float textWidth = font.MeasureString(_options[i].DisplayName).X;
        Tools.DrawExclamation(
          b,
          new Vector2(contentX + textWidth + 12, textY + 4),
          exclamationScale
        );
      }
    }

    base.draw(b);
    drawMouse(b);
  }
}
