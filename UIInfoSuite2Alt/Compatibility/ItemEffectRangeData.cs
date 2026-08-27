namespace UIInfoSuite2Alt.Compatibility;

/// <summary>Area shape of a mod-declared item effect range.</summary>
public enum ItemEffectRangeShape
{
  /// <summary>Square area, as produced by a nested x/y loop.</summary>
  Square,

  /// <summary>Euclidean circle, as produced by a distance check.</summary>
  Circle,

  /// <summary>Diamond, as produced by a Manhattan distance check.</summary>
  Diamond,
}

/// <summary>An effect range declared by another mod, keyed by qualified item ID.</summary>
public class ItemEffectRangeData
{
  /// <summary>Effect radius in tiles, measured from the object's own tile.</summary>
  public int Radius { get; set; }

  /// <summary>Raw shape name. Kept as text so a typo warns instead of killing the whole asset.</summary>
  public string? Shape { get; set; }

  /// <summary><see cref="Shape"/> resolved at load time, defaulting to a square.</summary>
  internal ItemEffectRangeShape ResolvedShape { get; set; } = ItemEffectRangeShape.Square;

  /// <summary>Optional line shown under the object name in the range tooltip.</summary>
  public string? EffectLabel { get; set; }

  /// <summary>Limits the highlight to tiles a crop can occupy, and enables overlap tracking.</summary>
  public bool AffectsCrops { get; set; }
}
