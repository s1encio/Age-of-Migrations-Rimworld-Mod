using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AgeOfMigrations
{
    // ------------------------------------------------------------------ the lands of each people

    /// <summary>
    /// Every land tile belongs to the faction of the nearest settlement (town, fort or camp) within reach.
    /// Recomputed when the settlements of the world change.
    /// </summary>
    public static class Territory
    {
        public const float ReachInTiles = 11f;
        private static World world;
        private static int signature;
        private static Faction[] owner = new Faction[0];

        private static int Signature()
        {
            unchecked
            {
                int h = 17;
                foreach (var s in Find.WorldObjects.Settlements)
                    if (Counts(s)) h = h * 31 + s.Tile.tileId * 7 + s.Faction.loadID;
                return h;
            }
        }

        private static bool Counts(Settlement s) =>
            s?.Faction != null && !s.Faction.IsPlayer && !s.Faction.Hidden && s.Tile.Valid && s.Tile.Layer != null && s.Tile.Layer.IsRootSurface;

        public static Faction[] Owners()
        {
            if (Find.World == null || Find.WorldGrid == null) return owner;
            int sig = Signature();
            if (world == Find.World && sig == signature && owner.Length == Find.WorldGrid.Surface.TilesCount) return owner;
            world = Find.World; signature = sig;
            Recompute();
            return owner;
        }

        public static void Invalidate() => world = null;

        private static void Recompute()
        {
            var layer = Find.WorldGrid.Surface;
            int n = layer.TilesCount;
            owner = new Faction[n];
            var seats = Find.WorldObjects.Settlements.Where(Counts).Select(s => (pos: layer.GetTileCenter(s.Tile), f: s.Faction)).ToList();
            if (seats.Count == 0) return;
            float reach = ReachInTiles * layer.AverageTileSize, reach2 = reach * reach;
            for (int i = 0; i < n; i++)
            {
                var tile = layer[i];
                if (tile == null || tile.WaterCovered) continue;
                var c = layer.GetTileCenter(i);
                float best = reach2; Faction f = null;
                for (int k = 0; k < seats.Count; k++)
                {
                    float d = (seats[k].pos - c).sqrMagnitude;
                    if (d < best) { best = d; f = seats[k].f; }
                }
                owner[i] = f;
            }
        }

        public static Faction OwnerOf(PlanetTile tile)
        {
            if (!tile.Valid || tile.Layer == null || !tile.Layer.IsRootSurface) return null;
            var o = Owners();
            return tile.tileId >= 0 && tile.tileId < o.Length ? o[tile.tileId] : null;
        }

        public static bool HasLands(FactionDef people) => people != null && Owners().Any(f => f != null && f.def == people);
    }

    /// <summary>Coloured lands and a border band along their edge, drawn over the planet surface.</summary>
    public class WorldDrawLayer_Borders : WorldDrawLayer
    {
        private static readonly Dictionary<Color, Material> Mats = new Dictionary<Color, Material>();
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Vector3> nverts = new List<Vector3>();
        private readonly List<PlanetTile> neighbors = new List<PlanetTile>();

        public override bool Visible => base.Visible && (AoMMod.Settings?.showBorders ?? true);

        private static Material Mat(Color c)
        {
            if (!Mats.TryGetValue(c, out var m))
            {
                m = SolidColorMaterials.NewSolidColorMaterial(c, ShaderDatabase.WorldOverlayTransparentLit);
                m.renderQueue = WorldMaterials.DebugTileRenderQueue + 5;
                Mats[c] = m;
            }
            return m;
        }

        private static Vector3 Lift(Vector3 v, Vector3 origin, float h) => v + (v - origin).normalized * h;

        public override IEnumerable Regenerate()
        {
            foreach (object o in base.Regenerate()) yield return o;
            if (planetLayer == null || !planetLayer.IsRootSurface) yield break;
            Faction[] owners;
            try { owners = Territory.Owners(); }
            catch (Exception e) { Log.ErrorOnce("[Age of Migrations] borders: " + e, 81233); yield break; }
            var origin = planetLayer.Origin;
            int n = Math.Min(owners.Length, planetLayer.TilesCount);
            for (int i = 0; i < n; i++)
            {
                var f = owners[i];
                if (f == null) continue;
                var col = f.Color;
                var fill = GetSubMesh(Mat(new Color(col.r, col.g, col.b, 0.16f)));
                var line = GetSubMesh(Mat(new Color(col.r, col.g, col.b, 0.85f)));
                planetLayer.GetTileVertices(i, verts);
                if (verts.Count < 3) continue;
                var center = planetLayer.GetTileCenter(i);
                // fill
                int b0 = fill.verts.Count;
                for (int k = 0; k < verts.Count; k++) fill.verts.Add(Lift(verts[k], origin, 0.06f));
                for (int k = 1; k < verts.Count - 1; k++)
                {
                    fill.tris.Add(b0); fill.tris.Add(b0 + k + 1); fill.tris.Add(b0 + k);
                    fill.tris.Add(b0); fill.tris.Add(b0 + k); fill.tris.Add(b0 + k + 1);
                }
                // border band on every edge that faces another owner (or the sea, or no one)
                planetLayer.GetTileNeighbors(i, neighbors);
                foreach (var nb in neighbors)
                {
                    if (nb.tileId >= 0 && nb.tileId < owners.Length && owners[nb.tileId] == f) continue;
                    planetLayer.GetTileVertices(nb.tileId, nverts);
                    Vector3? a = null, b = null;
                    foreach (var v in verts)
                        if (nverts.Any(w => (w - v).sqrMagnitude < 1e-4f)) { if (a == null) a = v; else { b = v; break; } }
                    if (a == null || b == null) continue;
                    var ai = Vector3.Lerp(a.Value, center, 0.22f);
                    var bi = Vector3.Lerp(b.Value, center, 0.22f);
                    int l0 = line.verts.Count;
                    line.verts.Add(Lift(a.Value, origin, 0.07f)); line.verts.Add(Lift(b.Value, origin, 0.07f));
                    line.verts.Add(Lift(bi, origin, 0.07f)); line.verts.Add(Lift(ai, origin, 0.07f));
                    line.tris.Add(l0); line.tris.Add(l0 + 2); line.tris.Add(l0 + 1);
                    line.tris.Add(l0); line.tris.Add(l0 + 3); line.tris.Add(l0 + 2);
                    line.tris.Add(l0); line.tris.Add(l0 + 1); line.tris.Add(l0 + 2);
                    line.tris.Add(l0); line.tris.Add(l0 + 2); line.tris.Add(l0 + 3);
                }
                if (i % 4000 == 0) yield return null;
            }
            FinalizeMesh(MeshParts.All);
        }
    }

    /// <summary>Redraws the borders when settlements are founded or destroyed; keeps an aligned player with their people.</summary>
    public class WorldComponent_Peoples : WorldComponent
    {
        private int lastSignature;
        public WorldComponent_Peoples(World world) : base(world) { }

        public override void WorldComponentTick()
        {
            if (Find.TickManager.TicksGame % 2000 != 0) return;
            try
            {
                int sig = Find.WorldObjects.Settlements.Count;
                if (sig != lastSignature)
                {
                    lastSignature = sig;
                    Territory.Invalidate();
                    Find.WorldGrid?.Surface?.SetDirty<WorldDrawLayer_Borders>();
                }
                if (Find.TickManager.TicksGame % 60000 == 0) ScenPart_People.Current?.KeepFaith();
            }
            catch (Exception e) { Log.ErrorOnce("[Age of Migrations] peoples: " + e, 81234); }
        }
    }

    // ------------------------------------------------------------------ scenarios: which people the player belongs to

    public class ScenPart_People : ScenPart
    {
        public FactionDef people;

        public static ScenPart_People Current => Find.Scenario?.AllParts.OfType<ScenPart_People>().FirstOrDefault(p => p.people != null);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref people, "people");
        }

        public override string Summary(Scenario scen) =>
            people == null ? null : $"You belong to the {people.label} and begin within their lands.";

        public IEnumerable<Faction> Factions =>
            Find.FactionManager.AllFactions.Where(f => f.def == people && !f.IsPlayer && !f.defeated);

        public override void PostGameStart()
        {
            base.PostGameStart();
            try
            {
                var me = Faction.OfPlayer;
                var own = Factions.ToList();
                if (own.Count == 0) return;
                // the player shares the friends and foes of their people
                foreach (var other in Find.FactionManager.AllFactions)
                {
                    if (other == me || other.Hidden || other.def.permanentEnemy || own.Contains(other)) continue;
                    int target = own[0].GoodwillWith(other);
                    int delta = target - me.GoodwillWith(other);
                    if (delta != 0) me.TryAffectGoodwillWith(other, delta, false, false);
                }
                foreach (var f in own) me.TryAffectGoodwillWith(f, 200, false, false);
            }
            catch (Exception e) { Log.Error("[Age of Migrations] aligning the player with their people failed: " + e); }
        }

        /// <summary>Daily: the bond with your own people does not fade on its own.</summary>
        public void KeepFaith()
        {
            var me = Faction.OfPlayer;
            foreach (var f in Factions)
                if (!f.HostileTo(me) && f.GoodwillWith(me) < 90) me.TryAffectGoodwillWith(f, 5, false, false);
        }
    }

    [HarmonyPatch(typeof(Page_SelectStartingSite), "CanDoNext")]
    public static class Patch_StartInOwnLands
    {
        public static bool Prefix(ref bool __result)
        {
            try
            {
                var part = ScenPart_People.Current;
                if (part == null || !Territory.HasLands(part.people)) return true;
                var tile = Find.WorldSelector.SelectedTile;
                if (!tile.Valid) return true;
                if (Territory.OwnerOf(tile)?.def == part.people) return true;
                Messages.Message($"You belong to the {part.people.label}: choose a site inside their borders on the map.", MessageTypeDefOf.RejectInput, false);
                __result = false;
                return false;
            }
            catch (Exception e) { Log.ErrorOnce("[Age of Migrations] start site check: " + e, 81235); return true; }
        }
    }

    [HarmonyPatch(typeof(TileFinder), nameof(TileFinder.RandomStartingTile))]
    public static class Patch_RandomStartInOwnLands
    {
        public static void Postfix(ref PlanetTile __result)
        {
            try
            {
                var part = ScenPart_People.Current;
                if (part == null) return;
                var owners = Territory.Owners();
                var layer = Find.WorldGrid.Surface;
                var lands = Enumerable.Range(0, owners.Length).Where(i => owners[i] != null && owners[i].def == part.people).ToList();
                if (lands.Count == 0) return;
                lands.Shuffle();
                foreach (int i in lands.Take(400))
                {
                    var t = new PlanetTile(i, layer);
                    if (TileFinder.IsValidTileForNewSettlement(t)) { __result = t; return; }
                }
            }
            catch (Exception e) { Log.ErrorOnce("[Age of Migrations] random start site: " + e, 81236); }
        }
    }

    // ------------------------------------------------------------------ barbarian and Hun raids

    public class RaidPeoples : DefModExtension
    {
        public List<FactionDef> peoples = new List<FactionDef>();
    }

    /// <summary>A raid by one of the listed peoples that is at war with you.</summary>
    public class IncidentWorker_PeopleRaid : IncidentWorker_RaidEnemy
    {
        private IEnumerable<Faction> Candidates(IncidentParms parms)
        {
            var ext = def.GetModExtension<RaidPeoples>();
            if (ext == null) return Enumerable.Empty<Faction>();
            return Find.FactionManager.AllFactions.Where(f => ext.peoples.Contains(f.def) && !f.defeated && !f.IsPlayer &&
                                                              f.HostileTo(Faction.OfPlayer) && FactionCanBeGroupSource(f, parms));
        }

        protected override bool CanFireNowSub(IncidentParms parms) => base.CanFireNowSub(parms) && Candidates(parms).Any();

        protected override bool TryResolveRaidFaction(IncidentParms parms)
        {
            var ext = def.GetModExtension<RaidPeoples>();
            if (parms.faction != null && ext != null && ext.peoples.Contains(parms.faction.def)) return true;
            if (!Candidates(parms).TryRandomElement(out var f)) return false;
            parms.faction = f;
            return true;
        }

        protected override string GetLetterLabel(IncidentParms parms) =>
            def.letterLabel.NullOrEmpty() ? base.GetLetterLabel(parms) : def.letterLabel;

        protected override string GetLetterText(IncidentParms parms, List<Pawn> pawns)
        {
            string b = base.GetLetterText(parms, pawns);
            if (def.letterText.NullOrEmpty() || parms.faction == null) return b;
            return def.letterText.Replace("{FACTION}", parms.faction.Name) + "\n\n" + b;
        }
    }

    // ------------------------------------------------------------------ Realism: no Anomaly, no insect hives, no ancient shrines

    [HarmonyPatch(typeof(Difficulty), nameof(Difficulty.AnomalyPlaystyleDef), MethodType.Getter)]
    public static class Patch_NoAnomaly
    {
        private static AnomalyPlaystyleDef disabled;

        public static void Postfix(ref AnomalyPlaystyleDef __result)
        {
            if (!(AoMMod.Settings?.removeAnomaly ?? false) || !ModsConfig.AnomalyActive) return;
            disabled = disabled ?? DefDatabase<AnomalyPlaystyleDef>.GetNamedSilentFail("Disabled");
            if (disabled != null) __result = disabled;
        }
    }

    [StaticConstructorOnStartup]
    public static class RealismMapGen
    {
        private static readonly HashSet<Type> Skip = new HashSet<Type>();

        static RealismMapGen()
        {
            try
            {
                if (!(AoMMod.Settings?.removeSciFiEvents ?? false)) return;
                var harmony = new Harmony("syneezvetik.ageofmigrations.realismmapgen");
                var patched = new HashSet<System.Reflection.MethodBase>();
                foreach (var name in new[] { "RimWorld.GenStep_ScatterShrines", "RimWorld.GenStep_CaveHives" })
                {
                    var t = AccessTools.TypeByName(name);
                    var m = t == null ? null : AccessTools.Method(t, "Generate", new[] { typeof(Map), typeof(GenStepParams) });
                    if (m == null) continue;
                    if (m.DeclaringType != null && m.DeclaringType != t)   // inherited: Harmony patches the declaring method
                        m = AccessTools.DeclaredMethod(m.DeclaringType, "Generate", new[] { typeof(Map), typeof(GenStepParams) }) ?? m;
                    Skip.Add(t);
                    if (patched.Add(m)) harmony.Patch(m, prefix: new HarmonyMethod(typeof(RealismMapGen), nameof(Prefix)));
                }
            }
            catch (Exception e) { Log.Error("[Age of Migrations] realism map generation: " + e); }
        }

        public static bool Prefix(GenStep __instance) => !Skip.Contains(__instance.GetType());
    }
}
