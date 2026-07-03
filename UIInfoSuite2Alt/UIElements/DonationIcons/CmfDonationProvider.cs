using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Compatibility.Helpers;
using UIInfoSuite2Alt.Infrastructure.Extensions;

namespace UIInfoSuite2Alt.UIElements.DonationIcons;

internal class CmfDonationProvider : IDonationIconProvider
{
  private static readonly Rectangle LostBookFallbackRect = new(144, 447, 15, 15);

  private readonly IModHelper _helper;
  private readonly string _locationName;
  private readonly CmfMuseumData _data;
  private (Texture2D texture, Rectangle sourceRect)? _icon;
  private bool _iconInitialized;

  public CmfDonationProvider(IModHelper helper, string locationName, CmfMuseumData data)
  {
    _helper = helper;
    _locationName = locationName;
    _data = data;
  }

  public bool IsAvailable => CustomMuseumFrameworkHelper.IsModLoaded;

  public bool NeedsDonation(Item item)
  {
    if (!CustomMuseumFrameworkHelper.IsSuitableForDonation(item, _data))
    {
      return false;
    }

    return !CustomMuseumFrameworkHelper.IsAlreadyDonated(_locationName, item.QualifiedItemId);
  }

  public (Texture2D texture, Rectangle sourceRect)? GetIcon()
  {
    if (_iconInitialized)
    {
      return _icon;
    }

    _iconInitialized = true;
    _icon =
      ResolveOwnerHeadshot()
      ?? ResolveOwnerTextureDirect()
      ?? ResolveMuseumOverride()
      ?? ResolveFallback();
    return _icon;
  }

  private (Texture2D texture, Rectangle sourceRect)? ResolveMuseumOverride()
  {
    if (!CustomMuseumFrameworkHelper.TryGetIconOverride(_locationName, out var overrideIcon))
    {
      return null;
    }

    try
    {
      Texture2D texture = _helper.GameContent.Load<Texture2D>(overrideIcon.AssetName);
      return (texture, overrideIcon.SourceRect);
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"CmfDonationProvider: override icon load failed, museum={_locationName}, asset={overrideIcon.AssetName}, {ex.Message}",
        LogLevel.Warn
      );
      return null;
    }
  }

  private (Texture2D texture, Rectangle sourceRect)? ResolveOwnerHeadshot()
  {
    string? ownerName = _data.Owner?.Name;
    if (string.IsNullOrWhiteSpace(ownerName))
    {
      return null;
    }

    NPC? owner = Game1.getCharacterFromName(ownerName);
    if (owner?.Sprite?.Texture is null)
    {
      return null;
    }

    return (owner.Sprite.Texture, owner.GetHeadShot());
  }

  private (Texture2D texture, Rectangle sourceRect)? ResolveOwnerTextureDirect()
  {
    string? ownerName = _data.Owner?.Name;
    if (string.IsNullOrWhiteSpace(ownerName))
    {
      return null;
    }

    try
    {
      Texture2D texture = _helper.GameContent.Load<Texture2D>("Characters/" + ownerName);
      return (texture, new Rectangle(0, 3, 16, 15));
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"CmfDonationProvider: direct texture load failed, owner={ownerName}, museum={_locationName}, {ex.Message}",
        LogLevel.Trace
      );
      return null;
    }
  }

  private (Texture2D texture, Rectangle sourceRect)? ResolveFallback()
  {
    try
    {
      Texture2D cursors = _helper.GameContent.Load<Texture2D>("LooseSprites/Cursors");
      return (cursors, LostBookFallbackRect);
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"CmfDonationProvider: fallback icon load failed, museum={_locationName}, {ex.Message}",
        LogLevel.Warn
      );
      return null;
    }
  }
}
