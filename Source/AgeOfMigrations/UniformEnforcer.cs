using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AgeOfMigrations
{
    /// <summary>
    /// Put on a PawnKindDef to give it a strict uniform: after the pawn is generated, it wears exactly its
    /// apparelRequired items (plus anything listed in allowedExtra), and nothing else. This stops the game
    /// from swapping helmets for tuques/caps on cold-weather raids, or adding random role clothing.
    /// </summary>
    public class UniformExtension : DefModExtension
    {
        public List<ThingDef> allowedExtra = new List<ThingDef>();
    }

    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            new Harmony("syneezvetik.ageofmigrations").PatchAll();
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_GeneratePawn_Uniform
    {
        public static void Postfix(Pawn __result)
        {
            Pawn pawn = __result;
            if (pawn?.apparel == null || pawn.kindDef == null)
                return;

            UniformExtension uniform = pawn.kindDef.GetModExtension<UniformExtension>();
            if (uniform != null)
                EnforceUniform(pawn, uniform);
            else
                RemoveRomanGear(pawn);
        }

        private static bool IsRomanApparel(ThingDef def) => def.defName.StartsWith("LR_");

        /// <summary>Other factions keep their own clothes; any Roman uniform piece they got is taken off.</summary>
        private static void RemoveRomanGear(Pawn pawn)
        {
            if (pawn.Faction != null && pawn.Faction.def.defName.StartsWith("LR_"))
                return;
            foreach (Apparel ap in pawn.apparel.WornApparel.ToList())
            {
                if (IsRomanApparel(ap.def))
                {
                    pawn.apparel.Remove(ap);
                    ap.Destroy();
                }
            }
        }

        private static void EnforceUniform(Pawn pawn, UniformExtension uniform)
        {
            List<ThingDef> required = pawn.kindDef.apparelRequired ?? new List<ThingDef>();
            var allowed = new HashSet<ThingDef>(required);
            if (uniform.allowedExtra != null)
                allowed.UnionWith(uniform.allowedExtra);

            // 1. Take off everything that isn't part of the uniform (tuques, caps, parkas, role apparel...)
            foreach (Apparel ap in pawn.apparel.WornApparel.ToList())
            {
                if (!allowed.Contains(ap.def))
                {
                    pawn.apparel.Remove(ap);
                    ap.Destroy();
                }
            }

            // 2. Put on any required piece that is missing (e.g. a helmet that was replaced)
            foreach (ThingDef def in required)
            {
                if (def == null || pawn.apparel.WornApparel.Any(a => a.def == def))
                    continue;
                if (!ApparelUtility.HasPartsToWear(pawn, def))
                    continue;
                ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
                Apparel ap = (Apparel)ThingMaker.MakeThing(def, stuff);
                pawn.apparel.Wear(ap, false);
            }
        }
    }
}
