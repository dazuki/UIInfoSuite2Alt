using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using StardewModdingAPI;

namespace UIInfoSuite2Alt.Compatibility;

public static class ModCompat
{
  public const string ContentPatcher = "Pathoschild.ContentPatcher";
  public const string CustomBush = "furyx639.CustomBush";
  public const string Gmcm = "spacechase0.GenericModConfigMenu";
  public const string CloudySkies = "leclair.cloudyskies";
  public const string DeluxeJournal = "MolsonCAD.DeluxeJournal";
  public const string BetterGameMenu = "leclair.bettergamemenu";
  public const string FerngillEconomy = "paulsteele.fse";
  public const string RidgesideVillage = "Rafseazz.RidgesideVillage";
  public const string SunberryVillage = "SunberryTeam.SBVSMAPI";
  public const string SunberryVillageCp = "skellady.SBVCP";
  public const string EscasModdingPlugins = "Esca.EMP";
  public const string SwordAndSorcery = "KCC.SnS";
  public const string NpcMapLocations = "Bouhm.NPCMapLocations";
  public const string SpaceCore = "spacechase0.SpaceCore";
  public const string VanillaPlusProfessions = "KediDili.VanillaPlusProfessions";
  public const string UnlockableBundles = "DLX.Bundles";
  public const string BetterRanching = "BetterRanching";
  public const string Informant = "Slothsoft.Informant";
  public const string InformantAquarium = "Slothsoft.Informant.Aquarium";
  public const string StardewAquarium = "Cherry.StardewAquarium";
  public const string DailyTasksReportPlus = "Prism99.DailyTasksReportPlus";
  public const string ShowItemQuality = "Jonqora.ShowItemQuality";
  public const string FarmTypeManager = "Esca.FarmTypeManager";
  public const string ArchaeologySkill = "moonslime.ArchaeologySkill";
  public const string BetterJunimos = "hawkfalcon.BetterJunimos";
  public const string WalkOfLife = "DaLion.Professions";
  public const string ExtraMachineConfig = "selph.ExtraMachineConfig";
  public const string AnimalHusbandry = "DIGUS.ANIMALHUSBANDRYMOD";
  public const string BiggerBackpack = "spacechase0.BiggerBackpack";
  public const string FullInventoryView = "CpdnCristiano.FullInventoryView";
  public const string CatValley = "RimeNovi.CatValley";
  public const string CornucopiaArtisanMachines = "Cornucopia.ArtisanMachines";
  public const string BinningSkill = "drbirbdev.BinningSkill";
  public const string GarbageDay = "furyx639.GarbageDay";
  public const string CustomMuseumFramework = "Spiderbuttons.CMF";
  public const string LauncherDrawer = "aedenthorn.LauncherDrawer";
  public const string ChestsAnywhere = "Pathoschild.ChestsAnywhere";

  // original UIInfoSuite variants
  public const string UIInfoSuite2 = "Annosz.UiInfoSuite2";
  public const string UIInfoSuite = "Cdaragorn.UiInfoSuite";
}

public static class ApiManager
{
  private static readonly Dictionary<string, object> RegisteredApis = [];

  public static T? TryRegisterApi<T>(
    IModHelper helper,
    string modId,
    string? minimumVersion = null,
    bool warnIfNotPresent = false
  )
    where T : class
  {
    IModInfo? modInfo = helper.ModRegistry.Get(modId);
    if (modInfo == null)
      return null;

    if (minimumVersion != null && modInfo.Manifest.Version.IsOlderThan(minimumVersion))
    {
      ModEntry.MonitorObject.Log(
        $"ApiManager: version mismatch for {modId}, requested={minimumVersion}, got={modInfo.Manifest.Version}",
        LogLevel.Warn
      );
      return null;
    }

    var api = helper.ModRegistry.GetApi<T>(modId);
    if (api is null)
    {
      if (warnIfNotPresent)
        ModEntry.MonitorObject.Log($"ApiManager: no API found for {modId}", LogLevel.Warn);
      return null;
    }

    RegisteredApis[modId] = api;
    return api;
  }

  public static bool GetApi<T>(string modId, [NotNullWhen(true)] out T? apiInstance)
    where T : class
  {
    apiInstance = null;
    if (!RegisteredApis.TryGetValue(modId, out object? api))
    {
      return false;
    }

    if (api is T apiVal)
    {
      apiInstance = apiVal;
      return true;
    }

    ModEntry.MonitorObject.Log($"ApiManager: type mismatch for {modId}", LogLevel.Warn);
    return false;
  }
}
