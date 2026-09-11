using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KneeSlapperFreeze
{
    public static class GatheringKit
    {
        public const string DeliveredKey = "kneeslapper.gatheringkit.v1";
        public static readonly string[] Equipment = {
            "Bow", "BowGold_FrostFire", "AxeGold_FrostFire", "PickaxeBlackMetal",
            "Hammer", "Hoe", "Cultivator", "Scythe", "FishingRod", "ArrowWood", "FishingBait"
        };

        public static bool IsKneeSlapper(Player player)
            => player && string.Equals(player.GetPlayerName(), "Knee-Slapper", StringComparison.Ordinal);

        private static readonly MethodInfo CloneShared = AccessTools.Method(typeof(object), "MemberwiseClone");
        public static int KitQuality(string name, ItemDrop.ItemData item)
        {
            if (name == "Bow") return 1;
            int minimum = name == "Hammer" || name == "Hoe" || name == "Cultivator" || name == "Scythe" ? 3
                : name == "BowGold_FrostFire" || name == "AxeGold_FrostFire" || name == "PickaxeBlackMetal" ? 4 : 1;
            return Math.Max(item.m_shared.m_maxQuality, minimum);
        }
        public static void MakeToolsIndestructible(Player player)
        {
            if (!IsKneeSlapper(player)) return;
            foreach (var item in player.GetInventory().GetAllItems())
            {
                if (!item.m_dropPrefab || !Equipment.Contains(item.m_dropPrefab.name)
                    || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo) continue;
                int maxQuality = KitQuality(item.m_dropPrefab.name, item);
                if (item.m_shared.m_useDurability || item.m_shared.m_maxQuality < maxQuality)
                {
                    // SharedData normally belongs to the global prefab. Give this item
                    // its own copy before changing it, preserving everyone else's gear.
                    item.m_shared = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(item.m_shared, null);
                    item.m_shared.m_useDurability = false;
                    item.m_shared.m_maxQuality = maxQuality;
                }
                item.m_durability = item.GetMaxDurability();
            }
        }

        public static void Deliver(Player player)
        {
            if (!IsKneeSlapper(player) || player != Player.m_localPlayer
                || player.m_customData.ContainsKey(DeliveredKey) || !ObjectDB.instance) return;
            var inventory = player.GetInventory();
            foreach (string prefabName in Equipment)
            {
                string key = DeliveredKey + "." + prefabName;
                if (player.m_customData.ContainsKey(key)) continue;
                var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                if (!prefab) throw new InvalidOperationException("Missing kit item: " + prefabName);
                var template = prefab.GetComponent<ItemDrop>().m_itemData;
                int quality = KitQuality(prefabName, template);
                // Adopt matching gear already carried, so retries and existing inventories
                // don't create unnecessary copies. Never remove or overwrite other items.
                var item = inventory.GetAllItems().FirstOrDefault(i => i.m_dropPrefab
                    && i.m_dropPrefab.name == prefabName && i.m_quality >= quality);
                if (item == null)
                {
                    if (inventory.GetEmptySlots() == 0)
                    {
                        player.Message(MessageHud.MessageType.TopLeft, "Make inventory space to receive the rest of Knee-Slapper's kit.");
                        return;
                    }
                    int amount = prefabName == "ArrowWood" || prefabName == "FishingBait"
                        ? Mathf.Min(100, template.m_shared.m_maxStackSize) : 1;
                    item = inventory.AddItem(prefabName, amount, quality, 0, player.GetPlayerID(),
                        "Knee-Slapper", new Vector2i(-1, -1), true, true, false);
                    if (item == null) return;
                }
                player.m_customData[key] = "delivered";
            }
            var bow = inventory.GetAllItems().First(i => i.m_dropPrefab && i.m_dropPrefab.name == "Bow");
            var arrows = inventory.GetAllItems().First(i => i.m_dropPrefab && i.m_dropPrefab.name == "ArrowWood");
            MakeToolsIndestructible(player);
            if (!player.EquipItem(bow, false) || !player.EquipItem(arrows, false)) return;
            player.m_customData[DeliveredKey] = "delivered";
            player.Message(MessageHud.MessageType.Center, "Gathering kit ready: infinite wooden arrows and stamina; carry weight 10,000.");
            // The normal character save persists the gear and delivery markers together.
        }

        [HarmonyPatch(typeof(Attack), "UseAmmo")]
        private static class UnlimitedWoodenArrows
        {
            private static bool Prefix(Humanoid ___m_character, ItemDrop.ItemData ___m_weapon,
                ref ItemDrop.ItemData ___m_ammoItem, ref ItemDrop.ItemData ammoItem, ref bool __result)
            {
                if (!IsKneeSlapper(___m_character as Player) || ___m_weapon == null
                    || string.IsNullOrWhiteSpace(___m_weapon.m_shared.m_ammoType)) return true;
                var inventory = ___m_character.GetInventory();
                var selected = ___m_character.GetAmmoItem();
                if (selected == null || !inventory.ContainsItem(selected)
                    || selected.m_shared.m_ammoType != ___m_weapon.m_shared.m_ammoType)
                    selected = inventory.GetAmmoItem(___m_weapon.m_shared.m_ammoType);
                if (selected?.m_dropPrefab == null || selected.m_dropPrefab.name != "ArrowWood") return true;
                ammoItem = selected;
                ___m_ammoItem = selected;
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
        private static class CarryCapacity
        {
            [HarmonyPriority(Priority.Last)]
            [HarmonyAfter("pl.zienti.cwelinskiPlecak")]
            private static void Postfix(Player __instance, ref float __result)
            {
                if (IsKneeSlapper(__instance)) __result = Mathf.Max(__result, 10000f);
            }
        }

        [HarmonyPatch(typeof(Player), "RPC_UseStamina")]
        private static class UnlimitedStamina
        {
            private static bool Prefix(Player __instance) => !IsKneeSlapper(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.HaveStamina))]
        private static class SufficientStamina
        {
            private static void Postfix(Player __instance, ref bool __result)
            {
                if (IsKneeSlapper(__instance)) __result = true;
            }
        }
    }
}
