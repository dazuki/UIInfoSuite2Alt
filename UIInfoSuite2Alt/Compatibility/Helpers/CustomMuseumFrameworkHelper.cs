using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Inventories;

namespace UIInfoSuite2Alt.Compatibility.Helpers;

internal static class CustomMuseumFrameworkHelper
{
  public const string MuseumsAssetName = "Spiderbuttons.CMF/Museums";
  private const string GlobalInventoryPrefix = "Spiderbuttons.CMF_";
  private const string CmfTypeName = "CustomMuseumFramework.CMF";

  private static readonly Dictionary<
    string,
    (string AssetName, Rectangle SourceRect)
  > MuseumIconOverrides = new()
  {
    ["Lumisteria.MtVapius_NaturalReserveIndoor"] = (
      "Mods/Lumisteria.MtVapius/Books",
      new Rectangle(16, 0, 16, 16)
    ),
  };

  private static bool _isModLoaded;
  private static Type? _cachedCmfType;
  private static PropertyInfo? _cachedMuseumDataProp;

  public static bool IsModLoaded => _isModLoaded;

  public static bool TryGetIconOverride(
    string locationName,
    out (string AssetName, Rectangle SourceRect) icon
  )
  {
    return MuseumIconOverrides.TryGetValue(locationName, out icon);
  }

  public static void Initialize(IModHelper helper)
  {
    _isModLoaded = helper.ModRegistry.IsLoaded(ModCompat.CustomMuseumFramework);
    _cachedCmfType = null;
    _cachedMuseumDataProp = null;
  }

  public static Dictionary<string, CmfMuseumData> LoadMuseums()
  {
    if (!_isModLoaded)
    {
      return [];
    }

    try
    {
      object? raw = GetCmfMuseumData();
      if (raw is not IDictionary cmfDict)
      {
        return [];
      }

      var result = new Dictionary<string, CmfMuseumData>(cmfDict.Count);
      foreach (object? keyObj in cmfDict.Keys)
      {
        if (keyObj is not string key)
        {
          continue;
        }

        object? value = cmfDict[keyObj];
        if (value is null)
        {
          continue;
        }

        result[key] = ConvertMuseumData(value);
      }

      return result;
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"CustomMuseumFrameworkHelper: failed to read CMF museum data, {ex.Message}",
        LogLevel.Warn
      );
      return [];
    }
  }

  public static bool IsSuitableForDonation(Item? item, CmfMuseumData museum)
  {
    if (item is null)
    {
      return false;
    }

    if (item.HasContextTag("not_museum_donatable") || MatchesAny(item, museum.BlacklistedDonations))
    {
      return MatchesAny(item, museum.WhitelistedDonations);
    }

    return MatchesAny(item, museum.WhitelistedDonations)
      || MatchesAny(item, museum.DonationRequirements);
  }

  public static bool IsAlreadyDonated(string locationName, string qualifiedItemId)
  {
    if (Game1.player?.team is null)
    {
      return false;
    }

    Inventory inv = Game1.player.team.GetOrCreateGlobalInventory(
      GlobalInventoryPrefix + locationName
    );
    for (int i = 0; i < inv.Count; i++)
    {
      Item? donated = inv[i];
      if (donated is not null && donated.QualifiedItemId == qualifiedItemId)
      {
        return true;
      }
    }

    return false;
  }

  private static CmfMuseumData ConvertMuseumData(object source)
  {
    Type t = source.GetType();
    return new CmfMuseumData
    {
      Id = GetProp<string>(t, source, "Id") ?? "",
      Owner = ConvertOwner(GetProp<object>(t, source, "Owner")),
      DonationRequirements = ConvertRequirementList(
        GetProp<object>(t, source, "DonationRequirements")
      ),
      BlacklistedDonations = ConvertRequirementList(
        GetProp<object>(t, source, "BlacklistedDonations")
      ),
      WhitelistedDonations = ConvertRequirementList(
        GetProp<object>(t, source, "WhitelistedDonations")
      ),
    };
  }

  private static CmfOwnerData? ConvertOwner(object? source)
  {
    if (source is null)
    {
      return null;
    }

    return new CmfOwnerData { Name = GetProp<string>(source.GetType(), source, "Name") };
  }

  private static List<CmfDonationRequirement> ConvertRequirementList(object? source)
  {
    var result = new List<CmfDonationRequirement>();
    if (source is not IEnumerable enumerable)
    {
      return result;
    }

    foreach (object? entry in enumerable)
    {
      if (entry is null)
      {
        continue;
      }

      Type t = entry.GetType();
      result.Add(
        new CmfDonationRequirement
        {
          Id = GetProp<string>(t, entry, "Id"),
          Categories = CopyList<int>(GetProp<object>(t, entry, "Categories")),
          ContextTags = CopyList<string>(GetProp<object>(t, entry, "ContextTags")),
          ItemIds = CopyList<string>(GetProp<object>(t, entry, "ItemIds")),
          MatchType = ConvertMatchType(GetProp<object>(t, entry, "MatchType")),
        }
      );
    }

    return result;
  }

  private static List<T>? CopyList<T>(object? source)
  {
    if (source is not IEnumerable enumerable)
    {
      return null;
    }

    var result = new List<T>();
    foreach (object? item in enumerable)
    {
      if (item is T typed)
      {
        result.Add(typed);
      }
    }

    return result;
  }

  private static CmfMatchType ConvertMatchType(object? source)
  {
    if (source is null)
    {
      return CmfMatchType.Any;
    }

    string name = source.ToString() ?? "Any";
    return Enum.TryParse(name, ignoreCase: true, out CmfMatchType parsed)
      ? parsed
      : CmfMatchType.Any;
  }

  private static T? GetProp<T>(Type type, object instance, string name)
    where T : class
  {
    PropertyInfo? prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    return prop?.GetValue(instance) as T;
  }

  private static object? GetCmfMuseumData()
  {
    PropertyInfo? prop = _cachedMuseumDataProp ?? ResolveMuseumDataProperty();
    if (prop is null)
    {
      return null;
    }

    return prop.GetValue(null);
  }

  private static PropertyInfo? ResolveMuseumDataProperty()
  {
    Type? cmfType = _cachedCmfType ?? ResolveCmfType();
    if (cmfType is null)
    {
      return null;
    }

    _cachedMuseumDataProp = cmfType.GetProperty(
      "MuseumData",
      BindingFlags.Public | BindingFlags.Static
    );
    return _cachedMuseumDataProp;
  }

  private static Type? ResolveCmfType()
  {
    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
    {
      Type? t = asm.GetType(CmfTypeName, throwOnError: false);
      if (t is not null)
      {
        _cachedCmfType = t;
        return t;
      }
    }
    return null;
  }

  private static bool MatchesAny(Item item, List<CmfDonationRequirement> requirements)
  {
    for (int i = 0; i < requirements.Count; i++)
    {
      if (DoesItemSatisfyRequirement(item, requirements[i]))
      {
        return true;
      }
    }

    return false;
  }

  private static bool DoesItemSatisfyRequirement(Item item, CmfDonationRequirement req)
  {
    if (req.ItemIds is null && req.Categories is null && req.ContextTags is null)
    {
      return true;
    }

    switch (req.MatchType)
    {
      case CmfMatchType.Any:
        if (req.ItemIds is not null && req.ItemIds.Contains(item.QualifiedItemId))
        {
          return true;
        }
        if (req.Categories is not null && req.Categories.Contains(item.Category))
        {
          return true;
        }
        if (
          req.ContextTags is not null
          && ItemContextTagManager.DoAnyTagsMatch(req.ContextTags, item.GetContextTags())
        )
        {
          return true;
        }
        return false;

      case CmfMatchType.All:
        if (req.ItemIds is not null && !req.ItemIds.Contains(item.QualifiedItemId))
        {
          return false;
        }
        if (req.Categories is not null && !req.Categories.Contains(item.Category))
        {
          return false;
        }
        return req.ContextTags is null
          || ItemContextTagManager.DoAnyTagsMatch(req.ContextTags, item.GetContextTags());

      default:
        return false;
    }
  }
}
