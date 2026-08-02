using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace UIInfoSuite2Alt.Options;

internal class ModOptionsImage : ModOptionsElement
{
  private readonly Func<Texture2D> _texture;
  private readonly Rectangle? _sourceRect;
  private readonly int _scale;
  private readonly Action? _onClick;
  private bool _boundsInitialized;

  public ModOptionsImage(
    Func<Texture2D> texture,
    Rectangle? sourceRect = null,
    int scale = Game1.pixelZoom,
    Action? onClick = null
  )
    : base("", -1)
  {
    _texture = texture;
    _sourceRect = sourceRect;
    _scale = scale;
    _onClick = onClick;
  }

  private void EnsureBounds()
  {
    if (_boundsInitialized || _onClick == null)
    {
      return;
    }

    _boundsInitialized = true;

    // Cover the full slot so clicking anywhere on the banner row toggles
    int slotWidth = Game1.activeClickableMenu?.width ?? Game1.uiViewport.Width;
    int slotHeight =
      Game1.activeClickableMenu != null
        ? (Game1.activeClickableMenu.height - Game1.tileSize * 2) / 7 + Game1.pixelZoom
        : Bounds.Height;
    Bounds = new Rectangle(0, 0, slotWidth, slotHeight);
  }

  public override bool ContainsClickPoint(int x, int y)
  {
    // Android: the banner spans the whole row, so leave it to drag-scrolling and toggle
    // from the section header only
    if (IsAndroid)
    {
      return false;
    }

    EnsureBounds();
    return Bounds.Contains(x, y);
  }

  public override void ReceiveLeftClick(int x, int y)
  {
    if (_onClick == null)
    {
      return;
    }

    if (ContainsClickPoint(x, y))
    {
      Game1.playSound("drumkit6");
      _onClick();
    }
  }

  public override void Draw(SpriteBatch batch, int slotX, int slotY)
  {
    EnsureBounds();

    Texture2D tex = _texture();
    Rectangle source = _sourceRect ?? new Rectangle(0, 0, tex.Width, tex.Height);

    // Center horizontally in the slot
    int drawWidth = source.Width * _scale;
    int drawHeight = source.Height * _scale;
    int slotWidth = Game1.activeClickableMenu?.width ?? Game1.uiViewport.Width;
    int drawX = slotX + (slotWidth - Game1.tileSize / 2 - drawWidth) / 2;

    // Center vertically in the slot
    int slotHeight =
      Game1.activeClickableMenu != null
        ? (Game1.activeClickableMenu.height - Game1.tileSize * 2) / 7 + Game1.pixelZoom
        : drawHeight;
    int drawY = slotY + (slotHeight - drawHeight) / 2;

    batch.Draw(
      tex,
      new Vector2(drawX, drawY),
      source,
      Color.White,
      0f,
      Vector2.Zero,
      _scale,
      SpriteEffects.None,
      0.4f
    );
  }

  public override Point? GetRelativeSnapPoint(Rectangle slotBounds)
  {
    if (_onClick != null)
    {
      EnsureBounds();
      return new Point(Bounds.Width / 2, slotBounds.Height / 2);
    }

    // Non-interactive, skip snap
    return null;
  }
}
