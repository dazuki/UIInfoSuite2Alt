using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.Compatibility.Helpers;

internal static class InformantHelper
{
  private static IModHelper _helper = null!;
  private static Texture2D? _aquariumIcon;
  private static bool _aquariumIconLoaded;

  private static bool _isLoaded;
  private static object? _modInstance;
  private static FieldInfo? _configField;
  private static PropertyInfo? _displayIdsProperty;
  private static bool _reflectionInitialized;

  // Cached snapshot of Informant's DisplayIds, refreshed every 60 ticks (~1 second)
  private static Dictionary<string, bool>? _cachedDisplayIds;
  private const uint RefreshInterval = 60;

  /// <summary>
  /// Whether the Informant mod is installed and loaded.
  /// </summary>
  public static bool IsLoaded => _isLoaded;

  public static void Initialize(IModHelper helper)
  {
    _isLoaded = helper.ModRegistry.IsLoaded(ModCompat.Informant);
    if (!_isLoaded)
    {
      return;
    }

    InitializeReflection();
    RefreshCache();
    RegisterDecorators(helper);

    helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
  }

  private static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (e.IsMultipleOf(RefreshInterval))
    {
      RefreshCache();
    }
  }

  private static void RefreshCache()
  {
    _cachedDisplayIds = GetDisplayIds();
  }

  /// <summary>
  /// Checks whether a specific Informant feature is currently enabled in Informant's config.
  /// Returns false if Informant is not loaded or the feature is disabled.
  /// </summary>
  public static bool IsFeatureEnabled(string featureId)
  {
    if (!_isLoaded)
    {
      return false;
    }

    if (_cachedDisplayIds == null)
    {
      // Reflection failed - assume Informant features are active (safe default)
      return true;
    }

    return _cachedDisplayIds.GetValueOrDefault(featureId, true);
  }

  private static void InitializeReflection()
  {
    try
    {
      Type? modType = null;
      foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
      {
        try
        {
          modType = assembly.GetType("Slothsoft.Informant.InformantMod");
          if (modType != null)
          {
            break;
          }
        }
        catch
        {
          // Skip assemblies that can't be inspected
        }
      }

      if (modType == null)
      {
        ModEntry.MonitorObject.Log(
          "InformantHelper: could not find InformantMod type via reflection",
          LogLevel.Warn
        );
        return;
      }

      FieldInfo? instanceField = modType.GetField(
        "Instance",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
      );
      _modInstance = instanceField?.GetValue(null);

      if (_modInstance == null)
      {
        ModEntry.MonitorObject.Log("InformantHelper: InformantMod.Instance is null", LogLevel.Warn);
        return;
      }

      _configField = _modInstance
        .GetType()
        .GetField("Config", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
      if (_configField == null)
      {
        ModEntry.MonitorObject.Log(
          "InformantHelper: could not find Config field on InformantMod",
          LogLevel.Warn
        );
        return;
      }

      object? config = _configField.GetValue(_modInstance);
      if (config != null)
      {
        _displayIdsProperty = config.GetType().GetProperty("DisplayIds");
      }

      _reflectionInitialized = _displayIdsProperty != null;
      if (!_reflectionInitialized)
      {
        ModEntry.MonitorObject.Log(
          "InformantHelper: could not find DisplayIds property on config",
          LogLevel.Warn
        );
      }
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"InformantHelper: reflection setup failed, {ex.Message}",
        LogLevel.Warn
      );
    }
  }

  private static Dictionary<string, bool>? GetDisplayIds()
  {
    if (!_reflectionInitialized || _modInstance == null || _configField == null)
    {
      return null;
    }

    try
    {
      object? config = _configField.GetValue(_modInstance);
      return config == null
        ? null
        : _displayIdsProperty?.GetValue(config) as Dictionary<string, bool>;
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"InformantHelper: failed to read Informant config, {ex.Message}",
        LogLevel.Trace
      );
      return null;
    }
  }

  private static void RegisterDecorators(IModHelper helper)
  {
    var api = helper.ModRegistry.GetApi<IInformantApi>(ModCompat.Informant);
    bool aquarium = helper.ModRegistry.IsLoaded(ModCompat.StardewAquarium);

    if (api == null)
    {
      ModEntry.MonitorObject.Log(
        "InformantHelper: Informant detected but API unavailable",
        LogLevel.Warn
      );
      return;
    }

    // Register Aquarium decorator if Stardew Aquarium is installed
    // but Informant's own Aquarium addon isn't already handling it
    if (aquarium && !helper.ModRegistry.IsLoaded(ModCompat.InformantAquarium))
    {
      _helper = helper;

      api.AddItemDecorator(
        "uiis2alt-aquarium",
        () => "Stardew Aquarium",
        () => "Shows an icon on fish not yet donated to the Aquarium",
        GetAquariumDecoratorIcon
      );
    }
  }

  private static Texture2D? GetAquariumDecoratorIcon(Item item)
  {
    if (!AquariumHelper.IsUndonatedAquariumFish(item))
    {
      return null;
    }

    if (!_aquariumIconLoaded)
    {
      _aquariumIconLoaded = true;
      try
      {
        Texture2D curatorSheet = _helper.GameContent.Load<Texture2D>("Characters/Curator");
        _aquariumIcon = Tools.CropTexture(curatorSheet, new Rectangle(0, 1, 16, 16));
      }
      catch (Exception ex)
      {
        ModEntry.MonitorObject.Log(
          $"InformantHelper: failed to load Curator sprite for Aquarium decorator, {ex.Message}",
          LogLevel.Warn
        );
      }
    }

    return _aquariumIcon;
  }
}
