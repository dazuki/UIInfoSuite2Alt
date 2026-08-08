using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.Options;

internal class ModOptionsPageButton
{
  private readonly Texture2D _tabIcon;

  public int xPositionOnScreen;
  public int yPositionOnScreen;

  public ModOptionsPageButton(IModHelper helper)
  {
    _tabIcon = AssetHelper.TryLoadTexture(helper, "assets/tab_icon.png");
  }

  public void draw(SpriteBatch b)
  {
    b.Draw(
      Game1.mouseCursors,
      new Vector2(xPositionOnScreen, yPositionOnScreen),
      new Rectangle(16, 368, 16, 16),
      Color.White,
      0.0f,
      Vector2.Zero,
      Game1.pixelZoom,
      SpriteEffects.None,
      1f
    );

    float iconScale = 3f;
    float iconSize = 16 * iconScale;
    float tabSize = 16 * Game1.pixelZoom;
    float offset = (tabSize - iconSize) / 2f;
    b.Draw(
      _tabIcon,
      new Vector2(xPositionOnScreen + offset, yPositionOnScreen + offset + 5),
      null,
      Color.White,
      0.0f,
      Vector2.Zero,
      iconScale,
      SpriteEffects.None,
      1f
    );
  }
}
