using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace UIInfoSuite2Alt.Options;

internal class ModOptionsDropdown : ModOptionsElement
{
  private static readonly Rectangle DropDownBGSource = new(433, 451, 3, 3);
  private static readonly Rectangle DropDownButtonSource = new(437, 450, 10, 11);

  private const int ButtonWidth = 48;
  private const int ItemHeight = 44;

  private readonly Action<int> _setOption;
  private readonly List<string> _displayOptions;
  private int _selectedOption;
  private int _startingSelected;
  private bool _clicked;
  private Rectangle _dropDownBounds;
  private int _recentSlotY;

  public ModOptionsDropdown(
    string label,
    int whichOption,
    List<string> displayOptions,
    Func<int> getOption,
    Action<int> setOption,
    ModOptionsCheckbox? parent = null
  )
    : base(label, whichOption, parent)
  {
    _displayOptions = displayOptions;
    _setOption = setOption;
    _selectedOption = Math.Clamp(getOption(), 0, Math.Max(0, displayOptions.Count - 1));

    RecalculateBounds();
  }

  private bool _canClick => IsEffectivelyEnabled();

  /// <summary>The expanded list covers other rows, so it claims the next tap.</summary>
  public override bool IsCapturingInput => IsAndroid && _clicked;

  private void RecalculateBounds()
  {
    int maxTextWidth = 0;
    foreach (string option in _displayOptions)
    {
      int w = (int)Game1.smallFont.MeasureString(option).X;
      if (w > maxTextWidth)
        maxTextWidth = w;
    }

    int controlWidth = maxTextWidth + 16 + ButtonWidth;
    Bounds = new Rectangle(Bounds.X, Bounds.Y, controlWidth, ItemHeight);
    _dropDownBounds = new Rectangle(
      Bounds.X,
      Bounds.Y,
      controlWidth - ButtonWidth,
      ItemHeight * _displayOptions.Count
    );
  }

  public override void ReceiveLeftClick(int x, int y)
  {
    if (!_canClick)
      return;

    if (IsAndroid)
    {
      AndroidReceiveLeftClick(x, y);
      return;
    }

    _startingSelected = _selectedOption;
    _dropDownBounds.Y = Bounds.Y;
    if (!_clicked)
    {
      Game1.playSound("shwip");
    }
    _clicked = true;
    LeftClickHeld(x, y);
  }

  /// <summary>
  ///   Android: first tap opens the list, second tap picks a row. Touch has no hover between press
  ///   and release, so the desktop drag-to-pick model commits a row the moment the list opens.
  /// </summary>
  private void AndroidReceiveLeftClick(int x, int y)
  {
    if (!_clicked)
    {
      _startingSelected = _selectedOption;
      _dropDownBounds.Y = Bounds.Y;
      ClampDropDownToViewport();
      Game1.playSound("shwip");
      _clicked = true;
      return;
    }

    _clicked = false;

    if (_displayOptions.Count > 0 && _dropDownBounds.Contains(x, y))
    {
      _selectedOption = Math.Clamp(
        (y - _dropDownBounds.Y) / ItemHeight,
        0,
        _displayOptions.Count - 1
      );
      Game1.playSound("drumkit6");
      _setOption(_selectedOption);
    }
    else
    {
      _selectedOption = _startingSelected;
    }
  }

  /// <summary>Shifts the list up if it would run past the bottom of the screen.</summary>
  private void ClampDropDownToViewport()
  {
    _dropDownBounds.Y = Math.Min(
      _dropDownBounds.Y,
      Game1.uiViewport.Height - _dropDownBounds.Height - _recentSlotY
    );
  }

  public override void LeftClickHeld(int x, int y)
  {
    // Android selects from discrete taps instead
    if (!_canClick || !_clicked || IsAndroid)
      return;

    ClampDropDownToViewport();

    // Skip mouse-position tracking with gamepad; selection is changed via ReceiveKeyPress
    if (!Game1.options.SnappyMenus)
    {
      _selectedOption = Math.Clamp(
        (y - _dropDownBounds.Y) / ItemHeight,
        0,
        Math.Max(0, _displayOptions.Count - 1)
      );
    }
  }

  public override void LeftClickReleased(int x, int y)
  {
    // Android keeps the list open; the next tap commits
    if (!_canClick || !_clicked || IsAndroid)
      return;

    _clicked = false;

    if (
      _displayOptions.Count > 0
      && (
        _dropDownBounds.Contains(x, y)
        || (Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse)
      )
    )
    {
      Game1.playSound("drumkit6");
      _setOption(_selectedOption);
    }
    else
    {
      _selectedOption = _startingSelected;
    }
  }

  public override void ReceiveKeyPress(Keys key)
  {
    if (!_canClick || !_clicked || _displayOptions.Count == 0)
      return;

    if (Game1.options.doesInputListContain(Game1.options.moveDownButton, key))
    {
      Game1.playSound("shiny4");
      _selectedOption = (_selectedOption + 1) % _displayOptions.Count;
    }
    else if (Game1.options.doesInputListContain(Game1.options.moveUpButton, key))
    {
      Game1.playSound("shiny4");
      _selectedOption = (_selectedOption - 1 + _displayOptions.Count) % _displayOptions.Count;
    }
  }

  public override void Draw(SpriteBatch batch, int slotX, int slotY)
  {
    _recentSlotY = slotY;
    float alpha = _canClick ? 1f : 0.33f;

    if (_clicked)
    {
      // Expanded dropdown background
      IClickableMenu.drawTextureBox(
        batch,
        Game1.mouseCursors,
        DropDownBGSource,
        slotX + _dropDownBounds.X,
        slotY + _dropDownBounds.Y,
        _dropDownBounds.Width,
        _dropDownBounds.Height,
        Color.White * alpha,
        Game1.pixelZoom,
        false,
        0.97f
      );

      for (int i = 0; i < _displayOptions.Count; i++)
      {
        if (i == _selectedOption)
        {
          batch.Draw(
            Game1.staminaRect,
            new Rectangle(
              slotX + _dropDownBounds.X,
              slotY + _dropDownBounds.Y + i * ItemHeight,
              _dropDownBounds.Width,
              ItemHeight
            ),
            new Rectangle(0, 0, 1, 1),
            Color.Wheat,
            0f,
            Vector2.Zero,
            SpriteEffects.None,
            0.975f
          );
        }

        batch.DrawString(
          Game1.smallFont,
          _displayOptions[i],
          new Vector2(
            slotX + _dropDownBounds.X + 4,
            slotY + _dropDownBounds.Y + 8 + ItemHeight * i
          ),
          Game1.textColor * alpha,
          0f,
          Vector2.Zero,
          1f,
          SpriteEffects.None,
          0.98f
        );
      }

      // Dropdown button (expanded state)
      batch.Draw(
        Game1.mouseCursors,
        new Vector2(slotX + Bounds.X + Bounds.Width - ButtonWidth, slotY + Bounds.Y),
        DropDownButtonSource,
        Color.Wheat * alpha,
        0f,
        Vector2.Zero,
        Game1.pixelZoom,
        SpriteEffects.None,
        0.981f
      );
    }
    else
    {
      // Collapsed dropdown background
      IClickableMenu.drawTextureBox(
        batch,
        Game1.mouseCursors,
        DropDownBGSource,
        slotX + Bounds.X,
        slotY + Bounds.Y,
        Bounds.Width - ButtonWidth,
        Bounds.Height,
        Color.White * alpha,
        Game1.pixelZoom,
        false
      );

      // Selected option text
      if (_selectedOption >= 0 && _selectedOption < _displayOptions.Count)
      {
        batch.DrawString(
          Game1.smallFont,
          _displayOptions[_selectedOption],
          new Vector2(slotX + Bounds.X + 4, slotY + Bounds.Y + 8),
          Game1.textColor * alpha,
          0f,
          Vector2.Zero,
          1f,
          SpriteEffects.None,
          0.88f
        );
      }

      // Dropdown button (collapsed state)
      batch.Draw(
        Game1.mouseCursors,
        new Vector2(slotX + Bounds.X + Bounds.Width - ButtonWidth, slotY + Bounds.Y),
        DropDownButtonSource,
        Color.White * alpha,
        0f,
        Vector2.Zero,
        Game1.pixelZoom,
        SpriteEffects.None,
        0.88f
      );
    }

    // Label text to the right of the control
    base.Draw(batch, slotX, slotY);
  }

  public override Point? GetRelativeSnapPoint(Rectangle slotBounds)
  {
    return new Point(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
  }
}
