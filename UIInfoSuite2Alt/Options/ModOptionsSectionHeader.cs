using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;

namespace UIInfoSuite2Alt.Options;

/// <summary>Clickable section header that toggles expand/collapse of its child options.</summary>
internal class ModOptionsSectionHeader : ModOptionsElement
{
  private static readonly Rectangle ExpandIcon = new(184, 345, 7, 8);
  private static readonly Rectangle CollapseIcon = new(177, 345, 7, 8);

  private readonly Action _onToggle;
  private bool _boundsInitialized;

  /// <summary>Android: just the icon and label, so a drag elsewhere on the row scrolls the page.</summary>
  private Rectangle _touchBounds;

  public bool IsExpanded { get; set; }

  public ModOptionsSectionHeader(string label, Action onToggle, bool isExpanded = false)
    : base(label, isVertCentered: true)
  {
    _onToggle = onToggle;
    IsExpanded = isExpanded;
  }

  public override bool IsInteractive => true;

  private void EnsureBounds()
  {
    if (_boundsInitialized)
    {
      return;
    }

    _boundsInitialized = true;

    // Desktop covers the full slot, so clicking anywhere on the row toggles
    int slotWidth = Game1.activeClickableMenu?.width ?? Game1.uiViewport.Width;
    int slotHeight =
      Game1.activeClickableMenu != null
        ? (Game1.activeClickableMenu.height - Game1.tileSize * 2) / 7 + Game1.pixelZoom
        : Bounds.Height;
    Bounds = new Rectangle(0, 0, slotWidth, slotHeight);

    // Matches the icon and label placement in Draw
    int iconX = DefaultX * Game1.pixelZoom;
    int textOffset = ExpandIcon.Width * Game1.pixelZoom + Game1.pixelZoom * 2 + 8;
    _touchBounds = new Rectangle(
      iconX,
      0,
      textOffset + SpriteText.getWidthOfString(_label),
      slotHeight
    );
  }

  public override bool ContainsClickPoint(int x, int y)
  {
    EnsureBounds();
    return IsAndroid ? _touchBounds.Contains(x, y) : Bounds.Contains(x, y);
  }

  public override void ReceiveLeftClick(int x, int y)
  {
    if (ContainsClickPoint(x, y))
    {
      Game1.playSound("drumkit6");
      _onToggle();
    }
  }

  public override void Draw(SpriteBatch batch, int slotX, int slotY)
  {
    EnsureBounds();

    int drawX = slotX + DefaultX * Game1.pixelZoom;
    int drawY = slotY + Bounds.Y;
    int slotHeight = Bounds.Height;

    if (Game1.activeClickableMenu != null)
    {
      slotHeight = (Game1.activeClickableMenu.height - Game1.tileSize * 2) / 7 + Game1.pixelZoom;
      int textHeight = SpriteText.getHeightOfString(_label);
      drawY = slotY + (slotHeight - textHeight) / 2 + Game1.pixelZoom * 3;
    }

    // Draw expand/collapse icon from Cursors
    Rectangle iconSource = IsExpanded ? CollapseIcon : ExpandIcon;
    int iconScale = Game1.pixelZoom;
    int iconWidth = iconSource.Width * iconScale;
    int iconHeight = iconSource.Height * iconScale;
    int iconY = slotY + (slotHeight - iconHeight) / 2 + 9;

    batch.Draw(
      Game1.mouseCursors,
      new Vector2(drawX, iconY),
      iconSource,
      Color.White,
      0f,
      Vector2.Zero,
      iconScale,
      SpriteEffects.None,
      0.4f
    );

    // Draw label text after the icon
    int textX = drawX + iconWidth + Game1.pixelZoom * 2 + 8;
    SpriteText.drawString(batch, _label, textX, drawY, 999, -1, 999, 1, 0.1f);
  }

  public override Point? GetRelativeSnapPoint(Rectangle slotBounds)
  {
    EnsureBounds();
    return new Point(DefaultX * Game1.pixelZoom + 16, slotBounds.Height / 2 - 12);
  }
}
