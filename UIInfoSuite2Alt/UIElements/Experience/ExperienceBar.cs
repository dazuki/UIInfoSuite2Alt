using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Enums;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Tools;
using UIInfoSuite2Alt.Compatibility;
using UIInfoSuite2Alt.Compatibility.Helpers;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements.Experience;

public partial class ExperienceBar : IDisposable
{
  #region Properties
  private readonly PerScreen<Item> _previousItem = new();
  private readonly PerScreen<int[]> _currentExperience = new(() => new int[5]);
  private readonly PerScreen<int> _currentSkillLevel = new(() => 0);
  private readonly PerScreen<int> _experienceRequiredToLevel = new(() => -1);
  private readonly PerScreen<int> _experienceFromPreviousLevels = new(() => -1);
  private readonly PerScreen<int> _experienceEarnedThisLevel = new(() => -1);

  private readonly PerScreen<DisplayedExperienceBar> _displayedExperienceBar = new(() =>
    new DisplayedExperienceBar()
  );

  private readonly PerScreen<DisplayedLevelUpMessage> _displayedLevelUpMessage = new(() =>
    new DisplayedLevelUpMessage()
  );

  private readonly PerScreen<List<DisplayedExperienceValue>> _displayedExperienceValues = new(() =>
    []
  );

  private const int LevelUpVisibleTicks = 120;
  private readonly PerScreen<int> _levelUpVisibleTimer = new();
  private const int ExperienceBarVisibleTicks = 480;
  private readonly PerScreen<int> _experienceBarVisibleTimer = new();

  private static readonly Dictionary<SkillType, Rectangle> SkillIconRectangles = new()
  {
    [SkillType.Farming] = new Rectangle(10, 428, 10, 10),
    [SkillType.Fishing] = new Rectangle(20, 428, 10, 10),
    [SkillType.Foraging] = new Rectangle(60, 428, 10, 10),
    [SkillType.Mining] = new Rectangle(30, 428, 10, 10),
    [SkillType.Combat] = new Rectangle(120, 428, 10, 10),
    [SkillType.Luck] = new Rectangle(50, 428, 10, 10),
  };

  private static readonly Dictionary<SkillType, Color> ExperienceFillColor = new()
  {
    [SkillType.Farming] = new Color(255, 251, 35, 0.38f),
    [SkillType.Fishing] = new Color(17, 84, 252, 0.63f),
    [SkillType.Foraging] = new Color(0, 234, 0, 0.63f),
    [SkillType.Mining] = new Color(145, 104, 63, 0.63f),
    [SkillType.Combat] = new Color(204, 0, 3, 0.63f),
    [SkillType.Luck] = new Color(232, 223, 42, 0.63f),
  };

  private readonly PerScreen<int> _previousMasteryExperience = new();
  private readonly PerScreen<bool> _isMasteryActive = new();

  private static readonly Color MasteryFillColor = new(60 / 255f, 180 / 255f, 80 / 255f, 0.63f);
  private static readonly Rectangle MasteryIconRectangle = new(457, 298, 11, 11);

  private readonly PerScreen<Rectangle> _experienceIconRectangle = new(() =>
    SkillIconRectangles[SkillType.Farming]
  );

  private readonly PerScreen<Rectangle> _levelUpIconRectangle = new(() =>
    SkillIconRectangles[SkillType.Farming]
  );
  private readonly PerScreen<Color> _experienceFillColor = new(() =>
    ExperienceFillColor[SkillType.Farming]
  );

  private bool ExperienceBarFadeoutEnabled { get; set; } = true;
  private bool ExperienceGainTextEnabled { get; set; } = true;
  private bool LevelUpAnimationEnabled { get; set; } = true;
  private bool ExperienceBarEnabled { get; set; } = true;

  // SpaceCore custom skill state
  private ISpaceCoreApi? _spaceCoreApi;
  private readonly PerScreen<Dictionary<string, int>> _currentCustomExperience = new(() => []);
  private readonly PerScreen<Dictionary<string, int>> _currentCustomLevels = new(() => []);
  private readonly PerScreen<string?> _activeCustomSkillId = new();
  private readonly PerScreen<Texture2D?> _customSkillIconTexture = new();

  // Stacked secondary bars for concurrent XP gains (e.g., Combat + custom skill from one kill)
  private readonly PerScreen<List<ExperienceBarState>> _secondaryBars = new(() => []);
  private const int BarStackOffset = 66;

  // Tracks which skill the primary bar is showing, to reset combo on skill change
  // Vanilla skills: "0"-"5", custom skills: their string ID
  private readonly PerScreen<string?> _primaryBarSkillId = new();

  // Accumulated XP "combo counter" shown on the bar while visible
  private readonly PerScreen<int> _accumulatedExperience = new();
  private readonly PerScreen<int> _comboTimer = new();
  private readonly PerScreen<int> _comboShakeTicks = new();
  private const int ComboVisibleTicks = 480; // 8 seconds total (6s solid + 2s fade)
  private const int ComboFadeTicks = 120; // last 2 seconds = fade

  // Static bar visibility for HudMessagePatch to offset vanilla notifications
  private static readonly PerScreen<int> _visibleBarCount = new();
  private static readonly PerScreen<bool> _primaryBarVisible = new();

  private const int AndroidNotificationBaseOffset = 100;

  private readonly IModHelper _helper;
  private readonly IVanillaPlusProfessions? _vppApi;
  #endregion Properties

  #region Lifecycle
  public ExperienceBar(IModHelper helper)
  {
    _helper = helper;

    ApiManager.GetApi(ModCompat.SpaceCore, out _spaceCoreApi);

    if (_helper.ModRegistry.IsLoaded(ModCompat.VanillaPlusProfessions))
    {
      _vppApi = _helper.ModRegistry.GetApi<IVanillaPlusProfessions>(
        ModCompat.VanillaPlusProfessions
      );
    }
  }

  public void ToggleOption(
    bool experienceBarEnabled,
    bool experienceBarFadeoutEnabled,
    bool experienceGainTextEnabled,
    bool levelUpAnimationEnabled
  )
  {
    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.Player.Warped -= OnWarped;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked_HandleTimers;
    _helper.Events.GameLoop.SaveLoaded -= OnSaveLoaded;

    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked_UpdateExperience;
    _helper.Events.Player.LevelChanged -= OnLevelChanged;

    _visibleBarCount.ResetAllScreens();
    _primaryBarVisible.ResetAllScreens();

    ExperienceBarEnabled = experienceBarEnabled;
    ExperienceBarFadeoutEnabled = experienceBarFadeoutEnabled;
    ExperienceGainTextEnabled = experienceGainTextEnabled;
    LevelUpAnimationEnabled = levelUpAnimationEnabled;

    if (
      ExperienceBarEnabled
      || ExperienceBarFadeoutEnabled
      || ExperienceGainTextEnabled
      || LevelUpAnimationEnabled
    )
    {
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.Player.Warped += OnWarped;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked_HandleTimers;
      _helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
    }

    if (ExperienceBarEnabled || ExperienceGainTextEnabled)
    {
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked_UpdateExperience;
    }

    if (LevelUpAnimationEnabled)
    {
      _helper.Events.Player.LevelChanged += OnLevelChanged;
    }
  }

  public void ToggleShowExperienceBar(bool experienceBarEnabled)
  {
    ToggleOption(
      experienceBarEnabled,
      ExperienceBarFadeoutEnabled,
      ExperienceGainTextEnabled,
      LevelUpAnimationEnabled
    );
  }

  public void ToggleExperienceBarFade(bool experienceBarFadeoutEnabled)
  {
    ToggleOption(
      ExperienceBarEnabled,
      experienceBarFadeoutEnabled,
      ExperienceGainTextEnabled,
      LevelUpAnimationEnabled
    );
  }

  public void ToggleShowExperienceGain(bool experienceGainTextEnabled)
  {
    InitializeExperiencePoints();
    ToggleOption(
      ExperienceBarEnabled,
      ExperienceBarFadeoutEnabled,
      experienceGainTextEnabled,
      LevelUpAnimationEnabled
    );
  }

  public void ToggleLevelUpAnimation(bool levelUpAnimationEnabled)
  {
    ToggleOption(
      ExperienceBarEnabled,
      ExperienceBarFadeoutEnabled,
      ExperienceGainTextEnabled,
      levelUpAnimationEnabled
    );
  }

  public void Dispose()
  {
    ToggleOption(false, false, false, false);
  }
  #endregion Lifecycle

  #region Event subscriptions
  private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
  {
    InitializeExperiencePoints();

    _displayedExperienceValues.Value.Clear();
  }

  private void OnLevelChanged(object? sender, LevelChangedEventArgs e)
  {
    if (LevelUpAnimationEnabled && e.IsLocalPlayer)
    {
      _levelUpVisibleTimer.Value = LevelUpVisibleTicks;
      _levelUpIconRectangle.Value = SkillIconRectangles[e.Skill];

      _experienceBarVisibleTimer.Value = ExperienceBarVisibleTicks;

      SoundHelper.Play(Sounds.LevelUp);
    }
  }

  private void OnWarped(object? sender, WarpedEventArgs e)
  {
    if (e.IsLocalPlayer)
    {
      _displayedExperienceValues.Value.Clear();
      _secondaryBars.Value.Clear();
    }
  }

  private void OnUpdateTicked_UpdateExperience(object? sender, UpdateTickedEventArgs e)
  {
    if (!e.IsMultipleOf(15)) // quarter second
    {
      return;
    }

    bool skillChanged = TryGetCurrentLevelIndexFromSkillChange(out int currentLevelIndex);
    bool itemChanged = Game1.player.CurrentItem != _previousItem.Value;

    // WoL may redirect XP to mastery without updating experiencePoints[]
    int currentMasteryXp = (int)Game1.stats.Get("MasteryExp");
    bool masteryXpChanged = !skillChanged && currentMasteryXp != _previousMasteryExperience.Value;

    if (itemChanged)
    {
      currentLevelIndex = GetCurrentLevelIndexFromItemChange(Game1.player.CurrentItem);
      _previousItem.Value = Game1.player.CurrentItem;
    }

    if (masteryXpChanged && !itemChanged)
    {
      // Use the skill index from the current item since experiencePoints didn't change
      currentLevelIndex = GetCurrentLevelIndexFromItemChange(Game1.player.CurrentItem);
    }

    if (skillChanged || itemChanged || masteryXpChanged)
    {
      UpdateExperience(currentLevelIndex, skillChanged || masteryXpChanged);
    }

    // Check SpaceCore custom skills - may show as secondary bar if vanilla also changed
    List<string> changedCustomSkills = GetChangedCustomSkills();
    if (changedCustomSkills.Count > 0)
    {
      if (skillChanged)
      {
        // Vanilla bar is primary - show custom skills as stacked secondary bars
        for (int i = 0; i < changedCustomSkills.Count; ++i)
        {
          AddOrUpdateSecondaryBar(changedCustomSkills[i], (i + 1) * 24);
        }
      }
      else
      {
        // First custom skill is primary bar (no delay)
        UpdateCustomSkillExperience(changedCustomSkills[0], true);

        // Additional custom skills as secondary bars with cascading delay
        for (int i = 1; i < changedCustomSkills.Count; ++i)
        {
          AddOrUpdateSecondaryBar(changedCustomSkills[i], i * 24);
        }
      }
    }
  }

  public void OnUpdateTicked_HandleTimers(object? sender, UpdateTickedEventArgs e)
  {
    if (_levelUpVisibleTimer.Value > 0)
    {
      _levelUpVisibleTimer.Value--;
    }

    if (_experienceBarVisibleTimer.Value > 0)
    {
      _experienceBarVisibleTimer.Value--;
    }

    if (_comboTimer.Value > 0)
    {
      _comboTimer.Value--;
    }

    if (_comboShakeTicks.Value > 0)
    {
      _comboShakeTicks.Value--;
    }

    // Secondary bar timers
    for (int i = _secondaryBars.Value.Count - 1; i >= 0; --i)
    {
      if (_secondaryBars.Value[i].VisibleTimer > 0)
      {
        _secondaryBars.Value[i].VisibleTimer--;
      }

      if (_secondaryBars.Value[i].ComboTimer > 0)
      {
        _secondaryBars.Value[i].ComboTimer--;
      }

      if (_secondaryBars.Value[i].ComboShakeTicks > 0)
      {
        _secondaryBars.Value[i].ComboShakeTicks--;
      }

      if (_secondaryBars.Value[i].VisibleTimer <= 0 && ExperienceBarFadeoutEnabled)
      {
        _secondaryBars.Value.RemoveAt(i);
      }
    }
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (!UIElementUtils.IsRenderingNormally())
    {
      _visibleBarCount.Value = 0;
      _primaryBarVisible.Value = false;
      return;
    }

    // Count visible secondary (stacked) bars for HudMessagePatch notification offset
    int barCount = 0;
    foreach (ExperienceBarState bar in _secondaryBars.Value)
    {
      if (bar.VisibleTimer > 0 || !ExperienceBarFadeoutEnabled)
      {
        barCount++;
      }
    }
    _visibleBarCount.Value = barCount;

    // Level up text
    if (LevelUpAnimationEnabled && _levelUpVisibleTimer.Value != 0)
    {
      _displayedLevelUpMessage.Value.Draw(
        _levelUpIconRectangle.Value,
        I18n.LevelUp(),
        _customSkillIconTexture.Value
      );
    }

    // Experience values
    for (int i = _displayedExperienceValues.Value.Count - 1; i >= 0; --i)
    {
      if (_displayedExperienceValues.Value[i].IsInvisible)
      {
        _displayedExperienceValues.Value.RemoveAt(i);
      }
      else
      {
        if (ExperienceGainTextEnabled)
        {
          _displayedExperienceValues.Value[i].Draw();
        }
      }
    }

    // Primary experience bar
    _primaryBarVisible.Value =
      ExperienceBarEnabled
      && (_experienceBarVisibleTimer.Value != 0 || !ExperienceBarFadeoutEnabled)
      && _experienceRequiredToLevel.Value > 0;
    if (_primaryBarVisible.Value)
    {
      Texture2D? barIconTexture = _isMasteryActive.Value
        ? Game1.mouseCursors_1_6
        : _customSkillIconTexture.Value;

      float comboAlpha =
        _comboTimer.Value > ComboFadeTicks ? 1f : _comboTimer.Value / (float)ComboFadeTicks;

      _displayedExperienceBar.Value.Draw(
        _experienceFillColor.Value,
        _experienceIconRectangle.Value,
        _experienceEarnedThisLevel.Value,
        _experienceRequiredToLevel.Value - _experienceFromPreviousLevels.Value,
        _currentSkillLevel.Value,
        barIconTexture,
        _isMasteryActive.Value,
        _isMasteryActive.Value ? (29f / 11f) : 2.9f,
        0,
        _accumulatedExperience.Value,
        comboAlpha,
        _comboShakeTicks.Value
      );
    }

    // Secondary bars stacked above primary
    for (int i = 0; i < _secondaryBars.Value.Count; ++i)
    {
      ExperienceBarState bar = _secondaryBars.Value[i];
      if (bar.VisibleTimer > 0 || !ExperienceBarFadeoutEnabled)
      {
        float barComboAlpha =
          bar.ComboTimer > ComboFadeTicks ? 1f : bar.ComboTimer / (float)ComboFadeTicks;

        _displayedExperienceBar.Value.Draw(
          bar.FillColor,
          bar.IconRectangle,
          bar.EarnedThisLevel,
          bar.DifferenceBetweenLevels,
          bar.SkillLevel,
          bar.IconTexture,
          bar.IsMastery,
          bar.IconScale,
          (i + 1) * BarStackOffset,
          bar.AccumulatedExperience,
          barComboAlpha,
          bar.ComboShakeTicks
        );
      }
    }
  }

  #endregion Event subscriptions

  #region Logic
  private void InitializeExperiencePoints()
  {
    for (var i = 0; i < _currentExperience.Value.Length; ++i)
    {
      _currentExperience.Value[i] = Game1.player.experiencePoints[i];
    }

    _previousMasteryExperience.Value = (int)Game1.stats.Get("MasteryExp");

    InitializeCustomSkills();
  }

  private void InitializeCustomSkills()
  {
    _currentCustomExperience.Value.Clear();
    _currentCustomLevels.Value.Clear();
    _activeCustomSkillId.Value = null;

    if (_spaceCoreApi == null)
    {
      return;
    }

    foreach (string skillId in _spaceCoreApi.GetCustomSkills())
    {
      _currentCustomExperience.Value[skillId] = _spaceCoreApi.GetExperienceForCustomSkill(
        Game1.player,
        skillId
      );
      _currentCustomLevels.Value[skillId] = _spaceCoreApi.GetLevelForCustomSkill(
        Game1.player,
        skillId
      );
    }
  }

  private bool TryGetCurrentLevelIndexFromSkillChange(out int currentLevelIndex)
  {
    currentLevelIndex = -1;

    for (var i = 0; i < _currentExperience.Value.Length; ++i)
    {
      if (_currentExperience.Value[i] != Game1.player.experiencePoints[i])
      {
        currentLevelIndex = i;
        break;
      }
    }

    return currentLevelIndex != -1;
  }

  private static int GetCurrentLevelIndexFromItemChange(Item currentItem)
  {
    return currentItem switch
    {
      FishingRod => (int)SkillType.Fishing,
      Pickaxe => (int)SkillType.Mining,
      MeleeWeapon weapon when weapon.Name != "Scythe" => (int)SkillType.Combat,
      _ when Game1.currentLocation is Farm or FarmHouse && currentItem is not Axe => (int)
        SkillType.Farming,
      _ => (int)SkillType.Foraging,
    };
  }

  private void UpdateExperience(int currentLevelIndex, bool displayExperience)
  {
    _activeCustomSkillId.Value = null;
    _customSkillIconTexture.Value = null;

    if (_experienceBarVisibleTimer.Value == 0)
    {
      _accumulatedExperience.Value = 0;
    }
    _experienceBarVisibleTimer.Value = ExperienceBarVisibleTicks;

    if (_comboTimer.Value == 0)
    {
      _accumulatedExperience.Value = 0;
    }

    _experienceIconRectangle.Value = SkillIconRectangles[(SkillType)currentLevelIndex];
    _experienceFillColor.Value = ExperienceFillColor[(SkillType)currentLevelIndex];
    _currentSkillLevel.Value = Game1.player.GetUnmodifiedSkillLevel(currentLevelIndex);

    _experienceRequiredToLevel.Value = GetExperienceRequiredToLevel(_currentSkillLevel.Value);

    // WoL prestige: only active at level 10 if skill is mastered
    if (
      _currentSkillLevel.Value == 10
      && WalkOfLifeHelper.PrestigeEnabled
      && _experienceRequiredToLevel.Value > 0
      && Game1.player.stats.Get($"mastery_{currentLevelIndex}") == 0
    )
    {
      _experienceRequiredToLevel.Value = -1;
    }

    _experienceFromPreviousLevels.Value = GetExperienceRequiredToLevel(
      _currentSkillLevel.Value - 1
    );
    _experienceEarnedThisLevel.Value =
      Game1.player.experiencePoints[currentLevelIndex] - _experienceFromPreviousLevels.Value;

    // Mastery experience bar when skill is maxed and all skills meet the required level
    _isMasteryActive.Value = false;
    int masteryMinLevel = _vppApi?.MasteryCaveChanges ?? 10;
    if (
      _experienceRequiredToLevel.Value <= 0
      && _currentSkillLevel.Value >= masteryMinLevel
      && IsMasteryUnlocked()
    )
    {
      int currentMasteryLevel = MasteryTrackerMenu.getCurrentMasteryLevel();
      if (currentMasteryLevel < 5)
      {
        _isMasteryActive.Value = true;
        _experienceIconRectangle.Value = MasteryIconRectangle;
        _experienceFillColor.Value = MasteryFillColor;
        _experienceFromPreviousLevels.Value = MasteryTrackerMenu.getMasteryExpNeededForLevel(
          currentMasteryLevel
        );
        _experienceRequiredToLevel.Value = MasteryTrackerMenu.getMasteryExpNeededForLevel(
          currentMasteryLevel + 1
        );
        _experienceEarnedThisLevel.Value =
          (int)Game1.stats.Get("MasteryExp") - _experienceFromPreviousLevels.Value;
        _currentSkillLevel.Value = currentMasteryLevel;
      }
    }

    // Reset combo when the displayed skill changes.
    // Mastery uses a shared identity so switching between maxed tools preserves the combo.
    // Custom skill IDs never collide with vanilla indices, so custom->vanilla always resets.
    string skillId = _isMasteryActive.Value ? "mastery" : currentLevelIndex.ToString();
    if (_primaryBarSkillId.Value != skillId)
    {
      _accumulatedExperience.Value = 0;
      _comboTimer.Value = 0;
      _primaryBarSkillId.Value = skillId;
    }

    if (displayExperience)
    {
      if (ExperienceGainTextEnabled && _experienceRequiredToLevel.Value > 0)
      {
        int currentExperienceToUse;
        int previousExperienceToUse;

        if (_isMasteryActive.Value)
        {
          currentExperienceToUse = (int)Game1.stats.Get("MasteryExp");
          previousExperienceToUse = _previousMasteryExperience.Value;
        }
        else
        {
          currentExperienceToUse = Game1.player.experiencePoints[currentLevelIndex];
          previousExperienceToUse = _currentExperience.Value[currentLevelIndex];
        }

        int experienceGain = currentExperienceToUse - previousExperienceToUse;

        if (experienceGain > 0)
        {
          if (_accumulatedExperience.Value > 0)
          {
            _comboShakeTicks.Value = 15;
          }
          _accumulatedExperience.Value += experienceGain;
          _comboTimer.Value = ComboVisibleTicks;
          _displayedExperienceValues.Value.Add(
            new DisplayedExperienceValue(
              experienceGain,
              Game1.player.getLocalPosition(Game1.viewport)
            )
          );
        }
      }

      _currentExperience.Value[currentLevelIndex] = Game1.player.experiencePoints[
        currentLevelIndex
      ];

      if (_isMasteryActive.Value)
      {
        _previousMasteryExperience.Value = (int)Game1.stats.Get("MasteryExp");
      }
    }
  }

  private List<string> GetChangedCustomSkills()
  {
    List<string> changed = [];
    if (_spaceCoreApi == null)
    {
      return changed;
    }

    foreach (KeyValuePair<string, int> kvp in _currentCustomExperience.Value)
    {
      int currentXp = _spaceCoreApi.GetExperienceForCustomSkill(Game1.player, kvp.Key);
      if (currentXp != kvp.Value)
      {
        changed.Add(kvp.Key);
      }
    }

    return changed;
  }

  private void UpdateCustomSkillExperience(string skillId, bool displayExperience)
  {
    _activeCustomSkillId.Value = skillId;

    if (_primaryBarSkillId.Value != skillId)
    {
      _accumulatedExperience.Value = 0;
      _comboTimer.Value = 0;
    }
    _primaryBarSkillId.Value = skillId;

    if (_experienceBarVisibleTimer.Value == 0)
    {
      _accumulatedExperience.Value = 0;
    }

    if (_comboTimer.Value == 0)
    {
      _accumulatedExperience.Value = 0;
    }
    _experienceBarVisibleTimer.Value = ExperienceBarVisibleTicks;

    CachedCustomSkillInfo info = SpaceCoreHelper.GetSkillInfo(_spaceCoreApi!, skillId);

    int currentLevel = _spaceCoreApi!.GetLevelForCustomSkill(Game1.player, skillId);
    int currentXp = _spaceCoreApi.GetExperienceForCustomSkill(Game1.player, skillId);

    // Level-up detection
    if (
      LevelUpAnimationEnabled
      && _currentCustomLevels.Value.TryGetValue(skillId, out int prevLevel)
      && currentLevel > prevLevel
    )
    {
      _levelUpVisibleTimer.Value = LevelUpVisibleTicks;
      _customSkillIconTexture.Value = info.Icon;
      _levelUpIconRectangle.Value = new Rectangle(0, 0, info.Icon.Width, info.Icon.Height);
      _experienceBarVisibleTimer.Value = ExperienceBarVisibleTicks;
      SoundHelper.Play(Sounds.LevelUp);
    }

    // Set bar state
    _customSkillIconTexture.Value = info.Icon;
    _experienceIconRectangle.Value = new Rectangle(0, 0, info.Icon.Width, info.Icon.Height);
    _experienceFillColor.Value = info.BarColor;
    _currentSkillLevel.Value = currentLevel;
    _isMasteryActive.Value = false;

    int xpForCurrentLevel = SpaceCoreHelper.GetExperienceRequiredForLevel(info, currentLevel - 1);
    int xpForNextLevel = SpaceCoreHelper.GetExperienceRequiredForLevel(info, currentLevel);

    if (xpForNextLevel <= 0)
    {
      // Skill is maxed
      _experienceRequiredToLevel.Value = -1;
    }
    else
    {
      _experienceRequiredToLevel.Value = xpForNextLevel;
      _experienceFromPreviousLevels.Value = xpForCurrentLevel;
      _experienceEarnedThisLevel.Value = currentXp - xpForCurrentLevel;
    }

    // XP gain text
    if (displayExperience && ExperienceGainTextEnabled && _experienceRequiredToLevel.Value > 0)
    {
      int previousXp = _currentCustomExperience.Value.GetValueOrDefault(skillId, 0);
      int gain = currentXp - previousXp;

      if (gain > 0)
      {
        if (_accumulatedExperience.Value > 0)
        {
          _comboShakeTicks.Value = 15;
        }
        _accumulatedExperience.Value += gain;
        _comboTimer.Value = ComboVisibleTicks;
        _displayedExperienceValues.Value.Add(
          new DisplayedExperienceValue(
            gain,
            Game1.player.getLocalPosition(Game1.viewport),
            info.BarColor
          )
        );
      }
    }

    // Update cached state
    _currentCustomExperience.Value[skillId] = currentXp;
    _currentCustomLevels.Value[skillId] = currentLevel;
  }

  private void AddOrUpdateSecondaryBar(string skillId, int delayTicks = 24)
  {
    CachedCustomSkillInfo info = SpaceCoreHelper.GetSkillInfo(_spaceCoreApi!, skillId);

    int currentLevel = _spaceCoreApi!.GetLevelForCustomSkill(Game1.player, skillId);
    int currentXp = _spaceCoreApi.GetExperienceForCustomSkill(Game1.player, skillId);

    int xpForCurrentLevel = SpaceCoreHelper.GetExperienceRequiredForLevel(info, currentLevel - 1);
    int xpForNextLevel = SpaceCoreHelper.GetExperienceRequiredForLevel(info, currentLevel);

    if (xpForNextLevel <= 0)
    {
      // Skill is maxed, don't show a bar
      _currentCustomExperience.Value[skillId] = currentXp;
      _currentCustomLevels.Value[skillId] = currentLevel;
      return;
    }

    // Level-up detection
    if (
      LevelUpAnimationEnabled
      && _currentCustomLevels.Value.TryGetValue(skillId, out int prevLevel)
      && currentLevel > prevLevel
    )
    {
      _levelUpVisibleTimer.Value = LevelUpVisibleTicks;
      _customSkillIconTexture.Value = info.Icon;
      _levelUpIconRectangle.Value = new Rectangle(0, 0, info.Icon.Width, info.Icon.Height);
      SoundHelper.Play(Sounds.LevelUp);
    }

    // XP gain text
    if (ExperienceGainTextEnabled)
    {
      int previousXp = _currentCustomExperience.Value.GetValueOrDefault(skillId, 0);
      int gain = currentXp - previousXp;

      if (gain > 0)
      {
        _displayedExperienceValues.Value.Add(
          new DisplayedExperienceValue(
            gain,
            Game1.player.getLocalPosition(Game1.viewport),
            info.BarColor,
            delayTicks
          )
        );
      }
    }

    // Find existing secondary bar for this skill or create new
    ExperienceBarState? existing = null;
    foreach (ExperienceBarState bar in _secondaryBars.Value)
    {
      if (bar.IconTexture == info.Icon)
      {
        existing = bar;
        break;
      }
    }

    if (existing == null)
    {
      existing = new ExperienceBarState();
      _secondaryBars.Value.Add(existing);
    }

    int xpGain = currentXp - _currentCustomExperience.Value.GetValueOrDefault(skillId, 0);
    if (xpGain > 0)
    {
      if (existing.ComboTimer == 0)
      {
        existing.AccumulatedExperience = 0;
      }
      else if (existing.AccumulatedExperience > 0)
      {
        existing.ComboShakeTicks = 15;
      }
      existing.AccumulatedExperience += xpGain;
      existing.ComboTimer = ComboVisibleTicks;
    }

    existing.FillColor = info.BarColor;
    existing.IconRectangle = new Rectangle(0, 0, info.Icon.Width, info.Icon.Height);
    existing.IconTexture = info.Icon;
    existing.EarnedThisLevel = currentXp - xpForCurrentLevel;
    existing.DifferenceBetweenLevels = xpForNextLevel - xpForCurrentLevel;
    existing.SkillLevel = currentLevel;
    existing.IsMastery = false;
    existing.IconScale = 2.9f;
    existing.VisibleTimer = ExperienceBarVisibleTicks;

    // Update cached state
    _currentCustomExperience.Value[skillId] = currentXp;
    _currentCustomLevels.Value[skillId] = currentLevel;
  }

  private bool IsMasteryUnlocked()
  {
    int maxLevel = _vppApi?.MasteryCaveChanges ?? 10;
    return Game1.player.farmingLevel.Value >= maxLevel
      && Game1.player.fishingLevel.Value >= maxLevel
      && Game1.player.foragingLevel.Value >= maxLevel
      && Game1.player.miningLevel.Value >= maxLevel
      && Game1.player.combatLevel.Value >= maxLevel;
  }

  /// <summary>Returns pixel offset for vanilla HUD notifications when experience bars are visible.</summary>
  internal static int GetNotificationOffset()
  {
    return _visibleBarCount.Value * BarStackOffset;
  }

  /// <summary>Android: lift for vanilla HUD notifications so they clear the whole bar stack.</summary>
  internal static int GetAndroidNotificationOffset()
  {
    int stacked = _visibleBarCount.Value;
    if (!_primaryBarVisible.Value && stacked == 0)
    {
      return 0;
    }

    return AndroidNotificationBaseOffset + stacked * BarStackOffset;
  }

  private int GetExperienceRequiredToLevel(int currentLevel)
  {
    return currentLevel switch
    {
      0 => 100,
      1 => 380,
      2 => 770,
      3 => 1300,
      4 => 2150,
      5 => 3300,
      6 => 4800,
      7 => 6900,
      8 => 10000,
      9 => 15000,
      _ => GetExtendedExperienceRequiredToLevel(currentLevel),
    };
  }

  /// <summary>Cumulative XP for extended levels (10+). Checks WoL then VPP.</summary>
  private int GetExtendedExperienceRequiredToLevel(int currentLevel)
  {
    // Walk of Life prestige levels (11-20)
    int wolXp = WalkOfLifeHelper.GetExperienceRequiredForPrestigeLevel(currentLevel);
    if (wolXp > 0)
    {
      return wolXp;
    }

    // VPP extended levels
    if (_vppApi != null)
    {
      int[] levelXp = _vppApi.LevelExperiences;
      int index = currentLevel - 10; // level 10 -> index 0, level 19 -> index 9
      if (index >= 0 && index < levelXp.Length)
      {
        return levelXp[index];
      }
    }

    return -1;
  }
  #endregion Logic

  #region Inner types
  private class ExperienceBarState
  {
    public Color FillColor;
    public Rectangle IconRectangle;
    public Texture2D? IconTexture;
    public int EarnedThisLevel;
    public int DifferenceBetweenLevels;
    public int SkillLevel;
    public bool IsMastery;
    public float IconScale;
    public int VisibleTimer;
    public int AccumulatedExperience;
    public int ComboTimer;
    public int ComboShakeTicks;
  }
  #endregion
}
