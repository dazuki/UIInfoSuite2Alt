using System;
using System.Linq;
using System.Reflection;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace UIInfoSuite2Alt.Compatibility.Helpers;

/// <summary>
/// Compat for MindMeltMax's Better Shipping Bin. It replaces the shipping bin's ItemGrabMenu with its
/// own MenuWithInventory subclass and tracks hovers in a public HoveredItem field (distinct from the
/// vanilla hoveredItem, which never sees bin-grid items). No API, so the field is read via reflection.
/// </summary>
public static class BetterShippingBinHelper
{
  private const string BinMenuTypeName = "BetterShipping.BinMenuOverride";
  private const string HoveredItemFieldName = "HoveredItem";

  private static bool _initialized;
  private static Type? _binMenuType;
  private static FieldInfo? _hoveredItemField;

  public static void Initialize(IMonitor monitor)
  {
    if (_initialized)
    {
      return;
    }

    _initialized = true;

    _binMenuType = AppDomain
      .CurrentDomain.GetAssemblies()
      .Select(a => a.GetType(BinMenuTypeName))
      .FirstOrDefault(t => t != null);
    _hoveredItemField = _binMenuType?.GetField(
      HoveredItemFieldName,
      BindingFlags.Public | BindingFlags.Instance
    );

    if (_hoveredItemField == null || !typeof(Item).IsAssignableFrom(_hoveredItemField.FieldType))
    {
      _hoveredItemField = null;
      monitor.Log(
        $"BetterShippingBinHelper: Better Shipping Bin internals changed, bin menu tooltips are disabled - please report this, type={_binMenuType?.FullName ?? "missing"}",
        LogLevel.Warn
      );
    }
  }

  public static bool TryGetHoveredItem(IClickableMenu? menu, out Item? item)
  {
    item = null;
    if (_hoveredItemField == null || menu == null || menu.GetType() != _binMenuType)
    {
      return false;
    }

    item = _hoveredItemField.GetValue(menu) as Item;
    return true;
  }
}
