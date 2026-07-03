using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StardewModdingAPI;

namespace UIInfoSuite2Alt.Compatibility.Helpers;

/// <summary>
/// Compat for aedenthorn's Launcher Drawer. It draws a drawer column over the money box during the
/// vanilla HUD pass, which our icons (drawn later on RenderedHud) would paint over. LD exposes no
/// API, so we read its open state via reflection and shift our icons left while it is visible.
/// Fragile to LD internals changing; the reads fail closed (no shift) if members go missing.
/// </summary>
public static class LauncherDrawerHelper
{
  /// <summary>How far left (px) to push the icon row to clear the ~220px-wide drawer column.</summary>
  public const int DrawerClearance = 194;

  private static bool _initialized;
  private static object? _drawerStatePerScreen;
  private static PropertyInfo? _perScreenValue;

  // Config can be replaced (GMCM reset), so hold the field and re-read rather than caching the value.
  private static FieldInfo? _configField;
  private static PropertyInfo? _customPositionProp;

  /// <summary>Resolve LD's state and config members once. Call after GameLaunched, when LD is loaded.</summary>
  public static void Initialize(IMonitor monitor)
  {
    if (_initialized)
    {
      return;
    }

    _initialized = true;

    Assembly? assembly = AppDomain
      .CurrentDomain.GetAssemblies()
      .FirstOrDefault(a => a.GetName().Name == "LauncherDrawer");
    Type? modEntry = assembly?.GetType("LauncherDrawer.ModEntry");
    FieldInfo? field = modEntry?.GetField(
      "currentDrawerState",
      BindingFlags.Public | BindingFlags.Static
    );

    // PerScreen<DrawerState>; DrawerState is LD-internal, so read it as a boxed enum.
    _drawerStatePerScreen = field?.GetValue(null);
    _perScreenValue = _drawerStatePerScreen?.GetType().GetProperty("Value");

    _configField = modEntry?.GetField("Config", BindingFlags.Public | BindingFlags.Static);
    _customPositionProp = _configField?.FieldType.GetProperty("CustomPosition");

    // Warn if any reflected member is missing (LD changed) so players can report it; reads fail closed.
    var missing = new List<string>();
    if (modEntry == null)
    {
      missing.Add("ModEntry");
    }
    if (_drawerStatePerScreen == null)
    {
      missing.Add("currentDrawerState");
    }
    if (_perScreenValue == null)
    {
      missing.Add("currentDrawerState.Value");
    }
    if (_configField == null)
    {
      missing.Add("Config");
    }
    if (_customPositionProp == null)
    {
      missing.Add("Config.CustomPosition");
    }

    if (missing.Count > 0)
    {
      monitor.Log(
        $"LauncherDrawerHelper: Launcher Drawer internals changed, icon overlap compat is disabled - please report this, missing=[{string.Join(", ", missing)}]",
        LogLevel.Warn
      );
    }
  }

  /// <summary>True when the drawer is anything but fully closed (DrawerState.Closed == 0).</summary>
  public static bool IsDrawerOpen
  {
    get
    {
      if (_drawerStatePerScreen == null || _perScreenValue == null)
      {
        return false;
      }

      object? value = _perScreenValue.GetValue(_drawerStatePerScreen);
      return value != null && Convert.ToInt32(value) != 0;
    }
  }

  /// <summary>True when the drawer was moved off the money box, where it can't overlap our icons.</summary>
  public static bool IsCustomPosition
  {
    get
    {
      if (_configField == null || _customPositionProp == null)
      {
        return false;
      }

      object? config = _configField.GetValue(null);
      return config != null && _customPositionProp.GetValue(config) is true;
    }
  }

  /// <summary>True when our icon row should slide left to clear the drawer's column this frame.</summary>
  public static bool ShouldShiftIcons => IsDrawerOpen && !IsCustomPosition;
}
