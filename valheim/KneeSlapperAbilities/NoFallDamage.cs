using HarmonyLib;

namespace KneeSlapperFreeze
{
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyFallDamage))]
    internal static class NoFallDamage
    {
        private static void Postfix(Character ___m_character, ref float damage)
        {
            if (GatheringKit.IsKneeSlapper(___m_character as Player)) damage = 0f;
        }
    }
}
