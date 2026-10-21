using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AgeOfMigrations
{
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
