using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;

namespace UIInfoSuite2Alt.Options;

public class ModOptionsElement
{
  protected const int DefaultX = 8;
  protected const int DefaultY = 4;
  protected const int DefaultPixelSize = 9;

  protected readonly string _label;

  protected readonly ModOptionsElement? _parent;
  private readonly int _whichOption;
  private readonly bool _isSubtitle;
  private readonly bool _isSmallText;
  private readonly bool _isCentered;
  private readonly Color? _textColor;
  private readonly bool _isVertCentered;

  public virtual bool IsInteractive => _whichOption >= 0 && IsEffectivelyEnabled();

  public ModOptionsElement(
    string label,
    int whichOption = -1,
    ModOptionsElement? parent = null,
    bool isSubtitle = false,
    bool isSmallText = false,
    bool isCentered = false,
    Color? textColor = null,
    bool isVertCentered = false,
    bool isIndented = false
  )
  {
    int x = DefaultX * Game1.pixelZoom;
    int y = DefaultY * Game1.pixelZoom;
    int width = DefaultPixelSize * Game1.pixelZoom;
    int height = DefaultPixelSize * Game1.pixelZoom;

    if (parent != null || isIndented)
    {
      x += DefaultX * 2 * Game1.pixelZoom;
    }

    if (isSmallText)
    {
      y -= Game1.pixelZoom * 3;
    }

    Bounds = new Rectangle(x, y, width, height);
    _label = label;
    _whichOption = whichOption;

    _parent = parent;
    _isSubtitle = isSubtitle;
    _isSmallText = isSmallText;
    _isCentered = isCentered;
    _textColor = textColor;
    _isVertCentered = isVertCentered;
  }

  public Rectangle Bounds { get; protected set; }

  protected static bool IsAndroid => Constants.TargetPlatform == GamePlatform.Android;

  /// <summary>
  ///   Whether this element is mid-interaction and takes the next click wherever it lands, for
  ///   controls that draw outside their own slot.
  /// </summary>
  public virtual bool IsCapturingInput => false;

  /// <summary>
  ///   Whether a slot-relative point activates this element. Overridden where the clickable area
  ///   must be narrower than <see cref="Bounds" />, so a touch drag scrolls instead of activating.
  /// </summary>
  public virtual bool ContainsClickPoint(int x, int y)
  {
    return Bounds.Contains(x, y);
  }

  public virtual int Height
  {
    get
    {
      int slotWidth = Game1.activeClickableMenu?.width ?? 800;
      int slotHeight =
        Game1.activeClickableMenu != null
          ? (Game1.activeClickableMenu.height - Game1.tileSize * 2) / 7 + Game1.pixelZoom
          : 68;

      float textHeight = 0;

      if (_isSmallText)
      {
        int maxWidth = slotWidth - Bounds.X - Game1.tileSize;
        string parsedText = Game1.parseText(_label, Game1.smallFont, maxWidth);
        textHeight = Game1.smallFont.MeasureString(parsedText).Y;
      }
      else if (_isSubtitle)
      {
        int maxWidth = slotWidth - Bounds.X - Game1.tileSize;
        string parsedText = Game1.parseText(_label, Game1.dialogueFont, maxWidth);
        textHeight = Game1.dialogueFont.MeasureString(parsedText).Y;
      }
      else if (_whichOption < 0)
      {
        int maxWidth = slotWidth - Bounds.X - Game1.tileSize;
        string parsedText = Game1.parseText(_label, Game1.dialogueFont, maxWidth);
        textHeight = SpriteText.getHeightOfString(parsedText);
      }
      else
      {
        int startX = Bounds.X + Bounds.Width + Game1.pixelZoom * 2;
        int maxWidth = slotWidth - startX - Game1.tileSize;
        string parsedText = Game1.parseText(_label, Game1.dialogueFont, maxWidth);
        textHeight = Game1.dialogueFont.MeasureString(parsedText).Y;
      }

      return System.Math.Max(slotHeight, (int)textHeight + Game1.pixelZoom * 2 + Bounds.Y);
    }
  }

  protected bool IsEffectivelyEnabled()
  {
    for (ModOptionsElement? node = _parent; node != null; node = node._parent)
    {
      if (node is ModOptionsCheckbox cb && !cb.IsChecked)
      {
        return false;
      }
    }
    return true;
  }

  public virtual void ReceiveLeftClick(int x, int y) { }

  public virtual void LeftClickHeld(int x, int y) { }

  public virtual void LeftClickReleased(int x, int y) { }

  public virtual void ReceiveKeyPress(Keys key) { }

  public virtual void Draw(SpriteBatch batch, int slotX, int slotY)
  {
    if (_isSmallText)
    {
      int slotWidth = Game1.activeClickableMenu?.width ?? Game1.uiViewport.Width;
      int maxWidth = slotWidth - Bounds.X - Game1.tileSize;
      string parsedText = Game1.parseText(_label, Game1.smallFont, maxWidth);

      float drawX = slotX + Bounds.X;
      if (_isCentered)
      {
        float textWidth = Game1.smallFont.MeasureString(parsedText).X;
        drawX = slotX + (slotWidth - Game1.tileSize / 2 - textWidth) / 2f;
      }

      float drawY = slotY + Bounds.Y;
      if (_isVertCentered && Game1.activeClickableMenu != null)
      {
        int slotHeight =
          (Game1.activeClickableMenu.height - Game1.tileSize * 2) / 7 + Game1.pixelZoom;
        float textHeight = Game1.smallFont.MeasureString(parsedText).Y;
        drawY = slotY + (slotHeight - textHeight) / 2f;
      }

      Utility.drawTextWithShadow(
        batch,
        parsedText,
        Game1.smallFont,
        new Vector2(drawX, drawY),
        _textColor ?? Game1.textColor,
        1f,
        0.1f
      );
    }
    else if (_isSubtitle)
    {
      int slotWidth = Game1.activeClickableMenu?.width ?? Game1.uiViewport.Width;
      int maxWidth = slotWidth - Bounds.X - Game1.tileSize;
      string parsedText = Game1.parseText(_label, Game1.dialogueFont, maxWidth);

      Utility.drawTextWithShadow(
        batch,
        parsedText,
        Game1.dialogueFont,
        new Vector2(slotX + Bounds.X, slotY + Bounds.Y),
        Game1.textColor,
        1f,
        0.1f
      );
    }
    else if (_whichOption < 0)
    {
      int drawX = slotX + Bounds.X;
      int slotWidth = Game1.activeClickableMenu?.width ?? Game1.uiViewport.Width;
      int wrapWidth = slotWidth - Bounds.X - Game1.tileSize;
      string parsedText = Game1.parseText(_label, Game1.dialogueFont, wrapWidth);

      if (_isCentered)
      {
        int textWidth = SpriteText.getWidthOfString(parsedText);
        drawX = slotX + (slotWidth - Game1.tileSize / 2 - textWidth) / 2;
      }

      int drawY = slotY + Bounds.Y;
      if (_isVertCentered && Game1.activeClickableMenu != null)
      {
        int slotHeight =
          (Game1.activeClickableMenu.height - Game1.tileSize * 2) / 7 + Game1.pixelZoom;
        int textHeight = SpriteText.getHeightOfString(parsedText);
        drawY = slotY + (slotHeight - textHeight) / 2 + Game1.pixelZoom * 3;
      }

      SpriteText.drawString(batch, parsedText, drawX, drawY, 999, -1, 999, 1, 0.1f);
    }
    else
    {
      int startX = Bounds.X + Bounds.Width + Game1.pixelZoom * 2;
      int slotWidth = Game1.activeClickableMenu?.width ?? Game1.uiViewport.Width;
      int maxWidth = slotWidth - startX - Game1.tileSize;
      string parsedText = Game1.parseText(_label, Game1.dialogueFont, maxWidth);

      Utility.drawTextWithShadow(
        batch,
        parsedText,
        Game1.dialogueFont,
        new Vector2(slotX + startX, slotY + Bounds.Y),
        Game1.textColor,
        1f,
        0.1f
      );
    }
  }

  public virtual Point? GetRelativeSnapPoint(Rectangle slotBounds)
  {
    // Positioning taken from OptionsPage.snapCursorToCurrentSnappedComponent
    return new Point(48, slotBounds.Height / 2 - 12);
  }
}
