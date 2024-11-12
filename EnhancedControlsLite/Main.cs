using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using UnityModManagerNet;
using Kingmaker.View;
using System.Reflection.Emit;
using Kingmaker.Controllers.Clicks;
using static ModKit.UI;
using Kingmaker;
using TurnBased.Controllers;
using Kingmaker.Controllers.Clicks.Handlers;
using Kingmaker.Kingdom;
using Kingmaker.Armies.TacticalCombat.Controllers;

namespace EnhancedControlsLite;

public static class Main {
    internal static Harmony HarmonyInstance;
    internal static UnityModManager.ModEntry.ModLogger log;
    internal static Settings settings;
    internal static bool rightClickIsPatched = false;
    internal static bool hasPressedPauseBind = false;
    internal static bool hasPressedFastBind = false;

    public static bool Load(UnityModManager.ModEntry modEntry) {
        log = modEntry.Logger;
        modEntry.OnGUI = OnGUI;
        modEntry.OnSaveGUI = OnSaveGUI;
        modEntry.OnUpdate = OnUpdate;
        settings = Settings.Load<Settings>(modEntry);
        ModKit.Mod.OnLoad(modEntry);
        KeyBindings.OnLoad(modEntry);
        HarmonyInstance = new Harmony(modEntry.Info.Id);
        if (settings.RightClickRotate || settings.MiddleClickAlsoRotate) RightClickPatches.Patch();
        if (settings.EndTurnHotkeyEnabled) EndTurnPatches.Patch();
        KeyBindings.RegisterAction("End Turn", () => {
            if (CombatController.IsInTurnBasedCombat() || settings.EndTurnKeyBindShouldAlsoPause) {
                hasPressedPauseBind = true;
                Game.Instance.PauseBind();
                hasPressedPauseBind = false;
            }
        });
        KeyBindings.RegisterAction("Fast Forward", () => {
            if (CombatController.IsInTurnBasedCombat()) {
                hasPressedFastBind = true;
                Game.Instance.PauseBind();
                hasPressedFastBind = false;
            }
        });
        KeyBindings.RegisterAction("Alt RMB", () => { });
        return true;
    }
    public static void OnSaveGUI(UnityModManager.ModEntry modEntry) => settings.Save(modEntry);
    public static void OnGUI(UnityModManager.ModEntry modEntry) {
        if (DisclosureToggle("Use a separate End Turn hotkey?", ref settings.EndTurnHotkeyEnabled)) {
            if (settings.EndTurnHotkeyEnabled) {
                EndTurnPatches.Patch();
            } else {
                EndTurnPatches.UnPatch();
            }
        }
        if (settings.EndTurnHotkeyEnabled) {
            using (HorizontalScope()) {
                Space(50);
                using (VerticalScope()) {
                    KeyBindPicker("End Turn", "End Turn Hotkey");
                    Toggle("End Turn key should also Pause/Unpause the game outside of combat", ref settings.EndTurnKeyBindShouldAlsoPause, AutoWidth());
                }
            }
        }
        Div();
        if (DisclosureToggle("Use a separate Speed Up Combat Turn hotkey?", ref settings.FastForwardHotkeyEnabled)) {
            if (settings.FastForwardHotkeyEnabled) {
                SpeedUpPatches.Patch();
            } else {
                SpeedUpPatches.UnPatch();
            }
        }
        if (settings.FastForwardHotkeyEnabled) {
            using (HorizontalScope()) {
                Space(50);
                using (VerticalScope()) {
                    KeyBindPicker("Fast Forward", "Speed Up Hotkey");
                }
            }
        }
        Div();
        if (DisclosureToggle("Use right click to rotate camera", ref settings.RightClickRotate)) {
            if (settings.RightClickRotate) {
                RightClickPatches.Patch();
            } else {
                RightClickPatches.UnPatch();
            }
        }
        if (settings.RightClickRotate) {
            using (HorizontalScope()) {
                Space(50);
                using (VerticalScope()) {
                    Toggle("Still permit middle mouse click to rotate camera", ref settings.MiddleClickAlsoRotate, AutoWidth());
                    KeyBindPicker("Alt RMB", "RMB + this key for default right click (default is Shift)");
                }
            }
        }
    }
    public static void OnUpdate(UnityModManager.ModEntry modEntry, float t) {
        KeyBindings.OnUpdate();
    }
    public static class EndTurnPatches {
        public static void Patch() {
            HarmonyInstance.Patch(AccessTools.Method(typeof(Game), nameof(Game.PauseBind)), transpiler: new(AccessTools.Method(typeof(EndTurnPatches), nameof(PauseBind))));
        }
        public static void UnPatch() {
            HarmonyInstance.Unpatch(AccessTools.Method(typeof(Game), nameof(Game.PauseBind)), HarmonyPatchType.Transpiler, HarmonyInstance.Id);
        }
        private static IEnumerable<CodeInstruction> PauseBind(IEnumerable<CodeInstruction> instructions) {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].Calls(AccessTools.Method(typeof(CombatController), nameof(CombatController.IsInTurnBasedCombat)))) {
                    codes.InsertRange(i + 2, [CodeInstruction.LoadField(typeof(Main), nameof(hasPressedPauseBind)), new CodeInstruction(OpCodes.Brfalse, codes[i + 1].operand)]);
                }
            }
            return codes;
        }
    }
    public static class SpeedUpPatches {
        public static void Patch() {
            HarmonyInstance.Patch(AccessTools.Method(typeof(Game), nameof(Game.PauseBind)), transpiler: new(AccessTools.Method(typeof(EndTurnPatches), nameof(PauseBind))));
        }
        public static void UnPatch() {
            HarmonyInstance.Unpatch(AccessTools.Method(typeof(Game), nameof(Game.PauseBind)), HarmonyPatchType.Transpiler, HarmonyInstance.Id);
        }
        private static IEnumerable<CodeInstruction> PauseBind(IEnumerable<CodeInstruction> instructions) {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].LoadsConstant("Player.UISettings.DoSpeedUp()")) {
                    codes.InsertRange(i, [CodeInstruction.LoadField(typeof(Main), nameof(hasPressedFastBind)), new CodeInstruction(OpCodes.Brfalse, codes.First(c => c.LoadsConstant("End of PauseBind() method")).operand)]);
                }
            }
            return codes;
        }
    }
    public static class RightClickPatches {
        private static Type[] Types = [typeof(PlaceRestMarkerHandler), typeof(ClickWithSelectedAbilityHandler), typeof(ClickUnitHandler), 
            typeof(ClickGroundHandler), typeof(ClickMapObjectHandler), typeof(ClickOnDetectClicksObjectHandler), typeof(TacticalCombatClickGroundHandler)];
        /*
        public static IEnumerable<CodeInstruction> Generic_OnClick(IEnumerable<CodeInstruction> instructions) {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].opcode == OpCodes.Ldarg_3 && codes[i + 1].opcode == OpCodes.Ldc_I4_1 && (codes[i + 2].opcode == OpCodes.Bne_Un || codes[i + 2].opcode == OpCodes.Bne_Un_S)) {
                    codes.InsertRange(i + 3, [CodeInstruction.Call(() => IsShiftPressed()), new CodeInstruction(OpCodes.Brfalse, codes[i + 2].operand)]);
                }
            }
            return codes;
        }
        */
        public static bool OnClick_Prefix(int button, ref bool __result) {
            if ((button == 1 || GetRmbUp()) && !IsShiftPressed()) {
                __result = false;
                return false;
            }
            return true;
        }
        public static void Patch() {
            foreach (var t in Types) {
                HarmonyInstance.Patch(AccessTools.Method(t, "OnClick", [typeof(UnityEngine.GameObject), typeof(UnityEngine.Vector3), typeof(int), typeof(bool), typeof(bool), typeof(bool)]),
                    prefix: AccessTools.Method(typeof(RightClickPatches), nameof(OnClick_Prefix)));
            }
            HarmonyInstance.Patch(AccessTools.Method(typeof(CameraRig), nameof(CameraRig.RotateByMiddleButton)), transpiler: new(AccessTools.Method(typeof(RightClickPatches), nameof(CameraRig_RotateByMiddleButton))));
            HarmonyInstance.Patch(AccessTools.Method(typeof(PointerController), nameof(PointerController.Tick)), transpiler: new(AccessTools.Method(typeof(RightClickPatches), nameof(PointerController_Tick))));
            HarmonyInstance.Patch(AccessTools.Method(typeof(PointerController2D), nameof(PointerController2D.Tick)), transpiler: new(AccessTools.Method(typeof(RightClickPatches), nameof(PointerController2D_Tick))));
            rightClickIsPatched = true;
        }
        public static void UnPatch() {
            foreach (var t in Types) {
                HarmonyInstance.Unpatch(AccessTools.Method(t, "OnClick", [typeof(UnityEngine.GameObject), typeof(UnityEngine.Vector3), typeof(int), typeof(bool), typeof(bool), typeof(bool)]),
                    HarmonyPatchType.Prefix, HarmonyInstance.Id);
            }
            HarmonyInstance.Unpatch(AccessTools.Method(typeof(CameraRig), nameof(CameraRig.RotateByMiddleButton)), HarmonyPatchType.Transpiler, HarmonyInstance.Id);
            HarmonyInstance.Unpatch(AccessTools.Method(typeof(PointerController), nameof(PointerController.Tick)), HarmonyPatchType.Transpiler, HarmonyInstance.Id);
            HarmonyInstance.Unpatch(AccessTools.Method(typeof(PointerController2D), nameof(PointerController2D.Tick)), HarmonyPatchType.Transpiler, HarmonyInstance.Id);
            rightClickIsPatched = false;
        }
        private static IEnumerable<CodeInstruction> CameraRig_RotateByMiddleButton(IEnumerable<CodeInstruction> instructions) {
            foreach (var inst in instructions) {
                if (inst.Calls(AccessTools.Method(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetMouseButtonDown)))) {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return CodeInstruction.Call(() => GetRmbAndNotShiftDown());
                } else if (inst.Calls(AccessTools.Method(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetMouseButtonUp)))) {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return CodeInstruction.Call(() => GetRmbUp());
                } else {
                    yield return inst;
                }
            }
        }
        private static IEnumerable<CodeInstruction> PointerController_Tick(IEnumerable<CodeInstruction> instructions) {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].LoadsField(AccessTools.Field(typeof(PointerController), nameof(PointerController.m_MouseDownButton)))
                    && codes[i+1].opcode == OpCodes.Ldc_I4_1
                    && (codes[i+2].opcode == OpCodes.Bne_Un || codes[i+2].opcode == OpCodes.Bne_Un_S)) {
                    codes.InsertRange(i + 3, [CodeInstruction.Call(() => IsShiftPressed()), new CodeInstruction(OpCodes.Brfalse, codes[i + 2].operand)]);
                }
                if (codes[i].opcode == OpCodes.Isinst && codes[i].OperandIs(typeof(IDragClickEventHandler))) {
                    codes.InsertRange(i + 4, [CodeInstruction.Call(() => IsShiftPressed()), new CodeInstruction(OpCodes.Brfalse, codes[i + 3].operand)]);
                }
                if (codes[i].opcode == OpCodes.Ldloc_0
                    && (codes[i + 1].opcode == OpCodes.Brfalse_S || codes[i + 1].opcode == OpCodes.Brfalse)
                    && codes[i + 2].opcode == OpCodes.Ldarg_0
                    && codes[i + 3].LoadsField(AccessTools.Field(typeof(PointerController), nameof(PointerController.m_MouseDownButton)))
                    && (codes[i + 4].opcode == OpCodes.Brtrue || codes[i + 4].opcode == OpCodes.Brtrue_S)) {
                    codes.InsertRange(i + 5, [new(OpCodes.Ldloc_0), new(OpCodes.Ldarg_0), CodeInstruction.Call((bool isControllerGamepad, PointerController instance) => PointerControllerConditional(isControllerGamepad, instance)), new(OpCodes.Brfalse, codes[i + 4].operand)]);
                }
            }
            return codes;
        }
        private static bool PointerControllerConditional(bool isControllerGamepad, PointerController instance) {
            return (instance.m_MouseDownButton == 0) || (!isControllerGamepad && (instance.m_MouseDownButton != 1 || IsShiftPressed()));
        }
        public static bool GetRmbAndNotShiftDown() {
            return (settings.RightClickRotate && UnityEngine.Input.GetMouseButtonDown(1) && !IsShiftPressed())
                || (settings.MiddleClickAlsoRotate && UnityEngine.Input.GetMouseButtonDown(2));
        }
        public static bool GetRmbUp() {
            return (settings.RightClickRotate && UnityEngine.Input.GetMouseButtonUp(1))
                || (settings.MiddleClickAlsoRotate && UnityEngine.Input.GetMouseButtonUp(2));
        }
        public static bool IsShiftPressed() {
            if (KeyBindings.bindings.TryGetValue("Alt RMB", out var bind) && bind != null && !bind.IsEmpty) {
                return UnityEngine.Input.GetKey(bind.Key);
            } else {
                return UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
            }
        }
        public static IEnumerable<CodeInstruction> PointerController2D_Tick(IEnumerable<CodeInstruction> instructions) {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].opcode == OpCodes.Ldc_I4_1 && codes[i + 1].Calls(AccessTools.Method(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetMouseButtonUp)))) {
                    codes.InsertRange(i + 3, [CodeInstruction.Call(() => IsShiftPressed()), new CodeInstruction(OpCodes.Brfalse, codes[i + 2].operand)]);
                }
            }
            return codes;
        }
    }
}
