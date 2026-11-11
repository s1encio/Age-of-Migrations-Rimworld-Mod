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
            { "LR_Sassanids", "Ctesiphon" },
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

        public void AssignCapitals()
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
                Settlement capital = own.FirstOrDefault(s => s.Name == name) ?? own.RandomElement();   // the accurate map places the real capital
                capital.Name = name;
                capitals[faction.loadID] = capital.ID;
                Log.Message($"[Age of Migrations] {faction.Name}: imperial capital is {name}.");
            }
        }

        /// <summary>True if this world object is a capital; side is "West", "East" or "Persian" (the layout is LR_{side}_Capital).</summary>
        public static bool IsCapital(WorldObject wo, out string side)
        {
            side = null;
            if (Instance == null || !(wo is Settlement s) || s.Faction == null)
                return false;
            if (!Instance.capitals.TryGetValue(s.Faction.loadID, out int id) || id != s.ID)
                return false;
            string f = s.Faction.def.defName;
            side = f == "LR_WesternEmpire" ? "West" : f == "LR_Sassanids" ? "Persian" : "East";
            return true;
        }
    }

    // ------------------------------------------------------------------ world map: icon and info

    [StaticConstructorOnStartup]
    public static class CapitalGraphics
    {
        public static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("World/LR_Capital", false);
        public static readonly Texture2D IconPersian = ContentFinder<Texture2D>.Get("World/LR_CapitalPersian", false);
        private static readonly Dictionary<Faction, Material> mats = new Dictionary<Faction, Material>();

        public static bool Persian(Faction f) => f?.def?.defName == "LR_Sassanids";

        public static Texture2D IconFor(Faction f) => Persian(f) && IconPersian != null ? IconPersian : Icon;

        public static Material MaterialFor(Faction f)
        {
            if (IconFor(f) == null) return null;
            if (!mats.TryGetValue(f, out Material m))
            {
                string path = Persian(f) && IconPersian != null ? "World/LR_CapitalPersian" : "World/LR_Capital";
                m = MaterialPool.MatFrom(path, ShaderDatabase.WorldOverlayTransparentLit, f.Color,
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
            if (GameComponent_RomanCapitals.IsCapital(__instance, out _) && CapitalGraphics.IconFor(__instance.Faction) != null)
                __result = CapitalGraphics.IconFor(__instance.Faction);
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
                string line = CapitalGraphics.Persian(__instance.Faction)
                    ? $"Royal capital of the {__instance.Faction.Name}, seat of the King of Kings. Heavily garrisoned and densely populated."
                    : $"Imperial capital of the {__instance.Faction.Name}. Heavily garrisoned and densely populated.";
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

        public static void Prefix(object __instance, IntVec3 loc, Map map, ref object __state)
        {
            __state = null;
            if (map?.Parent == null || !GameComponent_RomanCapitals.IsCapital(map.Parent, out string side))
                return;
            Def def = GenDefDatabase.GetDefSilentFail(layoutType, $"LR_{side}_Capital", false);
            if (def == null)
                return;
            __state = layoutsField.GetValue(__instance);
            try { ClearArea(map, loc, 172, 172); }
            catch (Exception e) { Log.Warning("[Age of Migrations] Could not clear capital area: " + e.Message); }
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

        /// <summary>Flatten the city area: no mountains, rock, ruins or water inside the walls.</summary>
        private static void ClearArea(Map map, IntVec3 center, int w, int h)
        {
            CellRect rect = CellRect.CenteredOn(center, w, h).ClipInsideMap(map);
            foreach (IntVec3 c in rect)
            {
                List<Thing> things = c.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    if (!(t is Pawn) && t.def.destroyable && !t.Destroyed)
                        t.Destroy(DestroyMode.Vanish);
                }
                map.roofGrid.SetRoof(c, null);
                TerrainDef terr = c.GetTerrain(map);
                if (terr.passability == Traversability.Impassable || terr.IsWater
                    || terr.defName.EndsWith("_Rough") || terr.defName.EndsWith("_RoughHewn") || terr.defName.EndsWith("_Smooth"))
                    map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
            }
        }

        /// <summary>Put the faction's real leader on the throne, in place of the placeholder emperor from the layout.</summary>
        private static void SeatLeader(Map map, Faction faction)
        {
            Pawn placeholder = map.mapPawns.AllPawnsSpawned
                .FirstOrDefault(p => p.Faction == faction && (p.kindDef.defName.EndsWith("_Augustus") || p.kindDef.defName.EndsWith("_Shah")));
            Pawn leader = faction.leader;
            if (placeholder == null || leader == null || leader == placeholder)
                return;
            if (leader.Dead || leader.Spawned || leader.IsPrisoner || leader.GetCaravan() != null)
                return;   // the emperor is away, captured or dead: the placeholder stands in as a regent
            IntVec3 cell = placeholder.Position;
            placeholder.Destroy(DestroyMode.Vanish);
            if (Find.WorldPawns.Contains(leader))
                Find.WorldPawns.RemovePawn(leader);
            GenSpawn.Spawn(leader, cell, map);
            Log.Message($"[Age of Migrations] {leader.LabelShort} holds court in {map.Parent.LabelCap}.");
        }

        private static void GatherPawns(Map map)
        {
            Faction faction = map.ParentFaction;
            if (faction == null || faction == Faction.OfPlayer)
                return;
            try { SeatLeader(map, faction); }
            catch (Exception e) { Log.Warning("[Age of Migrations] Could not seat the emperor: " + e.Message); }
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

namespace AgeOfMigrations
{
    /// <summary>A gold crown floating above each Roman capital on the world map, zoomed in and zoomed out.</summary>
    [StaticConstructorOnStartup]
    public static class CapitalCrown
    {
        public static readonly Texture2D Tex = ContentFinder<Texture2D>.Get("World/LR_CapitalCrown", false);
        public static readonly Texture2D TexPersian = ContentFinder<Texture2D>.Get("World/LR_CapitalCrownPersian", false);
        private static Material mat, matPersian;
        private static readonly MethodInfo iconRect = AccessTools.Method(typeof(ExpandableWorldObjectsUtility), "ExpandedIconScreenRect");
        private static readonly MethodInfo transition = AccessTools.PropertyGetter(typeof(ExpandableWorldObjectsUtility), "TransitionPct");

        private static Material Mat => mat ?? (mat = MaterialPool.MatFrom("World/LR_CapitalCrown",
            ShaderDatabase.WorldOverlayTransparentLit, Color.white, WorldMaterials.WorldObjectRenderQueue + 1));
        private static Material MatPersian => matPersian ?? (matPersian = MaterialPool.MatFrom("World/LR_CapitalCrownPersian",
            ShaderDatabase.WorldOverlayTransparentLit, Color.white, WorldMaterials.WorldObjectRenderQueue + 1));

        private static bool UsePersian(WorldObject wo) => TexPersian != null && CapitalGraphics.Persian(wo.Faction);

        private static IEnumerable<Settlement> Capitals()
        {
            if (Find.WorldObjects == null) yield break;
            foreach (Settlement s in Find.WorldObjects.Settlements)
                if (GameComponent_RomanCapitals.IsCapital(s, out _)) yield return s;
        }

        // zoomed in: draw on the globe, just north of the settlement
        public static void DrawOnGlobe(WorldObject wo)
        {
            if (Tex == null || !GameComponent_RomanCapitals.IsCapital(wo, out _)) return;
            float size = Find.WorldGrid.AverageTileSize;
            Vector3 pos = wo.DrawPos;
            Vector3 n = pos.normalized;
            Vector3 north = (Vector3.up - n * Vector3.Dot(Vector3.up, n)).normalized;
            WorldRendererUtility.DrawQuadTangentialToPlanet(pos + north * size * 0.8f, size * 0.75f, 0.03f, UsePersian(wo) ? MatPersian : Mat);
        }

        // zoomed out: draw above the expanded settlement icon
        public static void DrawOnGUI()
        {
            if (Tex == null || iconRect == null || Event.current.type != EventType.Repaint) return;
            float pct = transition != null ? (float)transition.Invoke(null, null) : 1f;
            if (pct <= 0f) return;
            foreach (Settlement s in Capitals())
            {
                Rect r = (Rect)iconRect.Invoke(null, new object[] { s });
                if (r.width <= 0f) continue;
                Rect crown = new Rect(r.center.x - r.width * 0.45f, r.y - r.height * 0.85f, r.width * 0.9f, r.width * 0.9f);
                GUI.color = new Color(1f, 1f, 1f, pct);
                GUI.DrawTexture(crown, UsePersian(s) ? TexPersian : Tex);
                GUI.color = Color.white;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_WorldObject_Draw
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(WorldObject), "Draw");
        private static bool Prepare() => TargetMethod() != null;
        public static void Postfix(WorldObject __instance)
        {
            try { CapitalCrown.DrawOnGlobe(__instance); } catch { }
        }
    }

    [HarmonyPatch]
    public static class Patch_ExpandableWorldObjectsOnGUI
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ExpandableWorldObjectsUtility), "ExpandableWorldObjectsOnGUI");
        private static bool Prepare() => TargetMethod() != null;
        public static void Postfix()
        {
            try { CapitalCrown.DrawOnGUI(); } catch { }
        }
    }
}
