using System;
using HarmonyLib;
using UnityEngine;

namespace KneeSlapperFreeze
{
    // Optional integrations: these types are absent in the standalone install.
    internal static class ProfileCompatibility
    {
        private static bool Comfy => AccessTools.TypeByName("ComfyQuickSlots.QuickSlotsManager") != null;
        private static Type Archery => AccessTools.TypeByName("BetterArchery.BetterArchery");
        private static bool Quiver
        {
            get
            {
                var setting = Archery == null ? null : AccessTools.Field(Archery, "ConfigQuiverEnabled")?.GetValue(null);
                return setting != null && (bool)AccessTools.Property(setting.GetType(), "Value").GetValue(setting, null);
            }
        }

        private static void Ensure(Player player)
        {
            if (!Comfy) return;
            var inventory = player.GetInventory();
            AccessTools.Field(typeof(Inventory), "m_name").SetValue(inventory, "ComfyQuickSlotsInventory");
            int minimum = Quiver ? 7 : 5;
            inventory.SetHeight(Math.Max(inventory.GetHeight(), minimum));
            if (Quiver) AccessTools.Field(Archery, "QuiverRowIndex").SetValue(null, inventory.GetHeight() - 1);
        }

        [HarmonyPatch(typeof(Player), "Awake")]
        private static class InventoryIdentity
        {
            [HarmonyPostfix, HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance) => Ensure(__instance);
        }

        [HarmonyPatch(typeof(Player), "SetInventorySize")]
        private static class PreserveEquipmentRow
        {
            [HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static void Prefix(ref int rows)
            {
                if (Comfy) rows = Math.Max(rows, 5);
            }
            [HarmonyPostfix, HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance) => Ensure(__instance);
        }

        [HarmonyPatch]
        private static class PreserveAfterSpawnAndDeath
        {
            private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Player), "OnSpawned");
                yield return AccessTools.Method(typeof(Player), "CreateTombStone");
            }
            [HarmonyPostfix, HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance) => Ensure(__instance);
        }
    }
}
