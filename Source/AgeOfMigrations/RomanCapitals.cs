using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace AgeOfMigrations
{
    /// <summary>
    /// One imperial capital per Roman faction. It is a normal settlement of that faction, renamed after the real
    /// capital, drawn with its own icon on the world map, and generated with the huge capital layout (needs VEF).
    /// If the capital is destroyed, it stays fallen: no new capital is picked.
    /// </summary>
    public class GameComponent_RomanCapitals : GameComponent
    {
        public static GameComponent_RomanCapitals Instance;

        private static readonly Dictionary<string, string> CapitalNames = new Dictionary<string, string>
        {
            { "LR_WesternEmpire", "Ravenna" },
            { "LR_EasternEmpire", "Constantinopolis" },
        };

        private Dictionary<int, int> capitals = new Dictionary<int, int>();   // faction loadID -> settlement ID
        private List<int> keysWork, valuesWork;

        public GameComponent_RomanCapitals(Game game) { Instance = this; }

        public override void StartedNewGame() { Instance = this; AssignCapitals(); }
        public override void LoadedGame() { Instance = this; AssignCapitals(); }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref capitals, "capitals", LookMode.Value, LookMode.Value, ref keysWork, ref valuesWork);
            if (capitals == null) capitals = new Dictionary<int, int>();
        }

        private void AssignCapitals()
        {
            if (Find.World == null || Find.FactionManager == null)
                return;
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (faction.defeated || !CapitalNames.TryGetValue(faction.def.defName, out string name))
                    continue;
                if (capitals.ContainsKey(faction.loadID))
                    continue;   // already has a capital (or its capital has fallen)

                List<Settlement> own = Find.WorldObjects.Settlements.Where(s => s.Faction == faction).ToList();
                if (own.Count == 0)
                    continue;
                Settlement capital = own.RandomElement();
                capital.Name = name;
                capitals[faction.loadID] = capital.ID;
                Log.Message($"[Age of Migrations] {faction.Name}: imperial capital is {name}.");
            }
        }

        /// <summary>True if this world object is a Roman capital; side is "West" or "East".</summary>
        public static bool IsCapital(WorldObject wo, out string side)
        {
            side = null;
            if (Instance == null || !(wo is Settlement s) || s.Faction == null)
                return false;
            if (!Instance.capitals.TryGetValue(s.Faction.loadID, out int id) || id != s.ID)
                return false;
            side = s.Faction.def.defName == "LR_WesternEmpire" ? "West" : "East";
            return true;
        }
    }

    // ------------------------------------------------------------------ world map: icon and info

    [StaticConstructorOnStartup]
    public static class CapitalGraphics
    {
        public static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("World/LR_Capital", false);
        private static readonly Dictionary<Faction, Material> mats = new Dictionary<Faction, Material>();

        public static Material MaterialFor(Faction f)
        {
            if (Icon == null) return null;
            if (!mats.TryGetValue(f, out Material m))
            {
                m = MaterialPool.MatFrom("World/LR_Capital", ShaderDatabase.WorldOverlayTransparentLit, f.Color,
                    WorldMaterials.WorldObjectRenderQueue);
                mats[f] = m;
            }
            return m;
        }
    }

    [HarmonyPatch]
    public static class Patch_Settlement_Material
    {
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(Settlement), "Material");
        private static bool Prepare() => TargetMethod() != null;

        public static void Postfix(Settlement __instance, ref Material __result)
        {
            if (GameComponent_RomanCapitals.IsCapital(__instance, out _))
                __result = CapitalGraphics.MaterialFor(__instance.Faction) ?? __result;
        }
    }

    [HarmonyPatch]
    public static class Patch_Settlement_ExpandingIcon
    {
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(Settlement), "ExpandingIcon");
        private static bool Prepare() => TargetMethod() != null;

        public static void Postfix(Settlement __instance, ref Texture2D __result)
        {
            if (CapitalGraphics.Icon != null && GameComponent_RomanCapitals.IsCapital(__instance, out _))
                __result = CapitalGraphics.Icon;
        }
    }

    [HarmonyPatch]
    public static class Patch_Settlement_InspectString
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Settlement), "GetInspectString");
        private static bool Prepare() => TargetMethod() != null;

        public static void Postfix(Settlement __instance, ref string __result)
        {
            if (GameComponent_RomanCapitals.IsCapital(__instance, out _))
            {
                string line = $"Imperial capital of the {__instance.Faction.Name}. Heavily garrisoned and densely populated.";
                __result = string.IsNullOrEmpty(__result) ? line : line + "\n" + __result;
            }
        }
    }

    // ------------------------------------------------------------------ map generation (only with VEF)

    /// <summary>
    /// When VEF generates the map of a capital, swap the faction's normal layouts for the capital layout,
    /// then put every Roman placed in the layout into the settlement's defence group.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CapitalMapGen
    {
        private static FieldInfo layoutsField;
        private static Type layoutType;

        static CapitalMapGen()
        {
            Type optionType = GenTypes.GetTypeInAnyAssembly("KCSG.CustomGenOption");
            layoutType = GenTypes.GetTypeInAnyAssembly("KCSG.StructureLayoutDef");
            MethodInfo generate = optionType == null ? null
                : AccessTools.Method(optionType, "Generate", new[] { typeof(IntVec3), typeof(Map) });
            layoutsField = optionType == null ? null : AccessTools.Field(optionType, "chooseFromlayouts");
            if (generate == null || layoutsField == null || layoutType == null)
                return;   // VEF not active: capitals still exist on the world map, with a normal settlement map

            new Harmony("syneezvetik.ageofmigrations.capital").Patch(generate,
                prefix: new HarmonyMethod(typeof(CapitalMapGen), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(CapitalMapGen), nameof(Postfix)),
                finalizer: new HarmonyMethod(typeof(CapitalMapGen), nameof(Finalizer)));
        }

        public static void Prefix(object __instance, Map map, ref object __state)
        {
            __state = null;
            if (map?.Parent == null || !GameComponent_RomanCapitals.IsCapital(map.Parent, out string side))
                return;
            Def def = GenDefDatabase.GetDefSilentFail(layoutType, $"LR_{side}_Capital", false);
            if (def == null)
                return;
            __state = layoutsField.GetValue(__instance);
            IList only = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(layoutType));
            only.Add(def);
            layoutsField.SetValue(__instance, only);
        }

        public static void Postfix(Map map, object __state)
        {
            if (__state == null || map == null)
                return;
            try { GatherPawns(map); }
            catch (Exception e) { Log.Warning("[Age of Migrations] Could not organise capital garrison: " + e.Message); }
        }

        // Always restore the faction's normal layouts, even if generation threw.
        public static Exception Finalizer(Exception __exception, object __instance, object __state)
        {
            if (__state != null)
                layoutsField.SetValue(__instance, __state);
            return __exception;
        }

        private static void GatherPawns(Map map)
        {
            Faction faction = map.ParentFaction;
            if (faction == null || faction == Faction.OfPlayer)
                return;
            Lord lord = map.lordManager.lords.FirstOrDefault(l => l.faction == faction && l.LordJob is LordJob_DefendBase)
                        ?? LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, map.Center, 25000), map);
            int added = 0;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (p.Faction == faction && p.GetLord() == null)
                {
                    lord.AddPawn(p);
                    added++;
                }
            }
            Log.Message($"[Age of Migrations] Capital garrison: {added} citizens and soldiers joined the defence.");
        }
    }
}
