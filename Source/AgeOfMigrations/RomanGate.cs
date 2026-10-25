using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AgeOfMigrations
{
    /// <summary>Swinging door frames for a gate: {texPath}_0 (closed) .. {texPath}_{frames-1} (open), each 3x1 cells.</summary>
    public class GateDoorsExtension : DefModExtension
    {
        public string texPath;
        public int frames = 6;
        public bool tinted;          // colour the doors with the gate's material (wood); false keeps the texture colours
        public int ticksToSwing = 45;
        public Vector2 size = new Vector2(3f, 1f);
    }

    /// <summary>
    /// A 3-wide gate that only its own faction can open from both sides. Anyone else can only open it from the
    /// inside (to get out); from the outside they have to break it down.
    /// "Inside" is the side enclosed by walls: from there the map edge can't be reached without passing a door.
    /// </summary>
    public class Building_RomanGate : Building_MultiTileDoor
    {
        private const int RecheckTicks = 2500;
        private int cacheTick = -1;
        private bool insideNeg, insidePos;   // is the side towards -x/-z (Neg) or +x/+z (Pos) enclosed?

        // ---- swinging doors (drawn on top of the support graphic, under pawns)
        private float swing = -1f;           // 0 closed .. 1 open
        private int swingTick = -1;
        private Material[] doorMats;
        private Color doorMatsColor;

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            GateDoorsExtension ext = def.GetModExtension<GateDoorsExtension>();
            if (ext == null || ext.frames < 1 || Rotation != Rot4.North)
                return;   // facing into the town from a north, east or west wall, the doors are hidden under the gatehouse roof
            int now = Find.TickManager.TicksGame;
            float target = Open ? 1f : 0f;
            if (swing < 0f || swingTick < 0)
                swing = target;
            else
            {
                float step = (now - swingTick) / (float)Mathf.Max(1, ext.ticksToSwing);
                swing = Mathf.MoveTowards(swing, target, step);
            }
            swingTick = now;
            int frame = Mathf.Clamp(Mathf.RoundToInt(swing * (ext.frames - 1)), 0, ext.frames - 1);
            Color col = ext.tinted ? DrawColor : Color.white;
            if (doorMats == null || doorMatsColor != col)
            {
                doorMats = new Material[ext.frames];
                for (int i = 0; i < ext.frames; i++)
                    doorMats[i] = MaterialPool.MatFrom(ext.texPath + "_" + i, ShaderDatabase.Cutout, col);
                doorMatsColor = col;
            }
            Vector3 pos = drawLoc;
            pos.z -= 1f;      // in the archway of the outer face, one cell south of the door line
            pos.y = AltitudeLayer.DoorMoveable.AltitudeFor();
            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(ext.size.x, 1f, ext.size.y));
            Graphics.DrawMesh(MeshPool.plane10, matrix, doorMats[frame], 0);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                swing = -1f;
                swingTick = -1;
            }
        }

        public override bool PawnCanOpen(Pawn p)
        {
            if (Faction == null || p.Faction == Faction)
                return base.PawnCanOpen(p);
            return FromInside(p);
        }

        private bool FromInside(Pawn p)
        {
            if (!Spawned || p.Map != Map)
                return false;
            UpdateCache();
            // a gate rotated East/West spans north-south, so its two sides lie along x; otherwise along z
            bool sidesAlongX = Rotation.IsHorizontal;
            int d = sidesAlongX ? p.Position.x - Position.x : p.Position.z - Position.z;
            if (d == 0)
                return true;   // already standing in the gateway
            return d < 0 ? insideNeg : insidePos;
        }

        private void UpdateCache()
        {
            int now = Find.TickManager.TicksGame;
            if (cacheTick >= 0 && now - cacheTick < RecheckTicks)
                return;
            cacheTick = now;
            IntVec3 step = Rotation.IsHorizontal ? IntVec3.East : IntVec3.North;
            insideNeg = Enclosed(Position - step, IntVec3.Zero - step);
            insidePos = Enclosed(Position + step, step);
        }

        /// <summary>True if the map edge can't be reached from start without walking through a door.</summary>
        private bool Enclosed(IntVec3 start, IntVec3 outward)
        {
            Map map = Map;
            if (!Passable(start, map))
            {
                start += outward;   // something stands right next to the gate: start one cell further out
                if (!Passable(start, map)) return false;
            }
            int sizeX = map.Size.x, sizeZ = map.Size.z;
            var seen = new bool[map.cellIndices.NumGridCells];
            var queue = new Queue<IntVec3>();
            queue.Enqueue(start);
            seen[map.cellIndices.CellToIndex(start)] = true;
            while (queue.Count > 0)
            {
                IntVec3 c = queue.Dequeue();
                if (c.x == 0 || c.z == 0 || c.x == sizeX - 1 || c.z == sizeZ - 1)
                    return false;   // reached the map edge: this side is outside
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 n = c + GenAdj.CardinalDirections[i];
                    if (!n.InBounds(map)) continue;
                    int idx = map.cellIndices.CellToIndex(n);
                    if (seen[idx]) continue;
                    seen[idx] = true;
                    if (Passable(n, map)) queue.Enqueue(n);
                }
            }
            return true;
        }

        private static bool Passable(IntVec3 c, Map map)
        {
            return c.InBounds(map) && c.Walkable(map) && !(c.GetEdifice(map) is Building_Door);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string rule = "Only its owners can open it from outside; anyone may open it from inside.";
            return string.IsNullOrEmpty(s) ? rule : s + "\n" + rule;
        }
    }
}
