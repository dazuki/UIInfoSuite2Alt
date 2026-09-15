using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.UIElements.Experience;

namespace UIInfoSuite2Alt.Patches;

internal static class HudMessagePatch
{
  public static void Initialize(Harmony harmony, bool spaceCoreLoaded)
  {
    ModEntry.MonitorObject.Log(
      $"HudMessagePatch: initialized, spaceCoreLoaded={spaceCoreLoaded}, android={AndroidHud.IsAndroid}",
      LogLevel.Trace
    );

    MethodInfo original = AccessTools.Method(typeof(HUDMessage), nameof(HUDMessage.draw));

    if (AndroidHud.IsAndroid)
    {
      harmony.Patch(
        original: original,
        transpiler: new HarmonyMethod(typeof(HudMessagePatch), nameof(TranspileAndroid))
      );
      return;
    }

    if (!spaceCoreLoaded)
    {
      return;
    }

    harmony.Patch(
      original: original,
      prefix: new HarmonyMethod(typeof(HudMessagePatch), nameof(BeforeDraw))
    );
  }

  // Game loops hudMessages in reverse (Count-1 down to 0), so the first
  // draw call each frame has heightUsed == 0. Add our offset there to shift
  // the starting point for all notifications above the stacked experience bars.
  private static void BeforeDraw(ref int heightUsed)
  {
    if (heightUsed == 0)
    {
      heightUsed += ExperienceBar.GetNotificationOffset() + 2;
    }
  }

  // Android ignores heightUsed and places each message from uiViewport.Height, so lift that baseline instead
  private static IEnumerable<CodeInstruction> TranspileAndroid(
    IEnumerable<CodeInstruction> instructions
  )
  {
    List<CodeInstruction> code = new(instructions);
    MethodInfo offset = AccessTools.Method(
      typeof(ExperienceBar),
      nameof(ExperienceBar.GetAndroidNotificationOffset)
    );
    int patched = 0;

    for (int i = 1; i < code.Count; i++)
    {
      if (
        code[i].operand is MethodInfo { Name: "get_Height" }
        && code[i - 1].opcode == OpCodes.Ldsflda
        && code[i - 1].operand is FieldInfo { Name: nameof(Game1.uiViewport) } field
        && field.DeclaringType == typeof(Game1)
      )
      {
        code.InsertRange(
          i + 1,
          new[] { new CodeInstruction(OpCodes.Call, offset), new CodeInstruction(OpCodes.Sub) }
        );
        patched++;
      }
    }

    ModEntry.MonitorObject.Log(
      $"HudMessagePatch: Android offset injected at {patched} uiViewport.Height reads",
      patched == 0 ? LogLevel.Warn : LogLevel.Trace
    );
    return code;
  }
}
