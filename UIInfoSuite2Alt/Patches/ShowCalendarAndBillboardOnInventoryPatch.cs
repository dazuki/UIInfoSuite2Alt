using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley.Menus;
using UIInfoSuite2Alt.UIElements;

namespace UIInfoSuite2Alt.Patches;

/// <summary>
/// Injects icon draws before the held-item/tooltip block in InventoryPage.draw so vanilla layering puts tooltips on top.
/// Android has no such block - draw ends at inventory.draw(b) - so a postfix is used there instead.
/// </summary>
internal static class ShowCalendarAndBillboardOnInventoryPatch
{
  public static void Initialize(Harmony harmony)
  {
    MethodInfo? original = AccessTools.Method(
      typeof(InventoryPage),
      nameof(InventoryPage.draw),
      [typeof(SpriteBatch)]
    );
    if (original == null)
    {
      ModEntry.MonitorObject.Log(
        "ShowCalendarAndBillboardOnInventoryPatch: could not find InventoryPage.draw(SpriteBatch)",
        LogLevel.Warn
      );
      return;
    }

    if (AccessTools.Method(typeof(InventoryPage), "checkHeldItem") != null)
    {
      harmony.Patch(
        original: original,
        transpiler: new HarmonyMethod(
          typeof(ShowCalendarAndBillboardOnInventoryPatch),
          nameof(Transpile)
        )
      );

      ModEntry.MonitorObject.Log(
        "ShowCalendarAndBillboardOnInventoryPatch: transpiler attached to InventoryPage.draw",
        LogLevel.Trace
      );
      return;
    }

    harmony.Patch(
      original: original,
      postfix: new HarmonyMethod(typeof(ShowCalendarAndBillboardOnInventoryPatch), nameof(Postfix))
    );

    ModEntry.MonitorObject.Log(
      "ShowCalendarAndBillboardOnInventoryPatch: InventoryPage.checkHeldItem not present, attached postfix to InventoryPage.draw instead",
      LogLevel.Trace
    );
  }

  private static void Postfix(SpriteBatch b)
  {
    ShowCalendarAndBillboardOnGameMenuButton.DrawIconsFromInventoryPagePatch(b);
  }

  private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
  {
    List<CodeInstruction> code = new(instructions);
    MethodInfo checkHeldItem = AccessTools.Method(typeof(InventoryPage), "checkHeldItem")!;
    MethodInfo hook = AccessTools.Method(
      typeof(ShowCalendarAndBillboardOnGameMenuButton),
      nameof(ShowCalendarAndBillboardOnGameMenuButton.DrawIconsFromInventoryPagePatch)
    )!;

    // Anchor on the `if (checkHeldItem())` guard before the held-item draw at the end of draw().
    for (int i = 1; i < code.Count; i++)
    {
      CodeInstruction instr = code[i];
      if (
        (instr.opcode == OpCodes.Call || instr.opcode == OpCodes.Callvirt)
        && instr.operand is MethodInfo mi
        && mi == checkHeldItem
      )
      {
        int insertAt = i - 1;
        List<CodeInstruction> hookInstructions =
        [
          new CodeInstruction(OpCodes.Ldarg_1),
          new CodeInstruction(OpCodes.Call, hook),
        ];

        // Move labels from the original ldarg.0 onto our first instruction so upstream branches still land correctly.
        hookInstructions[0].labels.AddRange(code[insertAt].labels);
        code[insertAt].labels.Clear();

        code.InsertRange(insertAt, hookInstructions);
        ModEntry.MonitorObject.Log(
          $"ShowCalendarAndBillboardOnInventoryPatch: hook injected before checkHeldItem at IL offset {insertAt}",
          LogLevel.Trace
        );
        return code;
      }
    }

    ModEntry.MonitorObject.Log(
      "ShowCalendarAndBillboardOnInventoryPatch: could not locate checkHeldItem call - icons will not render inside InventoryPage",
      LogLevel.Warn
    );
    return code;
  }
}
