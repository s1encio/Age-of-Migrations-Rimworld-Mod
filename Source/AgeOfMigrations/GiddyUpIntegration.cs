using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AgeOfMigrations
{
    /// <summary>Marks a Roman pawn kind as cavalry: with Giddy-Up installed it arrives mounted on one of these animals.</summary>
    public class CavalryExtension : DefModExtension
    {
        public List<PawnKindDef> mounts = new List<PawnKindDef>();
        public int mountChance = 100;
    }

    /// <summary>
    /// Optional Giddy-Up 2 support, done through reflection so the mod also runs without Giddy-Up.
    /// Every Roman pawn kind gets Giddy-Up's own "CustomMounts" settings: cavalry always rides its
    /// listed mounts, infantry never rides (so legionaries don't show up on random animals).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class GiddyUpIntegration
    {
        public static readonly bool Active;

        static GiddyUpIntegration()
        {
            Type customMounts = GenTypes.GetTypeInAnyAssembly("GiddyUp.CustomMounts");
            if (customMounts == null)
            {
                Log.Message("[Age of Migrations] Giddy-Up not found: Roman cavalry will fight on foot.");
                return;
            }

            int count = 0;
            foreach (PawnKindDef kind in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                if (!kind.defName.StartsWith("LR_"))
                    continue;
                try
                {
                    var ext = (DefModExtension)Activator.CreateInstance(customMounts, true);
                    Traverse t = Traverse.Create(ext);
                    CavalryExtension cav = kind.GetModExtension<CavalryExtension>();
                    if (cav != null && cav.mounts.Count > 0)
                    {
                        t.Field("mountChance").SetValue(cav.mountChance);
                        var mounts = t.Field("possibleMounts").GetValue<Dictionary<PawnKindDef, int>>()
                                     ?? new Dictionary<PawnKindDef, int>();
                        foreach (PawnKindDef m in cav.mounts)
                            if (m != null) mounts[m] = 1;
                        t.Field("possibleMounts").SetValue(mounts);
                        count++;
                    }
                    else
                    {
                        t.Field("mountChance").SetValue(1);   // Giddy-Up rolls 1-99: a chance of 1 never mounts
                    }
                    if (kind.modExtensions == null) kind.modExtensions = new List<DefModExtension>();
                    kind.modExtensions.Add(ext);
                }
                catch (Exception e)
                {
                    Log.Warning($"[Age of Migrations] Could not set up Giddy-Up mounts for {kind.defName}: {e.Message}");
                }
            }
            Active = true;
            Log.Message($"[Age of Migrations] Giddy-Up found: {count} Roman cavalry types will ride.");
        }
    }
}
