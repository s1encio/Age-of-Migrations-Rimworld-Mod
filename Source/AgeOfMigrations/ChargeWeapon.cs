using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AgeOfMigrations
{
    /// <summary>
    /// Put on a melee weapon: its hits do more damage the more momentum the wielder has built up.
    /// Momentum rises while the pawn keeps moving (on foot or mounted) and falls when it stops,
    /// so a long gallop into the enemy lands a much harder first blow than a standing thrust.
    /// </summary>
    public class ChargeWeaponExtension : DefModExtension
    {
        /// <summary>Momentum (cells per second) below which there is no bonus.</summary>
        public float minSpeed = 4f;
        /// <summary>Momentum at which the full bonus applies.</summary>
        public float fullSpeed = 6f;
        /// <summary>Extra damage at full momentum: 1.2 means up to 2.2x damage.</summary>
        public float maxBonus = 1.2f;
    }

    /// <summary>Tracks the momentum of every pawn that holds a charge weapon.</summary>
    public class GameComponent_Momentum : GameComponent
    {
        private const int Interval = 10;            // ticks between samples (6 per second)
        private const float Smoothing = 0.12f;      // how fast momentum builds and fades (~2-3 seconds to full)

        private class Track { public Vector3 lastPos; public float momentum; public int lastTick; }

        private static readonly Dictionary<int, Track> tracks = new Dictionary<int, Track>();

        public GameComponent_Momentum(Game game) { tracks.Clear(); }

        public static float MomentumOf(Pawn pawn)
        {
            return pawn != null && tracks.TryGetValue(pawn.thingIDNumber, out Track t) ? t.momentum : 0f;
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % Interval != 0)
                return;

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn.equipment?.Primary?.def.GetModExtension<ChargeWeaponExtension>() == null)
                        continue;

                    Vector3 pos = pawn.DrawPos;
                    if (!tracks.TryGetValue(pawn.thingIDNumber, out Track t) || now - t.lastTick > Interval * 3)
                    {
                        tracks[pawn.thingIDNumber] = new Track { lastPos = pos, momentum = 0f, lastTick = now };
                        continue;
                    }
                    Vector3 delta = pos - t.lastPos;
                    delta.y = 0f;
                    float speed = delta.magnitude / (Interval / 60f);   // cells per second
                    if (speed > 30f) speed = 0f;                        // teleport/respawn, not a gallop
                    t.momentum += (speed - t.momentum) * Smoothing;
                    t.lastPos = pos;
                    t.lastTick = now;
                }
            }

            // forget pawns that haven't been seen for a while
            if (now % 2500 == 0)
            {
                var stale = new List<int>();
                foreach (var kv in tracks)
                    if (now - kv.Value.lastTick > 600) stale.Add(kv.Key);
                foreach (int id in stale) tracks.Remove(id);
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_MeleeChargeDamage
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Verb_MeleeAttackDamage), "DamageInfosToApply");

        // If a future game version renames the method, skip only this patch instead of breaking the mod.
        private static bool Prepare()
        {
            if (TargetMethod() != null) return true;
            Log.Warning("[Age of Migrations] Melee damage method not found; kontos charge bonus disabled.");
            return false;
        }

        public static void Postfix(Verb_MeleeAttackDamage __instance, ref IEnumerable<DamageInfo> __result)
        {
            ThingWithComps weapon = __instance.EquipmentSource;
            ChargeWeaponExtension charge = weapon?.def.GetModExtension<ChargeWeaponExtension>();
            Pawn pawn = __instance.CasterPawn;
            if (charge == null || pawn == null)
                return;

            float momentum = GameComponent_Momentum.MomentumOf(pawn);
            float t = Mathf.InverseLerp(charge.minSpeed, charge.fullSpeed, momentum);
            if (t <= 0f)
                return;

            float factor = 1f + charge.maxBonus * t;
            __result = Scale(__result, factor);

            if (factor >= 1.25f && pawn.Spawned)
                MoteMaker.ThrowText(pawn.DrawPos + new Vector3(0f, 0f, 0.7f), pawn.Map,
                    "Charge! x" + factor.ToString("0.0"), new Color(1f, 0.85f, 0.4f), 2.2f);
        }

        private static IEnumerable<DamageInfo> Scale(IEnumerable<DamageInfo> infos, float factor)
        {
            foreach (DamageInfo info in infos)
            {
                DamageInfo scaled = info;
                scaled.SetAmount(info.Amount * factor);
                yield return scaled;
            }
        }
    }
}
