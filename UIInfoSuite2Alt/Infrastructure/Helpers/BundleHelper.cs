using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace UIInfoSuite2Alt.Infrastructure.Helpers;

// Maps qualified item ID (or category string) -> list of [bundleIdx, quantity, quality]
using BundleIngredientsCache = Dictionary<string, List<List<int>>>;

public record BundleRequiredItem(
  string Name,
  int BannerWidth,
  int Id,
  string QualifiedId,
  int Quality
);

public record BundleKeyData(string Name, int Color, string TexturePath, int SpriteIndex);

internal static class BundleHelper
{
  private static readonly Dictionary<int, BundleKeyData> BundleIdToBundleKeyDataMap = [];
  private static readonly BundleIngredientsCache AllBundleIngredients = [];
  private static readonly Dictionary<int, int> BundleIdToAreaMap = [];

  /// <summary>When true, show bundle items for all CC areas regardless of room unlock state.</summary>
  public static bool ShowLockedBundles { get; set; }

  public static void ClearCaches()
  {
    BundleIdToBundleKeyDataMap.Clear();
    AllBundleIngredients.Clear();
    BundleIdToAreaMap.Clear();
  }

  public static BundleKeyData? GetBundleKeyDataFromIndex(int bundleIdx, bool forceRefresh = false)
  {
    PopulateBundleCaches(forceRefresh);
    return BundleIdToBundleKeyDataMap.GetValueOrDefault(bundleIdx);
  }

  public static Color? GetRealColorFromIndex(int bundleIdx, bool forceRefresh = false)
  {
    PopulateBundleCaches(forceRefresh);
    BundleKeyData? bundleData = BundleIdToBundleKeyDataMap.GetValueOrDefault(bundleIdx);
    if (bundleData == null)
    {
      return null;
    }

    return Bundle.getColorFromColorIndex(bundleData.Color);
  }

  private const string DefaultBundleTexture = "LooseSprites\\JunimoNote";
  private const int BundleSpriteSize = 32;

  /// <summary>
  /// Gets the bundle icon texture and source rectangle for a bundle.
  /// Loads the texture fresh each call (no caching) since CP mods can invalidate textures.
  /// The default JunimoNote sheet has a Y offset of 180; custom textures start at Y=0.
  /// </summary>
  public static (Texture2D texture, Rectangle sourceRect)? GetBundleSpriteInfo(int bundleIdx)
  {
    BundleKeyData? data = GetBundleKeyDataFromIndex(bundleIdx);
    if (data == null)
    {
      return null;
    }

    try
    {
      Texture2D texture = Game1.content.Load<Texture2D>(data.TexturePath);
      bool isDefaultSheet = data.TexturePath == DefaultBundleTexture;
      int yOffset = isDefaultSheet ? 180 : 0;
      int x = data.SpriteIndex * BundleSpriteSize % texture.Width;
      int y = yOffset + BundleSpriteSize * (data.SpriteIndex * BundleSpriteSize / texture.Width);
      return (texture, new Rectangle(x, y, BundleSpriteSize, BundleSpriteSize));
    }
    catch (Exception)
    {
      return null;
    }
  }

  private static int GetBundleBannerWidthForName(string bundleName)
  {
    return 36 + (int)Game1.smallFont.MeasureString(bundleName).X;
  }

  public static BundleRequiredItem? GetBundleItemIfNotDonated(Item item)
  {
    if (item is not SObject donatedItem || donatedItem.bigCraftable.Value)
    {
      return null;
    }

    // No bundles to track if player chose Joja route or can't read junimo text yet
    var communityCenter = Game1.RequireLocation<CommunityCenter>("CommunityCenter");
    if (Game1.MasterPlayer.mailReceived.Contains("JojaMember"))
    {
      return null;
    }

    if (!ShowLockedBundles && !Game1.player.hasOrWillReceiveMail("canReadJunimoText"))
    {
      return null;
    }

    // No bundles to track if CC is complete and Missing Bundle (Abandoned JojaMart) is also done
    bool missingBundleDone = Game1.MasterPlayer.mailReceived.Contains("ccMovieTheater");
    if (communityCenter.areAllAreasComplete() && missingBundleDone)
    {
      return null;
    }

    PopulateBundleCaches();

    BundleRequiredItem? output;
    List<List<int>>? bundleRequiredItemsList;

    if (AllBundleIngredients.TryGetValue(donatedItem.QualifiedItemId, out bundleRequiredItemsList))
    {
      output = GetBundleItemIfNotDonatedFromList(
        bundleRequiredItemsList,
        donatedItem,
        communityCenter
      );
      if (output != null)
      {
        return output;
      }
    }

    if (
      donatedItem.Category >= 0
      || !AllBundleIngredients.TryGetValue(
        donatedItem.Category.ToString(),
        out bundleRequiredItemsList
      )
    )
    {
      return null;
    }

    output = GetBundleItemIfNotDonatedFromList(
      bundleRequiredItemsList,
      donatedItem,
      communityCenter
    );
    return output;
  }

  private static BundleRequiredItem? GetBundleItemIfNotDonatedFromList(
    List<List<int>>? lists,
    ISalable obj,
    CommunityCenter communityCenter
  )
  {
    if (lists == null)
    {
      return null;
    }

    foreach (List<int> list in lists)
    {
      if (list.Count < 3 || obj.Quality < list[2])
      {
        continue;
      }

      int bundleIdx = list[0];

      // Skip bundles in undiscovered rooms unless "Show locked bundle items" is on
      if (
        !ShowLockedBundles
        && BundleIdToAreaMap.TryGetValue(bundleIdx, out int area)
        && !communityCenter.shouldNoteAppearInArea(area)
      )
      {
        continue;
      }

      BundleKeyData? bundleKeyData = GetBundleKeyDataFromIndex(bundleIdx);
      if (bundleKeyData == null)
      {
        continue;
      }

      return new BundleRequiredItem(
        bundleKeyData.Name,
        GetBundleBannerWidthForName(bundleKeyData.Name),
        bundleIdx,
        obj.QualifiedItemId,
        obj.Quality
      );
    }

    return null;
  }

  /// <summary>
  /// Builds both the bundle name/color map and the ingredients cache from BundleData.
  /// Unlike the game's bundlesIngredientsInfo, this includes ALL areas (not just unlocked ones),
  /// so the traveling merchant and item tooltips can detect bundle needs for locked rooms too.
  /// </summary>
  public static void PopulateBundleCaches(bool force = false)
  {
    if (BundleIdToBundleKeyDataMap.Count != 0 && !force)
    {
      return;
    }

    BundleIdToBundleKeyDataMap.Clear();
    AllBundleIngredients.Clear();
    BundleIdToAreaMap.Clear();

    Dictionary<string, string> bundleData = Game1.netWorldState.Value.BundleData;
    Dictionary<int, bool[]> donationStatus = Game1.netWorldState.Value.Bundles.Pairs.ToDictionary(
      kvp => kvp.Key,
      kvp => kvp.Value.ToArray()
    );

    foreach (KeyValuePair<string, string> bundleInfo in bundleData)
    {
      try
      {
        string[] bundleLocationInfo = bundleInfo.Key.Split('/');
        int bundleIdx = Convert.ToInt32(bundleLocationInfo[1]);
        int areaNumber = CommunityCenter.getAreaNumberFromName(bundleLocationInfo[0]);
        BundleIdToAreaMap[bundleIdx] = areaNumber;
        string[] bundleContentsData = bundleInfo.Value.Split('/');

        // Populate name/color/sprite map
        string localizedName = bundleContentsData[6];
        int color = Convert.ToInt32(bundleContentsData[3]);

        // Parse sprite field (index 5): either "Index" (default sheet) or "TexturePath:Index"
        string texturePath = "LooseSprites\\JunimoNote";
        int spriteIndex = bundleIdx;
        if (!string.IsNullOrWhiteSpace(bundleContentsData[5]))
        {
          string[] spriteParts = bundleContentsData[5].Split(':', 2);
          if (spriteParts.Length == 2)
          {
            texturePath = spriteParts[0];
            int.TryParse(spriteParts[1], out spriteIndex);
          }
          else
          {
            int.TryParse(bundleContentsData[5], out spriteIndex);
          }
        }

        BundleIdToBundleKeyDataMap[bundleIdx] = new BundleKeyData(
          localizedName,
          color,
          texturePath,
          spriteIndex
        );

        // Populate ingredients cache for all undonated items (no area-unlock filter)
        string[] itemEntries = ArgUtility.SplitBySpace(bundleContentsData[2]);
        if (!donationStatus.TryGetValue(bundleIdx, out bool[]? donated))
        {
          continue;
        }

        for (int i = 0; i < itemEntries.Length; i += 3)
        {
          int slotIndex = i / 3;
          if (slotIndex < donated.Length && donated[slotIndex])
          {
            continue;
          }

          string itemId = itemEntries[i];
          int quantity = Convert.ToInt32(itemEntries[i + 1]);
          int quality = Convert.ToInt32(itemEntries[i + 2]);

          // Negative IDs are category matches, otherwise resolve to qualified item ID
          string key;
          if (int.TryParse(itemId, out int numericId) && numericId < 0)
          {
            key = numericId.ToString();
          }
          else
          {
            ParsedItemData? data = ItemRegistry.GetData(itemId);
            key = data != null ? data.QualifiedItemId : "(O)" + itemId;
          }

          if (!AllBundleIngredients.TryGetValue(key, out List<List<int>>? entryList))
          {
            entryList = [];
            AllBundleIngredients[key] = entryList;
          }

          entryList.Add([bundleIdx, quantity, quality]);
        }
      }
      catch (Exception e)
      {
        ModEntry.MonitorObject.Log(
          $"BundleHelper: failed to parse bundle '{bundleInfo}', {e.Message}",
          LogLevel.Warn
        );
      }
    }

    ModEntry.MonitorObject.Log(
      $"BundleHelper: cached CC bundles, bundles={BundleIdToBundleKeyDataMap.Count}, ingredients={AllBundleIngredients.Count}, areas={BundleIdToAreaMap.Values.Distinct().Count()}",
      LogLevel.Trace
    );
  }
}
