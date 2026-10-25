using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace AgeOfMigrations
{
    /// <summary>
    /// Every Roman, Vandal, Visigoth, Hun, Saxon, Ostrogoth and Frankish settlement is either a camp or a proper settlement. The kind is worked out from
    /// the settlement's ID, so it is fixed for the whole game without saving anything. It is shown on the world map
    /// after the name, explained in the inspect pane, and the map is generated with a layout of that kind (with VEF).
    /// Capitals are left alone: they have their own layout and label.
    /// </summary>
    public static class SettlementKinds
    {
        public static bool TryGet(Settlement s, out bool camp, out string kind)
        {
            camp = false;
            kind = null;
            if (s?.Faction == null || GameComponent_RomanCapitals.IsCapital(s, out _))
                return false;
            string f = s.Faction.def.defName;
            bool roman = f == "LR_WesternEmpire" || f == "LR_EasternEmpire";
            bool huns = f == "LR_Huns";
            bool persia = f == "LR_Sassanids";
            if (!roman && !huns && !persia && f != "LR_Vandals" && f != "LR_Visigoths" && f != "LR_Saxons" && f != "LR_Ostrogoths" && f != "LR_Franks")
                return false;
            uint h = unchecked((uint)s.ID * 2654435761u);
            camp = ((h >> 13) & 1u) == 0u;
            kind = roman ? (camp ? "marching camp" : "town") : persia ? (camp ? "army camp" : "city") : huns ? (camp ? "nomad camp" : "royal camp") : (camp ? "migrant camp" : "village");
            return true;
        }

        public static string Description(Settlement s, bool camp)
        {
            string f = s.Faction.def.defName;
            if (f == "LR_WesternEmpire" || f == "LR_EasternEmpire")
                return camp ? "Marching camp: a palisaded army camp of leather tents, laid out around the commander's tent."
                            : "Town: a walled Roman town with towers, a forum, houses, workshops and a basilica.";
            if (f == "LR_Huns")
                return camp ? "Nomad camp: round felt tents among great herds of horses. No walls, and most of the warriors are mounted."
                            : "Royal camp: a lord's wooden hall behind a palisade, surrounded by the felt tents of his people.";
            if (f == "LR_Sassanids")
                return camp ? "Army camp: a Persian army on campaign, rows of tents round the commander's pavilion, with horse lines, camels and war elephants."
                            : "City: a walled Persian city with a palace hall, a fire temple, a paradise garden, a bazaar, houses and stables.";
            if (f == "LR_Franks")
                return camp ? "Migrant camp: Frankish families on the move, camped around their fires beside their wagons. No walls."
                            : "Village: a Frankish village behind a palisade, with the king's hall, longhouses, a holy grove of the old gods, pens and fields.";
            if (f == "LR_Saxons")
                return camp ? "Migrant camp: Saxon families come over the sea, camped around their fires beside their wagons. No walls."
                            : "Village: a Saxon village behind a palisade, with a lord's mead hall, longhouses, a holy grove of the old gods, pens and fields.";
            return camp ? "Migrant camp: families on the move, camped around their fires beside their wagons. No walls."
                        : "Village: a settled village behind a palisade, with a king's hall, longhouses, a church, pens and fields.";
        }

        /// <summary>Layout names of the right kind contain one of these.</summary>
        public static bool LayoutMatches(string layout, bool camp)
        {
            return camp ? layout.Contains("_Castra_") || layout.Contains("_Camp_")
                        : layout.Contains("_Oppidum_") || layout.Contains("_Village_");
        }
    }

    /// <summary>On the world map, settlements show their kind after the name: "Gaisheim (migrant camp)".</summary>
    [HarmonyPatch]
    public static class Patch_Settlement_LabelKind
    {
        private static Func<bool> worldShown;

        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(Settlement), "Label");

        private static bool Prepare()
        {
            if (TargetMethod() == null)
                return false;
            // name of this property has changed between game versions
            PropertyInfo p = AccessTools.Property(typeof(WorldRendererUtility), "WorldRendered")
                             ?? AccessTools.Property(typeof(WorldRendererUtility), "WorldRenderedNow")
                             ?? AccessTools.Property(typeof(WorldRendererUtility), "WorldSelected");
            if (p == null || p.GetGetMethod() == null || !p.GetGetMethod().IsStatic)
                return false;
            MethodInfo getter = p.GetGetMethod();
            worldShown = () => (bool)getter.Invoke(null, null);
            return true;
        }

        public static void Postfix(WorldObject __instance, ref string __result)
        {
            if (!(__instance is Settlement settlement) || string.IsNullOrEmpty(__result) || worldShown == null)
                return;
            try
            {
                if (!worldShown())
                    return;   // only on the world map; letters and messages keep the plain name
            }
            catch
            {
                return;
            }
            if (SettlementKinds.TryGet(settlement, out _, out string kind))
                __result = __result + " (" + kind + ")";
        }
    }

    [HarmonyPatch]
    public static class Patch_Settlement_InspectKind
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Settlement), "GetInspectString");
        private static bool Prepare() => TargetMethod() != null;

        public static void Postfix(Settlement __instance, ref string __result)
        {
            if (!SettlementKinds.TryGet(__instance, out bool camp, out _))
                return;
            string line = SettlementKinds.Description(__instance, camp);
            __result = string.IsNullOrEmpty(__result) ? line : line + "\n" + __result;
        }
    }

    /// <summary>When VEF builds the map, only layouts of the settlement's kind are allowed.</summary>
    [StaticConstructorOnStartup]
    public static class SettlementKindMapGen
    {
        private static FieldInfo layoutsField;
        private static Type layoutType;

        static SettlementKindMapGen()
        {
            Type optionType = GenTypes.GetTypeInAnyAssembly("KCSG.CustomGenOption");
            layoutType = GenTypes.GetTypeInAnyAssembly("KCSG.StructureLayoutDef");
            MethodInfo generate = optionType == null ? null
                : AccessTools.Method(optionType, "Generate", new[] { typeof(IntVec3), typeof(Map) });
            layoutsField = optionType == null ? null : AccessTools.Field(optionType, "chooseFromlayouts");
            if (generate == null || layoutsField == null || layoutType == null)
                return;
            new Harmony("syneezvetik.ageofmigrations.settlementkinds").Patch(generate,
                prefix: new HarmonyMethod(typeof(SettlementKindMapGen), nameof(Prefix)),
                finalizer: new HarmonyMethod(typeof(SettlementKindMapGen), nameof(Finalizer)));
        }

        public static void Prefix(object __instance, Map map, ref object __state)
        {
            __state = null;
            if (!(map?.Parent is Settlement s) || !SettlementKinds.TryGet(s, out bool camp, out _))
                return;
            if (!(layoutsField.GetValue(__instance) is IList all) || all.Count == 0)
                return;
            IList pick = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(layoutType));
            foreach (object l in all)
            {
                if (l is Def d && SettlementKinds.LayoutMatches(d.defName, camp))
                    pick.Add(l);
            }
            if (pick.Count == 0)
                return;   // no layout of that kind: leave the choice to VEF
            __state = all;
            layoutsField.SetValue(__instance, pick);
        }

        public static Exception Finalizer(Exception __exception, object __instance, object __state)
        {
            if (__state != null)
                layoutsField.SetValue(__instance, __state);
            return __exception;
        }
    }
}
