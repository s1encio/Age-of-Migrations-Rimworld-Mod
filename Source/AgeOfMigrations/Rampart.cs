using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace AgeOfMigrations
{
    /// <summary>
    /// A tall wall face, drawn in the cell south of the wall (the side the camera looks at), so that a wall looks
    /// as high as it is: {facePath}_Single / _Left / _Right / _Mid, picked by the neighbours that also show a face.
    /// Pawns in that cell are drawn in front of the face, as if they stood at the foot of the wall.
    /// </summary>
    public class TallFaceExtension : DefModExtension
    {
        public string facePath;
        public bool tinted = true;      // colour the face with the wall's stuff
        public bool northOnly;          // stairs: only when they rise to the north
        public bool single;             // always use the _Single texture
        public float height = 1f;
    }

    /// <summary>Marks a floor as the top of a wall: who stands on it shoots better and is harder to hit.</summary>
    public class WallWalkExtension : DefModExtension
    {
        public float missFactor = 0.7f;        // per-cell miss chance of shooters on the walls is multiplied by this
        public float hitFactor = 0.8f;         // chance to hit someone standing on the walls is multiplied by this
    }

    public static class WallWalk
    {
        public static WallWalkExtension At(IntVec3 c, Map map)
        {
            if (map == null || !c.InBounds(map))
                return null;
            return c.GetTerrain(map)?.GetModExtension<WallWalkExtension>();
        }

        public static WallWalkExtension Of(Thing t) => t is Pawn p && p.Spawned ? At(p.Position, p.Map) : null;
    }

    public class Building_Rampart : Building
    {
        public override void Print(SectionLayer layer)
        {
            base.Print(layer);
            try { PrintFace(layer); }
            catch (Exception e) { Log.ErrorOnce("[Age of Migrations] Could not draw wall face: " + e.Message, 4826113); }
        }

        private void PrintFace(SectionLayer layer)
        {
            TallFaceExtension ext = def.GetModExtension<TallFaceExtension>();
            if (ext == null || ext.facePath.NullOrEmpty() || Map == null)
                return;
            if (ext.northOnly && Rotation != Rot4.North)
                return;
            IntVec3 below = Position + IntVec3.South;
            if (!ShowsFace(Position, Map, def))
                return;
            string variant = "Single";
            if (!ext.single)
            {
                bool w = SameFaceAt(Position + IntVec3.West), e = SameFaceAt(Position + IntVec3.East);
                variant = w && e ? "Mid" : w ? "Right" : e ? "Left" : "Single";
            }
            Material mat = MaterialPool.MatFrom(ext.facePath + "_" + variant, ShaderDatabase.Cutout, ext.tinted ? DrawColor : Color.white);
            Vector3 pos = below.ToVector3Shifted();
            pos.y = AltitudeLayer.Building.AltitudeFor() + 0.01f;
            pos.z += (1f - ext.height) / 2f;
            Printer_Plane.PrintPlane(layer, pos, new Vector2(1f, ext.height), mat);
        }

        private bool SameFaceAt(IntVec3 c)
        {
            if (!c.InBounds(Map))
                return false;
            Building_Rampart other = c.GetFirstThing<Building_Rampart>(Map);
            if (other == null)
                return false;
            TallFaceExtension a = def.GetModExtension<TallFaceExtension>(), b = other.def.GetModExtension<TallFaceExtension>();
            return a != null && b != null && a.facePath == b.facePath && ShowsFace(c, Map, other.def);
        }

        /// <summary>A wall at c shows its face when the cell south of it is open ground: not more wall, a gate, the wall walk or stairs.</summary>
        public static bool ShowsFace(IntVec3 c, Map map, ThingDef wallDef)
        {
            IntVec3 below = c + IntVec3.South;
            if (!below.InBounds(map))
                return false;
            if (WallWalk.At(below, map) != null)
                return false;
            List<Thing> things = below.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                ThingDef d = things[i].def;
                if (things[i] is Building_Rampart || d.IsDoor || (d.category == ThingCategory.Building && d.passability == Traversability.Impassable))
                    return false;
            }
            return true;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            DirtyAround(map);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            base.DeSpawn(mode);
            if (map != null)
                DirtyAround(map);
        }

        // the faces of the walls around this one depend on it: redraw them
        private void DirtyAround(Map map)
        {
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(this))
                if (c.InBounds(map))
                    map.mapDrawer.MapMeshDirty(c, MapMeshFlagDefOf.Things);
            IntVec3 b = Position + IntVec3.South + IntVec3.South;
            if (b.InBounds(map))
                map.mapDrawer.MapMeshDirty(b, MapMeshFlagDefOf.Things);
        }
    }

    /// <summary>Shooters standing on a wall walk miss less.</summary>
    public class StatPart_OnWalls : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            WallWalkExtension ext = WallWalk.Of(req.Thing);
            if (ext != null)
                val = 1f - (1f - val) * ext.missFactor;
        }

        public override string ExplanationPart(StatRequest req)
        {
            WallWalkExtension ext = WallWalk.Of(req.Thing);
            return ext == null ? null : "Shooting from the walls: misses x" + ext.missFactor.ToStringPercent();
        }
    }

    /// <summary>Someone standing on a wall walk is harder to hit from below.</summary>
    [HarmonyPatch]
    public static class Patch_ShotReport_OnWalls
    {
        private static FieldInfo sizeField;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ShotReport), "HitReportFor");

        private static bool Prepare()
        {
            sizeField = AccessTools.Field(typeof(ShotReport), "factorFromTargetSize");
            return sizeField != null && TargetMethod() != null;
        }

        public static void Postfix(Thing caster, LocalTargetInfo target, ref ShotReport __result)
        {
            try
            {
                if (!(target.Thing is Pawn p))
                    return;
                WallWalkExtension ext = WallWalk.Of(p);
                if (ext == null || (caster != null && WallWalk.Of(caster) != null))
                    return;   // shooting up at the walls is hard; shooting along the walls is not
                object boxed = __result;
                sizeField.SetValue(boxed, (float)sizeField.GetValue(boxed) * ext.hitFactor);
                __result = (ShotReport)boxed;
            }
            catch { }
        }
    }

    /// <summary>
    /// When an enemy settlement is generated, whoever was posted on its wall walk stays there and holds it,
    /// instead of joining the crowd that runs out to meet attackers.
    /// </summary>
    public class MapComponent_WallGarrison : MapComponent
    {
        private bool done;
        private int firstTick = -1;
        private static ConstructorInfo defendPoint;

        public MapComponent_WallGarrison(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref done, "lrWallGarrisonDone");
        }

        public override void MapComponentTick()
        {
            if (done)
                return;
            int now = Find.TickManager.TicksGame;
            if (firstTick < 0) { firstTick = now; return; }
            if (now - firstTick < 90)
                return;
            done = true;
            try { PostGuards(); }
            catch (Exception e) { Log.Warning("[Age of Migrations] Could not post guards on the walls: " + e.Message); }
        }

        private void PostGuards()
        {
            Faction faction = map.ParentFaction;
            if (faction == null || faction == Faction.OfPlayer)
                return;
            int n = 0;
            foreach (Pawn p in map.mapPawns.SpawnedPawnsInFaction(faction).ToList())
            {
                if (!p.RaceProps.Humanlike || p.Downed || WallWalk.At(p.Position, map) == null)
                    continue;
                LordJob job = MakeDefendPoint(p.Position);
                if (job == null)
                    return;
                p.GetLord()?.RemovePawn(p);
                LordMaker.MakeNewLord(faction, job, map, new List<Pawn> { p });
                n++;
            }
            if (n > 0)
                Log.Message($"[Age of Migrations] {n} guards hold the walls of {map.Parent?.LabelCap}.");
        }

        private static LordJob MakeDefendPoint(IntVec3 cell)
        {
            if (defendPoint == null)
                defendPoint = typeof(LordJob_DefendPoint).GetConstructors()
                    .FirstOrDefault(c => c.GetParameters().Length > 0 && c.GetParameters()[0].ParameterType == typeof(IntVec3));
            if (defendPoint == null)
                return null;
            ParameterInfo[] ps = defendPoint.GetParameters();
            object[] args = new object[ps.Length];
            args[0] = cell;
            for (int i = 1; i < ps.Length; i++)
            {
                string n = ps[i].Name ?? "";
                if (n.Contains("wander")) args[i] = (float?)1.5f;
                else if (n.Contains("defend")) args[i] = (float?)8f;
                else args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);
            }
            return (LordJob)defendPoint.Invoke(args);
        }
    }
}
