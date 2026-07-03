using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Machines;
using StardewValley.Objects;
using Object = StardewValley.Object;

namespace UIInfoSuite2Alt.Infrastructure.Helpers;

/// <summary>Probes Data/Machines to compute artisan output sell prices, skipping delegate-output rules.</summary>
public static class ArtisanPriceHelper
{
  public readonly struct ArtisanEntry
  {
    public readonly string MachineQualifiedId;
    public readonly int UnitSellPrice;
    public readonly int MinOutputStack;
    public readonly int MaxOutputStack;
    public readonly int InputsPerBatch;
    public readonly Item OutputItem;

    public ArtisanEntry(
      string machineQualifiedId,
      int unitSellPrice,
      int minOutputStack,
      int maxOutputStack,
      int inputsPerBatch,
      Item outputItem
    )
    {
      MachineQualifiedId = machineQualifiedId;
      UnitSellPrice = unitSellPrice;
      MinOutputStack = minOutputStack;
      MaxOutputStack = maxOutputStack;
      InputsPerBatch = inputsPerBatch;
      OutputItem = outputItem;
    }
  }

  // Machines excluded from tooltip rows.
  private static readonly HashSet<string> ExcludedMachines = new()
  {
    "(BC)163", // Cask
    "(BC)21", // Crystalarium
    "(BC)20", // Recycling Machine
    "(BC)25", // Seed Maker
    "(BC)156", // Slime Incubator
    "(BC)114", // Charcoal Kiln
    "(BC)158", // Slime Egg-Press
    "(BC)drbirbdev.BinningSkill_Composter", // Binning Skill Composter
  };

  private static readonly HashSet<string> _ownedMachines = new();
  private static Dictionary<string, string> _recipeByMachineQid = new();
  private static readonly Dictionary<string, ArtisanEntry[]> _cache = new();
  private static bool _eventsHooked;

  // Static trigger index: "which machines could possibly match this input?"
  // Parsed once from Data/Machines so hovers don't walk all ~56 machines.
  private static readonly Dictionary<string, List<string>> _machinesByRequiredQid = new();
  private static readonly Dictionary<string, List<string>> _machinesByRequiredTag = new();
  private static readonly HashSet<string> _universalMachines = new();
  private static bool _triggerIndexBuilt;

  // Trace per-machine probe decisions. Off by default.
  private static bool _traceLoggingEnabled;

  // True during probing; SuppressProbeNoisePatch uses it to silence EMC red messages.
  public static bool IsProbing { get; private set; }

  public static void EnsureInitialized(IModHelper helper)
  {
    if (_eventsHooked)
    {
      return;
    }

    helper.Events.GameLoop.DayStarted += (_, _) =>
    {
      _cache.Clear();
      RefreshOwnedMachines();
    };
    helper.Events.GameLoop.SaveLoaded += (_, _) =>
    {
      RebuildRecipeMap();
      _cache.Clear();
      // Defer ownership scan: at SaveLoaded, Utility.ForEachLocation can miss
      // objects that haven't been finalised yet, leaving _ownedMachines empty.
      // With OnlyShowKnownArtisanMachines on, that cached empty results until
      // the next DayStarted. Re-run once the first UpdateTicked fires.
      _deferredOwnershipRefresh = true;
    };
    helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    helper.Events.Content.AssetReady += OnAssetReady;
    _eventsHooked = true;

    // Lazy init fires after SaveLoaded/DayStarted have passed - prime state now.
    if (Context.IsWorldReady)
    {
      RebuildRecipeMap();
      _deferredOwnershipRefresh = true;
    }
  }

  private static bool _deferredOwnershipRefresh;

  private static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (!_deferredOwnershipRefresh || !Context.IsWorldReady)
    {
      return;
    }
    _deferredOwnershipRefresh = false;
    RefreshOwnedMachines();
    _cache.Clear();
  }

  public static void SetTraceLogging(bool enabled)
  {
    _traceLoggingEnabled = enabled;
    _cache.Clear();
  }

  public static bool IsMachineKnownOrOwned(string machineQualifiedId)
  {
    if (
      Game1.player != null
      && _recipeByMachineQid.TryGetValue(machineQualifiedId, out string? recipe)
      && Game1.player.craftingRecipes.ContainsKey(recipe)
    )
    {
      return true;
    }

    return _ownedMachines.Contains(machineQualifiedId);
  }

  private static void RebuildRecipeMap()
  {
    var map = new Dictionary<string, string>();
    Dictionary<string, string> recipes = DataLoader.CraftingRecipes(Game1.content);
    foreach ((string recipeName, string data) in recipes)
    {
      // Format: "ingredients / unused / outputItemId[ outputStack] / isBigCraftable / ..."
      string[] parts = data.Split('/');
      if (parts.Length < 4)
      {
        continue;
      }

      string[] outputParts = parts[2].Trim().Split(' ');
      if (outputParts.Length == 0 || string.IsNullOrEmpty(outputParts[0]))
      {
        continue;
      }

      if (!parts[3].Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      map["(BC)" + outputParts[0]] = recipeName;
    }
    _recipeByMachineQid = map;
  }

  private static void RefreshOwnedMachines()
  {
    _ownedMachines.Clear();
    if (!Context.IsWorldReady || Game1.player == null)
    {
      return;
    }

    Dictionary<string, MachineData> machines = DataLoader.Machines(Game1.content);

    foreach (Item? item in Game1.player.Items)
    {
      if (item != null)
      {
        CheckAndAddOwned(item, machines);
      }
    }

    Utility.ForEachLocation(loc =>
    {
      foreach (Object obj in loc.Objects.Values)
      {
        CheckAndAddOwned(obj, machines);
        if (obj is Chest chest)
        {
          foreach (Item? stored in chest.Items)
          {
            if (stored != null)
            {
              CheckAndAddOwned(stored, machines);
            }
          }
        }
      }
      return true;
    });
  }

  private static void CheckAndAddOwned(Item item, Dictionary<string, MachineData> machines)
  {
    if (machines.ContainsKey(item.QualifiedItemId))
    {
      _ownedMachines.Add(item.QualifiedItemId);
    }
  }

  private static void OnAssetReady(object? sender, AssetReadyEventArgs e)
  {
    if (e.Name.IsEquivalentTo("Data/Machines"))
    {
      _cache.Clear();
      InvalidateTriggerIndex();
    }
    else if (e.Name.IsEquivalentTo("Data/Objects"))
    {
      _cache.Clear();
    }
    if (e.Name.IsEquivalentTo("Data/CraftingRecipes"))
    {
      RebuildRecipeMap();
    }
  }

  private static void InvalidateTriggerIndex()
  {
    _machinesByRequiredQid.Clear();
    _machinesByRequiredTag.Clear();
    _universalMachines.Clear();
    _triggerIndexBuilt = false;
  }

  // Builds a static index: input qid/tag -> machines that could match. Rules with no
  // qid/tag filter go in a "universal" bucket that always probes.
  private static void BuildTriggerIndex()
  {
    _machinesByRequiredQid.Clear();
    _machinesByRequiredTag.Clear();
    _universalMachines.Clear();

    Dictionary<string, MachineData> machinesData = DataLoader.Machines(Game1.content);
    foreach ((string machineQid, MachineData? machineData) in machinesData)
    {
      if (machineData?.OutputRules == null || ExcludedMachines.Contains(machineQid))
      {
        continue;
      }

      foreach (MachineOutputRule? rule in machineData.OutputRules)
      {
        if (rule?.Triggers == null)
        {
          continue;
        }

        foreach (MachineOutputTriggerRule? trig in rule.Triggers)
        {
          if (trig == null)
          {
            continue;
          }
          if (!trig.Trigger.HasFlag(MachineOutputTrigger.ItemPlacedInMachine))
          {
            continue;
          }

          bool hasFilter = false;

          if (!string.IsNullOrEmpty(trig.RequiredItemId))
          {
            string? qid = null;
            try
            {
              qid = ItemRegistry.QualifyItemId(trig.RequiredItemId);
            }
            catch (Exception ex)
            {
              // Malformed id - fall back to the raw RequiredItemId below.
              ModEntry.MonitorObject.LogOnce(
                $"ArtisanPriceHelper: failed to qualify RequiredItemId '{trig.RequiredItemId}'; using raw id, {ex.Message}",
                LogLevel.Trace
              );
            }
            string key = qid ?? trig.RequiredItemId;
            AddToIndex(_machinesByRequiredQid, key, machineQid);
            hasFilter = true;
          }

          if (trig.RequiredTags != null && trig.RequiredTags.Count > 0)
          {
            // AND-semantics. Index under first positive tag; skip leading negations since
            // no item has a "!tag". Probe time re-validates all tags for correctness.
            string? indexTag = null;
            foreach (string tag in trig.RequiredTags)
            {
              if (!string.IsNullOrEmpty(tag) && tag[0] != '!')
              {
                indexTag = tag;
                break;
              }
            }

            if (indexTag != null)
            {
              AddToIndex(_machinesByRequiredTag, indexTag, machineQid);
              hasFilter = true;
            }
            else
            {
              // All required tags are negations - we can't narrow, so always probe.
              _universalMachines.Add(machineQid);
              hasFilter = true;
            }
          }

          if (!hasFilter)
          {
            _universalMachines.Add(machineQid);
          }
        }
      }
    }

    _triggerIndexBuilt = true;

    if (_traceLoggingEnabled)
    {
      ModEntry.MonitorObject.Log(
        $"ArtisanPriceHelper: trigger index built - {_machinesByRequiredQid.Count} qid keys, {_machinesByRequiredTag.Count} tag keys, {_universalMachines.Count} universal machines",
        LogLevel.Trace
      );
    }
  }

  private static void AddToIndex(
    Dictionary<string, List<string>> index,
    string key,
    string machineQid
  )
  {
    if (!index.TryGetValue(key, out List<string>? list))
    {
      list = new List<string>();
      index[key] = list;
    }
    if (!list.Contains(machineQid))
    {
      list.Add(machineQid);
    }
  }

  private static HashSet<string> GetCandidateMachines(Object inputObj)
  {
    if (!_triggerIndexBuilt)
    {
      BuildTriggerIndex();
    }

    var candidates = new HashSet<string>(_universalMachines);

    if (
      !string.IsNullOrEmpty(inputObj.QualifiedItemId)
      && _machinesByRequiredQid.TryGetValue(inputObj.QualifiedItemId, out List<string>? byQid)
    )
    {
      candidates.UnionWith(byQid);
    }

    foreach (string tag in inputObj.GetContextTags())
    {
      if (_machinesByRequiredTag.TryGetValue(tag, out List<string>? byTag))
      {
        candidates.UnionWith(byTag);
      }
    }

    return candidates;
  }

  /// <summary>
  /// Returns every valid machine output for the given input. Empty array if the
  /// input isn't a probable input for any machine. When <paramref name="filterKnownOnly"/>
  /// is true, machines the player doesn't know or own are skipped before probing.
  /// </summary>
  public static ArtisanEntry[] GetEntries(Item? input, bool filterKnownOnly = false)
  {
    if (input is not Object inputObj || inputObj.QualifiedItemId == null)
    {
      return Array.Empty<ArtisanEntry>();
    }

    // Include preservedParentSheetIndex so distinct preserved items (e.g. fish roe variants)
    // don't collide - they share QualifiedItemId but differ in output price/color/name.
    string? preserveId = inputObj.preservedParentSheetIndex.Value;
    string cacheKey =
      preserveId != null
        ? inputObj.QualifiedItemId
          + "|"
          + inputObj.Quality
          + "|"
          + (filterKnownOnly ? 1 : 0)
          + "|"
          + preserveId
        : inputObj.QualifiedItemId + "|" + inputObj.Quality + "|" + (filterKnownOnly ? 1 : 0);
    if (_cache.TryGetValue(cacheKey, out ArtisanEntry[]? cached))
    {
      return cached;
    }

    bool trace = _traceLoggingEnabled;

    HashSet<string> candidates = GetCandidateMachines(inputObj);
    if (candidates.Count == 0)
    {
      _cache[cacheKey] = Array.Empty<ArtisanEntry>();
      return Array.Empty<ArtisanEntry>();
    }

    if (trace)
    {
      ModEntry.MonitorObject.Log(
        $"ArtisanPriceHelper: probing input {inputObj.QualifiedItemId} (Q{inputObj.Quality}, \"{inputObj.DisplayName}\") - {candidates.Count} candidate machines",
        LogLevel.Trace
      );
    }

    var results = new List<ArtisanEntry>();
    // Dedupe key: output qualified id + quality. Collapses Juicer's three same-output rules
    // AND cross-machine duplicates like vanilla Dehydrator + Cornucopia Drying Rack both
    // producing DriedFlower. First match wins.
    var seenOutputs = new HashSet<string>();
    Dictionary<string, MachineData> machinesData = DataLoader.Machines(Game1.content);
    GameLocation location = Game1.currentLocation ?? Game1.getFarm();

    // Inflate stack so RequiredCount > 1 rules still match.
    Item probeInput = inputObj.getOne();
    probeInput.Quality = inputObj.Quality;
    probeInput.Stack = int.MaxValue;

    IsProbing = true;
    try
    {
      // Iterate in Data/Machines order so display is deterministic.
      foreach ((string machineQid, MachineData? machineData) in machinesData)
      {
        if (machineData == null || !candidates.Contains(machineQid))
        {
          continue;
        }

        if (ExcludedMachines.Contains(machineQid))
        {
          continue;
        }

        if (filterKnownOnly && !IsMachineKnownOrOwned(machineQid))
        {
          if (trace)
          {
            ModEntry.MonitorObject.Log(
              $"ArtisanPriceHelper:   skip {machineQid} (not known/owned)",
              LogLevel.Trace
            );
          }
          continue;
        }

        Object machine;
        try
        {
          machine = (Object)ItemRegistry.Create(machineQid);
        }
        catch
        {
          if (trace)
          {
            ModEntry.MonitorObject.Log(
              $"ArtisanPriceHelper:   skip {machineQid} (ItemRegistry.Create threw)",
              LogLevel.Trace
            );
          }
          continue;
        }

        if (
          !MachineDataUtility.TryGetMachineOutputRule(
            machine,
            machineData,
            MachineOutputTrigger.ItemPlacedInMachine,
            probeInput,
            Game1.player,
            location,
            out MachineOutputRule? rule,
            out MachineOutputTriggerRule? triggerRule,
            out _,
            out _
          )
        )
        {
          if (trace)
          {
            ModEntry.MonitorObject.Log(
              $"ArtisanPriceHelper:   no rule matched for {machineQid}",
              LogLevel.Trace
            );
          }
          continue;
        }

        MachineItemOutput? outputData = MachineDataUtility.GetOutputData(
          machine,
          machineData,
          rule,
          probeInput,
          Game1.player,
          location
        );
        if (outputData == null)
        {
          if (trace)
          {
            ModEntry.MonitorObject.Log(
              $"ArtisanPriceHelper:   {machineQid} rule={rule.Id} but GetOutputData returned null",
              LogLevel.Trace
            );
          }
          continue;
        }

        // C# delegate outputs aren't introspectable.
        if (outputData.OutputMethod != null)
        {
          if (trace)
          {
            ModEntry.MonitorObject.Log(
              $"ArtisanPriceHelper:   skip {machineQid} rule={rule.Id} (OutputMethod delegate)",
              LogLevel.Trace
            );
          }
          continue;
        }

        Item? output;
        try
        {
          output = MachineDataUtility.GetOutputItem(
            machine,
            outputData,
            probeInput,
            Game1.player,
            probe: true,
            out _
          );
        }
        catch (Exception ex)
        {
          if (trace)
          {
            ModEntry.MonitorObject.Log(
              $"ArtisanPriceHelper:   {machineQid} rule={rule.Id} GetOutputItem threw, {ex.Message}",
              LogLevel.Trace
            );
          }
          continue;
        }

        if (output is not Object outObj || outObj.Price <= 0)
        {
          if (trace)
          {
            ModEntry.MonitorObject.Log(
              $"ArtisanPriceHelper:   {machineQid} rule={rule.Id} output invalid or price<=0",
              LogLevel.Trace
            );
          }
          continue;
        }

        int unit = Tools.GetSellToStorePrice(outObj);
        if (unit <= 0)
        {
          continue;
        }

        string dedupeKey = outObj.QualifiedItemId + "|" + outObj.Quality;
        if (!seenOutputs.Add(dedupeKey))
        {
          if (trace)
          {
            ModEntry.MonitorObject.Log(
              $"ArtisanPriceHelper:   skip {machineQid} rule={rule.Id} (dup output {dedupeKey})",
              LogLevel.Trace
            );
          }
          continue;
        }

        int minStack = outputData.MinStack > 0 ? outputData.MinStack : 1;
        int maxStack = outputData.MaxStack > 0 ? outputData.MaxStack : minStack;
        if (maxStack < minStack)
        {
          maxStack = minStack;
        }

        int inputsPerBatch = triggerRule?.RequiredCount > 0 ? triggerRule.RequiredCount : 1;
        results.Add(new ArtisanEntry(machineQid, unit, minStack, maxStack, inputsPerBatch, outObj));

        if (trace)
        {
          ModEntry.MonitorObject.Log(
            $"ArtisanPriceHelper:   MATCH {machineQid} rule={rule.Id} -> {outObj.QualifiedItemId} x{minStack}-{maxStack} @ {unit}g (batch {inputsPerBatch})",
            LogLevel.Trace
          );
        }
      }
    }
    finally
    {
      IsProbing = false;
    }

    // Stable sort: vanilla first, probe order within each group.
    ArtisanEntry[] arr = results
      .OrderBy(e => IsVanillaMachine(e.MachineQualifiedId) ? 0 : 1)
      .ToArray();
    _cache[cacheKey] = arr;
    return arr;
  }

  // 1.6+ vanilla machines that use string ids instead of numeric ids.
  private static readonly HashSet<string> VanillaStringMachines = new()
  {
    "(BC)Anvil",
    "(BC)BaitMaker",
    "(BC)Dehydrator",
    "(BC)DeluxeWormBin",
    "(BC)FishSmoker",
    "(BC)HeavyFurnace",
    "(BC)MushroomLog",
  };

  private static bool IsVanillaMachine(string machineQualifiedId)
  {
    const string prefix = "(BC)";
    if (!machineQualifiedId.StartsWith(prefix) || machineQualifiedId.Length <= prefix.Length)
    {
      return false;
    }

    if (VanillaStringMachines.Contains(machineQualifiedId))
    {
      return true;
    }

    for (int i = prefix.Length; i < machineQualifiedId.Length; i++)
    {
      if (!char.IsDigit(machineQualifiedId[i]))
      {
        return false;
      }
    }
    return true;
  }
}
