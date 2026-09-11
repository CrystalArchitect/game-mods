using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace KneeSlapperFreeze
{
    public static class DoubleJump
    {
        private sealed class FlightState { public bool AirJumpUsed; }
        private static readonly ConditionalWeakTable<Player, FlightState> Flights = new ConditionalWeakTable<Player, FlightState>();

        [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
        private static class ResetOnLanding
        {
            private static void Postfix(Character __instance)
            {
                var player = __instance as Player;
                if (!GatheringKit.IsKneeSlapper(player) || player != Player.m_localPlayer) return;
                if (player.IsOnGround() || player.IsSwimming() || player.IsDead())
                    Flights.GetValue(player, _ => new FlightState()).AirJumpUsed = false;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
        private static class ExtraJump
        {
            private static bool Prefix(Character __instance, bool force, Rigidbody ___m_body)
            {
                var player = __instance as Player;
                if (!GatheringKit.IsKneeSlapper(player) || player != Player.m_localPlayer || force) return true;
                var flight = Flights.GetValue(player, _ => new FlightState());
                if (player.IsOnGround())
                {
                    flight.AirJumpUsed = false;
                    return true;
                }
                if (flight.AirJumpUsed || player.IsDead() || player.InIntro() || player.IsTeleporting()
                    || player.IsAttached() || player.IsSwimming()
                    || player.IsDebugFlying() || player.IsFlying() || player.IsEncumbered()
                    || player.InDodge() || player.IsKnockedBack() || player.IsStaggering() || player.InAttack()
                    || GrapplingPoint.m_localGrappler)
                    return true;

                // Preserve sideways momentum and replace falling/rising velocity with
                // one normal jump impulse. ForceJump handles animation and jump events.
                Vector3 velocity = ___m_body.linearVelocity;
                float skill = player.GetSkills().GetSkillFactor(Skills.SkillType.Jump);
                velocity.y = player.m_jumpForce * (1f + skill * 0.4f);
                player.GetSEMan().ApplyStatusEffectJumpMods(ref velocity);
                if (velocity.y <= 0f) return true;
                flight.AirJumpUsed = true;
                player.ForceJump(velocity);
                return false;
            }
        }
    }
}
