using System.Collections.Generic;

namespace UIInfoSuite2Alt.Compatibility;

/// <summary>The API provided by the Flexible Upgrades mod (yousv.FlexibleUpgrades).</summary>
public interface IFlexibleUpgradesApi
{
  /// <summary>All tool upgrades in progress for the current player, vanilla slot first.</summary>
  IReadOnlyList<(
    string DisplayName,
    int DaysLeft,
    string QualifiedItemId,
    bool IsVanilla
  )> GetToolOrders();

  /// <summary>All Robin projects in progress for the current player: house, community, buildings.</summary>
  IReadOnlyList<(string Kind, string Name, int DaysLeft, string BuildingId)> GetRobinProjects();
}
