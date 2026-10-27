using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AgeOfMigrations
{
    /// <summary>
    /// The accurate map: the generated part of the planet becomes Europe, North Africa and the Near East as they really are
    /// (land and sea, mountains, climate, rivers), the world's coordinates become real latitudes and longitudes (so the
    /// seasons are right), every people starts where it was in 395 AD, every city stands where it really stood, and the
    /// seas, mountains and lands carry their Latin names.
    /// The real region (centred on the Black Sea coast of Thrace, 32 degrees in every direction) is stretched over whatever part of the
    /// planet is generated, so it works with any planet coverage; 30% gives a good level of detail.
    /// </summary>
    public static class EarthMap
    {
        public const float CenterLat = 43f, CenterLon = 26f, RealRadius = 32f;

        public static bool Active;                 // this world is an accurate map
        private static bool guard;

        // ---------------------------------------------------------------- Earth data
        private static int W, H;
        private static float Lon0, Lat0, Step;
        private static byte[] climate, elevation, rugged;
        private static List<KeyValuePair<int, List<Vector2>>> rivers;

        public static bool LoadData()
        {
            if (climate != null) return true;
            try
            {
                string dir = Path.Combine(AoMMod.Instance.Content.RootDir, "Earth");
                using (var r = new BinaryReader(File.OpenRead(Path.Combine(dir, "earth.bin"))))
                {
                    r.ReadBytes(8);
                    W = r.ReadInt32(); H = r.ReadInt32(); Lon0 = r.ReadSingle(); Lat0 = r.ReadSingle(); Step = r.ReadSingle();
                    climate = r.ReadBytes(W * H); elevation = r.ReadBytes(W * H); rugged = r.ReadBytes(W * H);
                }
                rivers = new List<KeyValuePair<int, List<Vector2>>>();
                foreach (string line in File.ReadAllLines(Path.Combine(dir, "rivers.txt")))
                {
                    string[] p = line.Split(' ');
                    if (p.Length < 3) continue;
                    var pts = new List<Vector2>(p.Length - 1);
                    for (int i = 1; i < p.Length; i++)
                    {
                        string[] xy = p[i].Split(',');
                        pts.Add(new Vector2(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture),
                                            float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    rivers.Add(new KeyValuePair<int, List<Vector2>>(int.Parse(p[0]), pts));
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[Age of Migrations] could not read the Earth data, falling back to a random world: " + e);
                climate = null;
                return false;
            }
        }

        private static int Cell(float lat, float lon)
        {
            int x = Mathf.Clamp((int)((lon - Lon0) / Step), 0, W - 1);
            int y = Mathf.Clamp((int)((Lat0 - lat) / Step), 0, H - 1);
            return y * W + x;
        }

        public static int ClimateAt(float lat, float lon) => climate[Cell(lat, lon)];
        public static float ElevationAt(float lat, float lon) => elevation[Cell(lat, lon)] * 25f;
        public static float RuggedAt(float lat, float lon) => rugged[Cell(lat, lon)];

        // ---------------------------------------------------------------- mapping world coordinates to real ones
        private class LayerMap { public float cLon, cLat, scale; }
        private static readonly Dictionary<int, LayerMap> maps = new Dictionary<int, LayerMap>();

        public static void ClearCache() => maps.Clear();

        private static LayerMap MapFor(PlanetLayer layer)
        {
            if (maps.TryGetValue(layer.LayerID, out LayerMap m)) return m;
            guard = true;
            try
            {
                Vector3 vc = layer.ViewCenter.normalized;
                int best = 0; float bestDot = -2f;
                for (int i = 0; i < layer.TilesCount; i++)
                {
                    float d = Vector3.Dot(layer.GetTileCenter(i).normalized, vc);
                    if (d > bestDot) { bestDot = d; best = i; }
                }
                Vector2 c = layer.LongLatOf(best);
                float maxDist = 1f;
                for (int i = 0; i < layer.TilesCount; i++)
                {
                    Vector2 ll = layer.LongLatOf(i);
                    maxDist = Mathf.Max(maxDist, AngularDistance(c.y, c.x, ll.y, ll.x));
                }
                m = new LayerMap { cLon = c.x, cLat = c.y, scale = RealRadius / maxDist };
                maps[layer.LayerID] = m;
                return m;
            }
            finally { guard = false; }
        }

        private static float AngularDistance(float lat1, float lon1, float lat2, float lon2)
        {
            float p1 = lat1 * Mathf.Deg2Rad, p2 = lat2 * Mathf.Deg2Rad, dl = (lon2 - lon1) * Mathf.Deg2Rad;
            float a = Mathf.Sin(p1) * Mathf.Sin(p2) + Mathf.Cos(p1) * Mathf.Cos(p2) * Mathf.Cos(dl);
            return Mathf.Acos(Mathf.Clamp(a, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>World long/lat (as the game computes it) to real long/lat (azimuthal equidistant around the real centre).</summary>
        public static Vector2 ToReal(PlanetLayer layer, Vector2 world)
        {
            LayerMap m = MapFor(layer);
            float p0 = m.cLat * Mathf.Deg2Rad, p1 = world.y * Mathf.Deg2Rad, dl = (world.x - m.cLon) * Mathf.Deg2Rad;
            float d = Mathf.Acos(Mathf.Clamp(Mathf.Sin(p0) * Mathf.Sin(p1) + Mathf.Cos(p0) * Mathf.Cos(p1) * Mathf.Cos(dl), -1f, 1f));
            float brg = Mathf.Atan2(Mathf.Sin(dl) * Mathf.Cos(p1), Mathf.Cos(p0) * Mathf.Sin(p1) - Mathf.Sin(p0) * Mathf.Cos(p1) * Mathf.Cos(dl));
            float D = d * m.scale;
            float q0 = CenterLat * Mathf.Deg2Rad;
            float lat = Mathf.Asin(Mathf.Clamp(Mathf.Sin(q0) * Mathf.Cos(D) + Mathf.Cos(q0) * Mathf.Sin(D) * Mathf.Cos(brg), -1f, 1f));
            float lon = CenterLon * Mathf.Deg2Rad + Mathf.Atan2(Mathf.Sin(brg) * Mathf.Sin(D) * Mathf.Cos(q0), Mathf.Cos(D) - Mathf.Sin(q0) * Mathf.Sin(lat));
            return new Vector2(Mathf.Repeat(lon * Mathf.Rad2Deg + 180f, 360f) - 180f, lat * Mathf.Rad2Deg);
        }

        public static bool Applies(PlanetLayer layer) => Active && !guard && layer != null && layer.IsRootSurface;

        public static Vector2 RealLongLat(PlanetLayer layer, int tile) => layer.LongLatOf(tile);   // patched: already real

        // ---------------------------------------------------------------- terrain of one tile
        private static BiomeDef B(string name) => DefDatabase<BiomeDef>.GetNamedSilentFail(name);

        private static readonly float[] BaseTemp = { 0, 26, 26, 26, 25, 21, 11, 24, 13, 15, 10, 6, 16, 13, 8, 18, 15, 8, 8, 5, -1, -10, 10, 6, 0, -8, 6, 3, -3, -10, -25, -6, 0 };
        private static readonly float[] BaseRain = { 0, 2500, 2200, 1200, 1100, 380, 330, 60, 130, 1100, 850, 1100, 600, 800, 900, 1100, 900, 800, 650, 600, 450, 300, 500, 500, 450, 400, 450, 450, 400, 350, 100, 350, 0 };

        private static bool InBox(float lat, float lon, float la0, float la1, float lo0, float lo1) => lat >= la0 && lat <= la1 && lon >= lo0 && lon <= lo1;

        public static void ApplyTerrain(Tile t, float lat, float lon)
        {
            int k = ClimateAt(lat, lon);
            if (k == 32 || k == 0)
            {
                t.elevation = -400f;
                t.hilliness = Hilliness.Flat;
                t.temperature = Mathf.Lerp(26f, -2f, Mathf.InverseLerp(10f, 75f, Mathf.Abs(lat)));
                t.rainfall = 0f;
                t.swampiness = 0f;
                t.PrimaryBiome = BiomeDefOf.Ocean;
                return;
            }
            float e = ElevationAt(lat, lon), r = RuggedAt(lat, lon);
            t.elevation = Mathf.Max(5f, e);
            Hilliness h = r < 28 ? Hilliness.Flat : r < 62 ? Hilliness.SmallHills : r < 110 ? Hilliness.LargeHills : Hilliness.Mountainous;
            if (r > 150 && e > 2600) h = Hilliness.Impassable;
            if (e > 1800 && h < Hilliness.SmallHills) h = Hilliness.SmallHills;
            t.hilliness = h;
            float temp = BaseTemp[k] - 0.006f * Mathf.Max(0f, e - 250f);
            if (k >= 9) temp += (42f - Mathf.Abs(lat)) * 0.15f;
            t.temperature = temp;
            t.rainfall = BaseRain[k];
            t.swampiness = 0f;

            BiomeDef biome;
            // the Pontic and Caspian steppe: dry steppe, hot-summer continental, and the southern forest-steppe
            bool steppeBelt = lon > 27f && lon < 80f && lat > 42f && (k == 6 || k == 18 || (k == 19 && lat < 48.5f));
            switch (k)
            {
                case 1: case 2: biome = B("TropicalRainforest"); break;
                case 3: case 4: case 5: biome = B("AridShrubland"); break;
                case 6: biome = (lat > 40f ? B("Grasslands") : null) ?? B("AridShrubland"); break;
                case 7:
                    bool inner = InBox(lat, lon, 18f, 29f, -6f, 30f) || InBox(lat, lon, 17f, 23f, 45f, 56f);
                    biome = inner && e < 1200f && h <= Hilliness.SmallHills ? (B("ExtremeDesert") ?? B("Desert")) : B("Desert");
                    break;
                case 8: biome = B("Desert"); break;
                case 20: case 21: case 24: case 25: case 28: case 29: biome = B("BorealForest"); break;
                case 30: biome = B("IceSheet"); break;
                case 31: biome = B("Tundra"); break;
                default: biome = B("TemperateForest"); break;
            }
            if (steppeBelt && B("Grasslands") != null)
                biome = B("Grasslands");
            // the great marshes: Polesia, the Danube delta, Colchis, the Po delta and the marshes of lower Mesopotamia
            bool marsh = InBox(lat, lon, 51.2f, 52.6f, 24f, 30.5f) || InBox(lat, lon, 44.8f, 45.4f, 28.7f, 29.7f) ||
                         InBox(lat, lon, 41.9f, 42.5f, 41.6f, 42.3f) || InBox(lat, lon, 44.8f, 45.2f, 11.9f, 12.6f);
            bool hotMarsh = InBox(lat, lon, 30.6f, 31.9f, 46.2f, 47.9f);
            if ((marsh || hotMarsh) && h <= Hilliness.SmallHills)
            {
                t.swampiness = 0.8f;
                biome = (hotMarsh ? B("TropicalSwamp") : B("TemperateSwamp")) ?? biome;
            }
            t.PrimaryBiome = biome ?? BiomeDefOf.TemperateForest;
        }

        // ---------------------------------------------------------------- rivers
        public static void GenerateRivers(PlanetLayer layer)
        {
            var index = new RealIndex(layer);
            RiverDef[] defs = { DefDatabase<RiverDef>.GetNamedSilentFail("Creek"), DefDatabase<RiverDef>.GetNamedSilentFail("River"),
                                DefDatabase<RiverDef>.GetNamedSilentFail("LargeRiver"), DefDatabase<RiverDef>.GetNamedSilentFail("HugeRiver") };
            int links = 0;
            var neigh = new List<PlanetTile>();
            foreach (var kv in rivers)
            {
                RiverDef def = defs[Mathf.Clamp(kv.Key, 0, 3)] ?? defs.FirstOrDefault(d => d != null);
                if (def == null) continue;
                int prev = -1;
                foreach (Vector2 p in kv.Value)
                {
                    int cur = index.Nearest(p.y, p.x, 1.0f);
                    if (cur < 0) { prev = -1; continue; }
                    if (prev >= 0 && cur != prev)
                    {
                        // walk from prev to cur over neighbouring tiles
                        int a = prev, guardSteps = 0;
                        Vector3 target = layer.GetTileCenter(cur);
                        while (a != cur && guardSteps++ < 40)
                        {
                            layer.GetTileNeighbors(a, neigh);
                            int next = -1; float bd = float.MaxValue;
                            foreach (PlanetTile n in neigh)
                            {
                                float d = (layer.GetTileCenter(n.tileId) - target).sqrMagnitude;
                                if (d < bd) { bd = d; next = n.tileId; }
                            }
                            if (next < 0) break;
                            if (layer[a].WaterCovered) break;
                            Find.WorldGrid.OverlayRiver(new PlanetTile(a, layer), new PlanetTile(next, layer), def);
                            links++;
                            a = next;
                        }
                    }
                    prev = cur;
                    if (layer[cur].WaterCovered) prev = -1;
                }
            }
            Log.Message($"[Age of Migrations] accurate map: {links} river links.");
        }

        /// <summary>Finds the tile at a real latitude and longitude.</summary>
        public class RealIndex
        {
            private readonly Dictionary<int, List<int>> cells = new Dictionary<int, List<int>>();
            private readonly Vector2[] real;
            private const float C = 0.5f;
            public RealIndex(PlanetLayer layer)
            {
                real = new Vector2[layer.TilesCount];
                for (int i = 0; i < layer.TilesCount; i++)
                {
                    Vector2 ll = layer.LongLatOf(i);
                    real[i] = ll;
                    int key = Key(ll.y, ll.x);
                    if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<int>();
                    l.Add(i);
                }
            }
            private static int Key(float lat, float lon) => Mathf.FloorToInt((lat + 90f) / C) * 10000 + Mathf.FloorToInt((lon + 180f) / C);
            public Vector2 Real(int i) => real[i];
            public int Nearest(float lat, float lon, float maxDeg, Predicate<int> ok = null)
            {
                int best = -1; float bd = maxDeg * maxDeg;
                int r = Mathf.CeilToInt(maxDeg / C);
                int cy = Mathf.FloorToInt((lat + 90f) / C), cx = Mathf.FloorToInt((lon + 180f) / C);
                float cosl = Mathf.Cos(lat * Mathf.Deg2Rad);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r - 2; dx <= r + 2; dx++)
                    {
                        if (!cells.TryGetValue((cy + dy) * 10000 + cx + dx, out var l)) continue;
                        foreach (int i in l)
                        {
                            float a = real[i].y - lat, b = (real[i].x - lon) * cosl;
                            float d = a * a + b * b;
                            if (d < bd && (ok == null || ok(i))) { bd = d; best = i; }
                        }
                    }
                return best;
            }
        }

        // ---------------------------------------------------------------- the peoples in 395 AD
        // faction, city name (null: a name of the people's own), latitude, longitude
        private static readonly (string f, string name, float lat, float lon)[] Places =
        {
            // Western Empire: Italy, Africa, the Gauls, the Spains, Britain, western Illyricum
            ("LR_WesternEmpire", "Ravenna", 44.42f, 12.20f), ("LR_WesternEmpire", "Roma", 41.89f, 12.49f), ("LR_WesternEmpire", "Mediolanum", 45.46f, 9.19f),
            ("LR_WesternEmpire", "Aquileia", 45.77f, 13.37f), ("LR_WesternEmpire", "Capua", 41.08f, 14.25f), ("LR_WesternEmpire", "Syracusae", 37.07f, 15.28f),
            ("LR_WesternEmpire", "Augusta Treverorum", 49.75f, 6.64f), ("LR_WesternEmpire", "Colonia Agrippina", 50.94f, 6.96f), ("LR_WesternEmpire", "Lugdunum", 45.76f, 4.83f),
            ("LR_WesternEmpire", "Arelate", 43.68f, 4.63f), ("LR_WesternEmpire", "Burdigala", 44.84f, -0.58f), ("LR_WesternEmpire", "Lutetia", 48.85f, 2.35f),
            ("LR_WesternEmpire", "Londinium", 51.51f, -0.09f), ("LR_WesternEmpire", "Eboracum", 53.96f, -1.08f), ("LR_WesternEmpire", "Tarraco", 41.12f, 1.25f),
            ("LR_WesternEmpire", "Emerita Augusta", 38.92f, -6.34f), ("LR_WesternEmpire", "Hispalis", 37.39f, -5.98f), ("LR_WesternEmpire", "Carthago Nova", 37.60f, -0.98f),
            ("LR_WesternEmpire", "Carthago", 36.85f, 10.32f), ("LR_WesternEmpire", "Caesarea Mauretaniae", 36.60f, 2.20f), ("LR_WesternEmpire", "Lepcis Magna", 32.64f, 14.29f),
            ("LR_WesternEmpire", "Sirmium", 44.97f, 19.61f), ("LR_WesternEmpire", "Salona", 43.54f, 16.48f), ("LR_WesternEmpire", "Augusta Vindelicorum", 48.37f, 10.90f),
            ("LR_WesternEmpire", "Carnuntum", 48.11f, 16.86f),
            // Eastern Empire: Thrace, Macedonia and Dacia, Asia, the East, Egypt
            ("LR_EasternEmpire", "Constantinopolis", 41.01f, 28.98f), ("LR_EasternEmpire", "Thessalonica", 40.64f, 22.94f), ("LR_EasternEmpire", "Athenae", 37.98f, 23.73f),
            ("LR_EasternEmpire", "Corinthus", 37.91f, 22.88f), ("LR_EasternEmpire", "Dyrrachium", 41.32f, 19.45f), ("LR_EasternEmpire", "Naissus", 43.32f, 21.90f),
            ("LR_EasternEmpire", "Serdica", 42.70f, 23.32f), ("LR_EasternEmpire", "Hadrianopolis", 41.68f, 26.56f), ("LR_EasternEmpire", "Marcianopolis", 43.20f, 27.60f),
            ("LR_EasternEmpire", "Tomis", 44.17f, 28.65f), ("LR_EasternEmpire", "Nicomedia", 40.77f, 29.92f), ("LR_EasternEmpire", "Ancyra", 39.93f, 32.86f),
            ("LR_EasternEmpire", "Caesarea Mazaca", 38.72f, 35.49f), ("LR_EasternEmpire", "Ephesus", 37.94f, 27.34f), ("LR_EasternEmpire", "Trapezus", 41.00f, 39.72f),
            ("LR_EasternEmpire", "Tarsus", 36.92f, 34.89f), ("LR_EasternEmpire", "Antiochia", 36.20f, 36.16f), ("LR_EasternEmpire", "Edessa", 37.16f, 38.79f),
            ("LR_EasternEmpire", "Amida", 37.91f, 40.24f), ("LR_EasternEmpire", "Damascus", 33.51f, 36.29f), ("LR_EasternEmpire", "Tyrus", 33.27f, 35.20f),
            ("LR_EasternEmpire", "Hierosolyma", 31.78f, 35.23f), ("LR_EasternEmpire", "Bostra", 32.52f, 36.48f), ("LR_EasternEmpire", "Alexandria", 31.20f, 29.92f),
            ("LR_EasternEmpire", "Pelusium", 31.04f, 32.55f), ("LR_EasternEmpire", "Antinoopolis", 27.81f, 30.88f), ("LR_EasternEmpire", "Cyrene", 32.82f, 21.86f),
            // Sassanid Empire
            ("LR_Sassanids", "Ctesiphon", 33.09f, 44.58f), ("LR_Sassanids", "Ecbatana", 34.80f, 48.52f), ("LR_Sassanids", "Istakhr", 29.98f, 52.90f),
            ("LR_Sassanids", "Bishapur", 29.78f, 51.57f), ("LR_Sassanids", "Gundeshapur", 32.28f, 48.52f), ("LR_Sassanids", "Nisibis", 37.07f, 41.22f),
            ("LR_Sassanids", "Rhagae", 35.59f, 51.43f), ("LR_Sassanids", "Merv", 37.66f, 62.19f), ("LR_Sassanids", "Nishapur", 36.21f, 58.80f),
            ("LR_Sassanids", "Ganzak", 36.60f, 47.23f), ("LR_Sassanids", "Arbela", 36.19f, 44.01f), ("LR_Sassanids", "Susa", 32.19f, 48.25f),
            ("LR_Sassanids", "Ardashir-Khwarrah", 28.85f, 52.53f), ("LR_Sassanids", "Spahan", 32.65f, 51.67f), ("LR_Sassanids", "Dvin", 40.00f, 44.58f),
            ("LR_Sassanids", "Hara", 34.35f, 62.20f), ("LR_Sassanids", "Gurgan", 37.30f, 54.90f), ("LR_Sassanids", "Kirman", 30.28f, 57.08f),
            // Visigoths: federates settled between the Danube and the Haemus, in arms under Alaric
            ("LR_Visigoths", null, 43.62f, 25.35f), ("LR_Visigoths", null, 43.70f, 24.47f), ("LR_Visigoths", null, 43.20f, 25.60f), ("LR_Visigoths", null, 43.82f, 22.90f),
            ("LR_Visigoths", null, 44.10f, 27.20f),
            // Ostrogoths: Greuthungi under Hunnic rule, north of the Black Sea and on the middle Danube
            ("LR_Ostrogoths", null, 47.00f, 33.00f), ("LR_Ostrogoths", null, 46.80f, 30.40f), ("LR_Ostrogoths", null, 48.00f, 35.00f), ("LR_Ostrogoths", null, 47.50f, 28.50f),
            // Huns: the Pontic and Caspian steppe
            ("LR_Huns", null, 47.50f, 40.00f), ("LR_Huns", null, 46.50f, 44.00f), ("LR_Huns", null, 48.50f, 45.50f), ("LR_Huns", null, 47.00f, 37.50f),
            ("LR_Huns", null, 49.50f, 42.00f), ("LR_Huns", null, 46.20f, 47.50f),
            // Vandals: Silingi in Silesia, Hasdingi on the upper Tisza
            ("LR_Vandals", null, 51.00f, 17.00f), ("LR_Vandals", null, 50.50f, 16.00f), ("LR_Vandals", null, 51.50f, 15.50f), ("LR_Vandals", null, 48.20f, 21.80f),
            ("LR_Vandals", null, 48.70f, 22.50f),
            // Saxons: between the Elbe and the Weser, and in Holstein
            ("LR_Saxons", null, 53.50f, 9.50f), ("LR_Saxons", null, 53.00f, 8.60f), ("LR_Saxons", null, 54.30f, 9.80f), ("LR_Saxons", null, 52.70f, 10.50f),
            // Franks: Salians in Toxandria, Ripuarians on the right bank of the Rhine
            ("LR_Franks", null, 51.45f, 5.30f), ("LR_Franks", null, 51.90f, 6.30f), ("LR_Franks", null, 51.30f, 7.40f), ("LR_Franks", null, 50.80f, 7.70f),
        };

        public static void PlacePeoples(PlanetLayer layer)
        {
            var index = new RealIndex(layer);
            var byDef = Find.FactionManager.AllFactionsListForReading.GroupBy(f => f.def.defName).ToDictionary(g => g.Key, g => g.First());
            var ours = new HashSet<string>(Places.Select(p => p.f));
            foreach (Settlement s in Find.WorldObjects.Settlements.ToList())
                if (s.Faction != null && ours.Contains(s.Faction.def.defName) && s.Tile.Layer == layer)
                    Find.WorldObjects.Remove(s);
            int placed = 0;
            var used = new HashSet<int>();
            foreach (var p in Places)
            {
                if (!byDef.TryGetValue(p.f, out Faction faction)) continue;
                int tile = index.Nearest(p.lat, p.lon, 2.5f, i =>
                    !used.Contains(i) && !Find.WorldObjects.AnyWorldObjectAt(new PlanetTile(i, layer)) &&
                    TileFinder.IsValidTileForNewSettlement(new PlanetTile(i, layer)));
                if (tile < 0) continue;
                used.Add(tile);
                var s = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                s.SetFaction(faction);
                s.Tile = new PlanetTile(tile, layer);
                s.Name = p.name ?? NameGenerator.GenerateName(faction.def.settlementNameMaker, Find.WorldObjects.Settlements.Select(x => x.Name).Where(x => x != null));
                Find.WorldObjects.Add(s);
                placed++;
            }
            Log.Message($"[Age of Migrations] accurate map: placed {placed} settlements where they stood in 395.");
        }

        // ---------------------------------------------------------------- Latin names for the map labels
        private static readonly (string name, int kind, float lat, float lon)[] Names =
        {   // kind 0 water, 1 mountains, 2 land
            ("Mare Internum", 0, 35.5f, 18f), ("Pontus Euxinus", 0, 43.2f, 34f), ("Mare Caspium", 0, 42f, 50.5f), ("Oceanus Atlanticus", 0, 42f, -20f),
            ("Oceanus Germanicus", 0, 56f, 3f), ("Mare Suebicum", 0, 57f, 19f), ("Sinus Persicus", 0, 27f, 51f), ("Sinus Arabicus", 0, 20f, 38.5f),
            ("Mare Aegaeum", 0, 38.5f, 25f), ("Mare Adriaticum", 0, 43f, 15f), ("Mare Tyrrhenum", 0, 40f, 12f), ("Pontus Cimmerius", 0, 46f, 36.5f),
            ("Mare Hibernicum", 0, 53.5f, -5f), ("Oceanus Indicus", 0, 15f, 60f), ("Mare Aegyptium", 0, 33f, 29f), ("Mare Ionium", 0, 37f, 18.5f),
            ("Mare Balearicum", 0, 39.5f, 3f), ("Mare Aralis", 0, 45f, 60f), ("Oceanus Septentrionalis", 0, 70f, 20f),
            ("Alpes", 1, 46.4f, 9f), ("Pyrenaei Montes", 1, 42.7f, 0.5f), ("Montes Carpates", 1, 47.5f, 24.5f), ("Caucasus", 1, 42.8f, 44f),
            ("Taurus", 1, 37.3f, 34f), ("Zagros", 1, 33f, 48.5f), ("Haemus", 1, 42.8f, 24.5f), ("Atlas", 1, 31.5f, -6f), ("Apenninus", 1, 42.5f, 13f),
            ("Montes Dinarici", 1, 43.8f, 17.5f), ("Elburz", 1, 36.2f, 52f), ("Montes Sarmatici", 1, 62f, 59f), ("Montes Scandinavi", 1, 63f, 13f),
            ("Hindu Kush", 1, 35.5f, 70f), ("Montes Armeniae", 1, 39.5f, 42.5f),
            ("Europa", 2, 50f, 15f), ("Africa", 2, 20f, 15f), ("Asia", 2, 45f, 70f), ("Britannia", 2, 53f, -2f), ("Hibernia", 2, 53.3f, -8f),
            ("Italia", 2, 42.5f, 13f), ("Hispania", 2, 40f, -4f), ("Gallia", 2, 46.5f, 2.5f), ("Germania", 2, 51.5f, 10f), ("Scandza", 2, 62f, 15f),
            ("Sarmatia", 2, 52f, 35f), ("Scythia", 2, 48f, 55f), ("Dacia", 2, 46f, 24.5f), ("Thracia", 2, 42f, 26f), ("Graecia", 2, 39f, 22f),
            ("Asia Minor", 2, 39f, 32f), ("Syria", 2, 34.5f, 37.5f), ("Arabia", 2, 23f, 45f), ("Aegyptus", 2, 26.5f, 30f), ("Libya", 2, 27f, 20f),
            ("Mauretania", 2, 32f, -3f), ("Mesopotamia", 2, 34.5f, 42f), ("Persis", 2, 30f, 53f), ("Media", 2, 35.5f, 48f), ("Armenia", 2, 40f, 44f),
            ("Sicilia", 2, 37.5f, 14f), ("Sardinia", 2, 40f, 9f), ("Corsica", 2, 42.2f, 9.1f), ("Creta", 2, 35.2f, 24.9f), ("Cyprus", 2, 35f, 33.2f),
            ("Sahara", 2, 23f, 5f), ("Chorasan", 2, 36f, 60f), ("Pannonia", 2, 46.8f, 18.5f), ("Islandia", 2, 64.9f, -18.5f),
        };

        public static void RenameFeatures(PlanetLayer layer)
        {
            var feats = Find.WorldFeatures?.features;
            if (feats == null) return;
            var taken = new HashSet<string>();
            foreach (WorldFeature f in feats.OrderByDescending(f => f.Tiles.Count()))
            {
                if (f.layer != null && f.layer != layer) continue;
                var tiles = f.Tiles.ToList();
                if (tiles.Count == 0) continue;
                bool water = layer[tiles[0]].WaterCovered;
                float mountains = tiles.Count(t => layer[t].hilliness >= Hilliness.Mountainous) / (float)tiles.Count;
                int kind = water ? 0 : mountains > 0.3f ? 1 : 2;
                float sx = 0, sy = 0, sz = 0;
                foreach (int t in tiles) { Vector3 v = layer.GetTileCenter(t).normalized; sx += v.x; sy += v.y; sz += v.z; }
                Vector3 c = new Vector3(sx, sy, sz).normalized * layer.GetTileCenter(tiles[0]).magnitude;
                int nearest = 0; float nd = float.MaxValue;
                for (int t = 0; t < tiles.Count; t += Math.Max(1, tiles.Count / 400))
                {
                    float d = (layer.GetTileCenter(tiles[t]) - c).sqrMagnitude;
                    if (d < nd) { nd = d; nearest = tiles[t]; }
                }
                Vector2 rl = layer.LongLatOf(nearest);
                string best = null; float bd = 14f * 14f;
                foreach (var n in Names)
                {
                    if (n.kind != kind || taken.Contains(n.name)) continue;
                    float a = n.lat - rl.y, b = (n.lon - rl.x) * Mathf.Cos(rl.y * Mathf.Deg2Rad);
                    float d = a * a + b * b;
                    if (d < bd) { bd = d; best = n.name; }
                }
                if (best != null) { f.name = best; taken.Add(best); }
            }
            Find.WorldFeatures.textsCreated = false;
        }
    }

    // ---------------------------------------------------------------- remembers that a world is an accurate map
    public class WorldComponent_EarthMap : WorldComponent
    {
        private bool active;
        public WorldComponent_EarthMap(World world) : base(world) { active = EarthMap.Active; }
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) active = EarthMap.Active;
            Scribe_Values.Look(ref active, "accurateEarth", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars) { EarthMap.Active = active; EarthMap.ClearCache(); }
        }
    }

    // ---------------------------------------------------------------- hooks into world generation

    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld))]
    public static class Patch_GenerateWorld
    {
        public static void Prefix()
        {
            EarthMap.ClearCache();
            EarthMap.Active = (AoMMod.Settings?.accurateEarth ?? false) && EarthMap.LoadData();
        }
    }

    [HarmonyPatch]
    public static class Patch_LoadGame_ResetEarth
    {
        // a save without the component is not an accurate map
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Game), "LoadGame");
        private static bool Prepare() => TargetMethod() != null;
        public static void Prefix() { EarthMap.Active = false; EarthMap.ClearCache(); }
    }

    [HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.LongLatOf), new[] { typeof(int) })]
    public static class Patch_LongLatOf_Int
    {
        public static void Postfix(PlanetLayer __instance, ref Vector2 __result)
        {
            if (EarthMap.Applies(__instance)) __result = EarthMap.ToReal(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.LongLatOf), new[] { typeof(PlanetTile) })]
    public static class Patch_LongLatOf_Tile
    {
        private static bool inner;
        public static void Postfix(PlanetLayer __instance, PlanetTile tile, ref Vector2 __result)
        {
            if (!EarthMap.Applies(__instance)) return;
            // the PlanetTile overload may itself call the int overload (already converted): check by recomputing
            if (inner) return;
            inner = true;
            try
            {
                Vector2 viaInt = __instance.LongLatOf(tile.tileId);
                __result = viaInt;
            }
            finally { inner = false; }
        }
    }

    [HarmonyPatch(typeof(WorldGenStep_Terrain), "GenerateTileFor")]
    public static class Patch_TerrainTile
    {
        public static void Postfix(PlanetTile tile, PlanetLayer layer, Tile __result)
        {
            if (__result == null || !EarthMap.Applies(layer)) return;
            Vector2 ll = layer.LongLatOf(tile.tileId);
            EarthMap.ApplyTerrain(__result, ll.y, ll.x);
        }
    }

    [HarmonyPatch(typeof(WorldGenStep_Rivers), nameof(WorldGenStep_Rivers.GenerateFresh))]
    public static class Patch_Rivers
    {
        public static bool Prefix(PlanetLayer layer)
        {
            if (!EarthMap.Applies(layer)) return true;
            try { EarthMap.GenerateRivers(layer); }
            catch (Exception e) { Log.Error("[Age of Migrations] accurate rivers failed: " + e); }
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGenStep_Factions), nameof(WorldGenStep_Factions.GenerateFresh))]
    public static class Patch_Factions
    {
        public static void Postfix(PlanetLayer layer)
        {
            if (!EarthMap.Applies(layer)) return;
            try { EarthMap.PlacePeoples(layer); }
            catch (Exception e) { Log.Error("[Age of Migrations] placing the peoples failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(WorldGenStep_Features), nameof(WorldGenStep_Features.GenerateFresh))]
    public static class Patch_Features
    {
        public static void Postfix(PlanetLayer layer)
        {
            if (!EarthMap.Applies(layer)) return;
            try { EarthMap.RenameFeatures(layer); }
            catch (Exception e) { Log.Error("[Age of Migrations] naming the map failed: " + e); }
        }
    }
}
