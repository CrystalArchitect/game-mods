using System;
using BepInEx;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace KneeSlapperFreeze
{
    [BepInPlugin(Guid, "Knee-Slapper's Abilities and Gear", "1.4.0")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.valheim.kneeslapperfreeze";
        public const string EffectName = "KneeSlapper_ArrowFreeze";
        public static readonly int EffectHash = EffectName.GetStableHashCode();
        private Harmony patches;

        private void Awake()
        {
            var effect = ScriptableObject.CreateInstance<ArrowFreeze>();
            effect.name = EffectName;
            effect.m_name = "Frozen";
            effect.m_tooltip = "Frozen by Knee-Slapper's wooden arrow for 2 seconds.";
            effect.m_ttl = 2f;
            if (!ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false)))
                throw new InvalidOperationException("Could not register arrow freeze.");
            patches = new Harmony(Guid);
            patches.PatchAll();
            Logger.LogInfo("Ready: Knee-Slapper + ArrowWood = 2-second freeze. Valheim " + global::Version.GetVersionString());
        }

        private float nextKitCheck;
        private void Update()
        {
            if (Time.time < nextKitCheck) return;
            nextKitCheck = Time.time + 2f;
            var player = Player.m_localPlayer;
            if (GatheringKit.IsKneeSlapper(player) && !player.IsDead() && !player.InIntro())
            {
                try { GatheringKit.Deliver(player); GatheringKit.MakeToolsIndestructible(player); }
                catch (Exception ex) { Logger.LogError("Gathering kit delivery failed: " + ex); nextKitCheck += 30f; }
            }
        }

        public static bool Matches(string playerName, string ammoPrefab)
        {
            return string.Equals(playerName, "Knee-Slapper", StringComparison.Ordinal)
                && string.Equals(ammoPrefab, "ArrowWood", StringComparison.Ordinal);
        }

        public static bool IsFrozen(Character character)
        {
            return character && !character.IsPlayer() && !character.IsDead()
                && character.GetSEMan() != null
                && character.GetSEMan().HaveStatusEffect(EffectHash);
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        private static class ArrowSetup
        {
            private static void Postfix(Projectile __instance, Character owner, ItemDrop.ItemData ammo, ref int ___m_statusEffectHash)
            {
                if (owner is Player player && ammo?.m_dropPrefab
                    && Matches(player.GetPlayerName(), ammo.m_dropPrefab.name))
                {
                    ___m_statusEffectHash = EffectHash;
                    // Better Archery can make each shot recoverable. These arrows were
                    // never consumed, so prevent an extra inventory item per shot.
                    if (__instance.m_spawnOnHit && __instance.m_spawnOnHit.name == "ArrowWood")
                        __instance.m_spawnOnHit = null;
                    var pickable = __instance.GetComponent<Pickable>();
                    if (pickable && pickable.m_itemPrefab && pickable.m_itemPrefab.name == "ArrowWood")
                        UnityEngine.Object.Destroy(pickable);
                }
            }
        }

        // Damage runs on the target's network owner, and vanilla code handles misses,
        // invulnerability and status refresh. Only allow our marker on enemy creatures.
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class EnemyFilter
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (hit.m_statusEffectHash != EffectHash) return;
                var player = hit.GetAttacker() as Player;
                if (!player || !Matches(player.GetPlayerName(), "ArrowWood")
                    || __instance.IsPlayer() || __instance.IsTamed()
                    || !BaseAI.IsEnemy(player, __instance))
                    hit.m_statusEffectHash = 0;
            }
        }

        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.UpdateAI))]
        private static class FreezeAI
        {
            private static bool Prefix(Character ___m_character, ref bool __result)
            {
                if (!IsFrozen(___m_character)) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Character), "UpdateMotion")]
        private static class FreezeMotion
        {
            private static bool Prefix(Character __instance) => !IsFrozen(__instance);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.CanMove))]
        private static class FreezeMovement
        {
            private static void Postfix(Character __instance, ref bool __result)
            {
                if (IsFrozen(__instance)) __result = false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), "UpdateAttack")]
        private static class FreezeAttackUpdate
        {
            private static bool Prefix(Humanoid __instance) => !IsFrozen(__instance);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
        private static class FreezeAttackTrigger
        {
            private static bool Prefix(Humanoid __instance) => !IsFrozen(__instance);
        }

        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        private static class FreezeAnimation
        {
            private static bool Prefix(Character ___m_character, Animator ___m_animator)
            {
                if (!IsFrozen(___m_character)) return true;
                ___m_animator.speed = 0f;
                return false;
            }
        }
    }

    public sealed class ArrowFreeze : StatusEffect
    {
        private Rigidbody body;
        private RigidbodyConstraints originalConstraints;
        private Animator animator;
        private float originalAnimationSpeed;
        private bool held;

        public override bool CanAdd(Character character)
            => character && !character.IsPlayer() && !character.IsTamed() && !character.IsDead();

        public override void Setup(Character character)
        {
            // Copy vanilla frost visuals, but use our own fixed-duration immobilization.
            var frost = ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectFrost);
            if (frost) m_startEffects = frost.m_startEffects;
            base.Setup(character);
            body = character.GetComponent<Rigidbody>();
            animator = character.GetComponentInChildren<Animator>();
            if (body)
            {
                originalConstraints = body.constraints;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.constraints = RigidbodyConstraints.FreezeAll;
            }
            if (animator)
            {
                originalAnimationSpeed = animator.speed;
                animator.speed = 0f;
            }
            held = true;
        }

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);
            if (m_character && m_character.IsDead()) Release();
        }

        public override bool IsDone() => m_time >= m_ttl || (m_character && m_character.IsDead());

        public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir)
            => speed = 0f;

        public override void Stop()
        {
            Release();
            base.Stop();
        }

        private void Release()
        {
            if (!held) return;
            held = false;
            if (body) body.constraints = originalConstraints;
            if (animator) animator.speed = originalAnimationSpeed;
        }
    }
}
