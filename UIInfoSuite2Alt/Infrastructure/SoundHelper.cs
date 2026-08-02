using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Audio;
using StardewModdingAPI;
using StardewValley;

namespace UIInfoSuite2Alt.Infrastructure;

public enum Sounds
{
  LevelUp,
  BuffExpired,
}

public class SoundHelper
{
  private static readonly Lazy<SoundHelper> LazyInstance = new(() => new SoundHelper());
  private bool _initialized;
  private readonly HashSet<Sounds> _registeredSounds = [];

  private string _modId = "InfoSuite";

  protected SoundHelper() { }

  public static SoundHelper Instance => LazyInstance.Value;

  public void Initialize(IModHelper helper)
  {
    if (_initialized)
    {
      throw new InvalidOperationException("Cannot re-initialize sound helper");
    }

    _modId = helper.ModContent.ModID;

    if (Constants.TargetPlatform != GamePlatform.Android)
    {
      RegisterSound(helper, Sounds.LevelUp, "level_up.ogg");
      RegisterSound(helper, Sounds.BuffExpired, "buff_expire.ogg");
    }

    _initialized = true;
  }

  private string GetQualifiedSoundName(Sounds sound)
  {
    return $"{_modId}.sounds.{sound.ToString()}";
  }

  private void RegisterSound(
    IModHelper helper,
    Sounds sound,
    string fileName,
    string category = "Sound",
    int instanceLimit = -1,
    CueDefinition.LimitBehavior? limitBehavior = null
  )
  {
    string filePath = Path.Combine(helper.DirectoryPath, "assets", fileName);
    SoundEffect? audio = AssetHelper.TryLoadSound(filePath);

    if (audio is null)
    {
      return;
    }

    CueDefinition newCueDefinition = new() { name = GetQualifiedSoundName(sound) };

    if (instanceLimit > 0)
    {
      newCueDefinition.instanceLimit = instanceLimit;
      newCueDefinition.limitBehavior = limitBehavior ?? CueDefinition.LimitBehavior.ReplaceOldest;
    }
    else if (limitBehavior.HasValue)
    {
      newCueDefinition.limitBehavior = limitBehavior.Value;
    }

    newCueDefinition.SetSound(audio, Game1.audioEngine.GetCategoryIndex(category));
    Game1.soundBank.AddCue(newCueDefinition);
    _registeredSounds.Add(sound);
    ModEntry.MonitorObject.Log(
      $"SoundHelper: registered sound, name={newCueDefinition.name}",
      LogLevel.Trace
    );
  }

  public static void Play(Sounds sound)
  {
    if (!Instance._registeredSounds.Contains(sound))
    {
      ModEntry.MonitorObject.LogOnce(
        $"SoundHelper: skipping playback of '{sound}' (asset not loaded)",
        LogLevel.Trace
      );
      return;
    }

    Game1.playSound(Instance.GetQualifiedSoundName(sound));
  }
}
