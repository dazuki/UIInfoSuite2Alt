using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace UIInfoSuite2Alt.Infrastructure.Extensions;

public static class NpcExtensions
{
  // Weak keys: a replaced sprite texture (outfit change, asset reload) drops its entries
  private static readonly ConditionalWeakTable<Texture2D, Dictionary<Rectangle, int>> TopRowCache =
    new();

  /// <summary>Square crop of the mugshot frame, starting one row above the first visible pixel row.</summary>
  public static Rectangle GetHeadShot(this NPC npc)
  {
    Rectangle mugShot = npc.getMugShotSourceRect();
    int size = mugShot.Width;
    Texture2D? texture = npc.Sprite?.Texture;
    if (texture == null)
    {
      return new Rectangle(mugShot.X, mugShot.Y, size, size);
    }

    Dictionary<Rectangle, int> rows = TopRowCache.GetOrCreateValue(texture);
    if (!rows.TryGetValue(mugShot, out int topRow))
    {
      topRow = FindTopOpaqueRow(texture, mugShot, npc.Name);
      rows[mugShot] = topRow;
    }

    int y = Math.Max(mugShot.Y, topRow - 1);
    return new Rectangle(mugShot.X, y, size, size);
  }

  private static int FindTopOpaqueRow(Texture2D texture, Rectangle area, string npcName)
  {
    Rectangle clamped = Rectangle.Intersect(area, texture.Bounds);
    if (clamped.Width <= 0 || clamped.Height <= 0)
    {
      return area.Y;
    }

    try
    {
      var pixels = new Color[clamped.Width * clamped.Height];
      texture.GetData(0, clamped, pixels, 0, pixels.Length);
      for (int row = 0; row < clamped.Height; row++)
      {
        for (int col = 0; col < clamped.Width; col++)
        {
          if (pixels[row * clamped.Width + col].A > 0)
          {
            return clamped.Y + row;
          }
        }
      }
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"NpcExtensions: failed to read sprite pixels for headshot crop, using frame top, npc: {npcName}, error: {ex.Message}",
        LogLevel.Trace
      );
    }

    return clamped.Y;
  }
}
