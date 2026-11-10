using System;
using System.Linq;
using System.Reflection;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace UIInfoSuite2Alt.Compatibility.Helpers;

/// <summary>
/// Compat for WisnuNug's MobileUI. It swaps the date box for its own menu but leaves
/// Game1.dayTimeMoneyBox pointing at the removed vanilla one. No API, so its journal button is read via reflection.
/// </summary>
public static class MobileUiHelper
{
  private const string DateBoxTypeName = "MobileUI.WisnuNug";

  private static bool _initialized;
  private static Type? _dateBoxType;
  private static FieldInfo? _questButtonField;

  public static void Initialize(IMonitor monitor)
  {
    if (_initialized)
    {
      return;
    }

    _initialized = true;

    _dateBoxType = AppDomain
      .CurrentDomain.GetAssemblies()
      .Select(a => a.GetType(DateBoxTypeName))
      .FirstOrDefault(t => t != null);
    _questButtonField = _dateBoxType?.GetField(
      "questButton",
      BindingFlags.Public | BindingFlags.Instance
    );

    if (_questButtonField?.FieldType != typeof(ClickableTextureComponent))
    {
      _questButtonField = null;
      monitor.Log(
        $"MobileUiHelper: MobileUI internals changed, HUD icons may overlap its date box - please report this, type={_dateBoxType?.FullName ?? "missing"}",
        LogLevel.Warn
      );
    }
  }

  /// <summary>MobileUI's journal button, or null when its date box is not active.</summary>
  public static ClickableTextureComponent? QuestButton
  {
    get
    {
      if (_questButtonField == null)
      {
        return null;
      }

      foreach (IClickableMenu menu in Game1.onScreenMenus)
      {
        if (menu.GetType() == _dateBoxType)
        {
          return _questButtonField.GetValue(menu) as ClickableTextureComponent;
        }
      }

      return null;
    }
  }
}
