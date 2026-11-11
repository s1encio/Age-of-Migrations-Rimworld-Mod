using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AgeOfMigrations
{
    [StaticConstructorOnStartup]
    public static class TopBarTex
    {
        public static readonly Texture2D Frame = ContentFinder<Texture2D>.Get("UI/LR_TopBar", false);
        public static readonly Texture2D Income = ContentFinder<Texture2D>.Get("UI/LR_IconIncome", false);
        public static readonly Texture2D Year = ContentFinder<Texture2D>.Get("UI/LR_IconYear", false);
        public static readonly Texture2D Bottom = ContentFinder<Texture2D>.Get("UI/LR_BottomBar", false);
        public static readonly Texture2D MapFrame = ContentFinder<Texture2D>.Get("UI/LR_MinimapFrame", false);
        public static readonly Texture2D Parchment = ContentFinder<Texture2D>.Get("UI/LR_Parchment", false);
        public static readonly Texture2D[] Buttons =
        {
            ContentFinder<Texture2D>.Get("UI/LR_Btn_diplomacy", false), ContentFinder<Texture2D>.Get("UI/LR_Btn_research", false),
            ContentFinder<Texture2D>.Get("UI/LR_Btn_world", false), ContentFinder<Texture2D>.Get("UI/LR_Btn_history", false),
        };
    }

    /// <summary>
    /// The Attila-style bar at the top of the screen: treasury in gold coins, the gold gained or lost over the last day,
    /// food in store, and the year. Shown on the colony map and on the world map.
    /// </summary>
    public class GameComponent_TopBar : GameComponent
    {
        private const float W = 640f, H = 54f, Cap = 40f;
        private int lastDay = -1, goldAtDayStart, income;
        private float foodAtDayStart, foodChange;
        private int cachedGold, cacheTick = -999;
        private float cachedFood;

        public GameComponent_TopBar(Game game) { }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lastDay, "lastDay", -1);
            Scribe_Values.Look(ref goldAtDayStart, "goldAtDayStart");
            Scribe_Values.Look(ref income, "income");
            Scribe_Values.Look(ref foodAtDayStart, "foodAtDayStart");
            Scribe_Values.Look(ref foodChange, "foodChange");
        }

        private void Refresh()
        {
            if (Find.TickManager.TicksGame - cacheTick < 120 && cacheTick >= 0) return;
            cacheTick = Find.TickManager.TicksGame;
            int gold = 0; float food = 0f;
            foreach (Map m in Find.Maps)
            {
                if (!m.IsPlayerHome) continue;
                gold += m.resourceCounter.Silver;
                food += m.resourceCounter.TotalHumanEdibleNutrition;
            }
            foreach (Caravan c in Find.WorldObjects.Caravans)
                if (c.IsPlayerControlled) gold += CaravanInventoryUtility.AllInventoryItems(c).Where(t => t.def == ThingDefOf.Silver).Sum(t => t.stackCount);
            cachedGold = gold; cachedFood = food;
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0) return;
            int day = GenDate.DaysPassed;
            if (day == lastDay) return;
            Refresh();
            if (lastDay >= 0) { income = cachedGold - goldAtDayStart; foodChange = cachedFood - foodAtDayStart; }
            lastDay = day; goldAtDayStart = cachedGold; foodAtDayStart = cachedFood;
        }

        public static int YearAD()
        {
            float lon = Find.CurrentMap != null ? Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile).x : 0f;
            return GenDate.Year(Find.TickManager.TicksAbs, lon);   // already counted from 395 AD, see Patch_YearAD
        }

        public override void GameComponentOnGUI()
        {
            try
            {
                if (!(AoMMod.Settings?.showTopBar ?? true) || Find.UIRoot == null || Find.UIRoot.screenshotMode.FiltersCurrentEvent) return;
                if (Current.ProgramState != ProgramState.Playing) return;
                Refresh();
                var r = new Rect((UI.screenWidth - W) / 2f, 0f, W, H);
                if (TopBarTex.Frame != null) GUI.DrawTexture(r, TopBarTex.Frame);
                float x0 = r.x + Cap + 12f, slot = (W - 2 * Cap - 24f) / 4f;
                const float rowH = 34f;

                int colonists = Find.Maps.Where(m => m.IsPlayerHome).Sum(m => m.mapPawns.FreeColonistsCount);
                float perDay = Mathf.Max(1, colonists) * 1.6f;
                string season = Find.CurrentMap != null ? GenLocalDate.Season(Find.CurrentMap).Label() : "";

                Slot(new Rect(x0, 2f, slot, rowH), ThingDefOf.Silver.uiIcon, cachedGold.ToString("N0"), Color.white,
                    $"Treasury: {cachedGold:N0} gold coins in your settlements and caravans.");
                Slot(new Rect(x0 + slot, 2f, slot, rowH), TopBarTex.Income, (income >= 0 ? "+" : "") + income.ToString("N0"),
                    income >= 0 ? new Color(0.55f, 0.85f, 0.45f) : new Color(0.95f, 0.4f, 0.32f),
                    $"Gold gained or lost over the last day: {income:+#,0;-#,0;0}.");
                var hay = DefDatabase<ThingDef>.GetNamedSilentFail("Hay");
                Slot(new Rect(x0 + 2 * slot, 2f, slot, rowH), hay?.uiIcon, cachedFood.ToString("N0"),
                    foodChange < 0 ? new Color(0.95f, 0.75f, 0.4f) : Color.white,
                    $"Food in store: {cachedFood:N0} nutrition, enough for about {cachedFood / perDay:N0} days for {colonists} people.\nChange over the last day: {foodChange:+#,0;-#,0;0}.");
                Slot(new Rect(x0 + 3 * slot, 2f, slot, rowH), TopBarTex.Year, YearAD() + " AD", Color.white,
                    $"The year {YearAD()} after Christ{(season.NullOrEmpty() ? "" : ", " + season)}.");
                BlockClicks(r);
                if (AoMMod.Settings?.showBottomBar ?? true) BottomBar();
                if (AoMMod.Settings?.showMinimap ?? true) Minimap.Draw();
            }
            catch (Exception e) { Log.ErrorOnce("[Age of Migrations] top bar: " + e, 81240); }
        }

        public static void BlockClicks(Rect r)
        {
            if (Event.current.type == EventType.MouseDown && Mouse.IsOver(r)) Event.current.Use();
        }

        private static readonly string[] ButtonTabs = { "Factions", "Research", "World", "History" };
        private static readonly string[] ButtonTips = { "Diplomacy: the peoples of the world and where you stand with them.",
            "Research: the knowledge of your people.", "The world map.", "History: the chronicle of your colony." };
        private static readonly float[] SocketX = { 260f - 172f, 260f - 104f, 260f + 104f, 260f + 172f };

        /// <summary>The bar above the vanilla tabs: your faction's emblem in a medallion, flanked by four round buttons.</summary>
        private static void BottomBar()
        {
            const float BW = 520f, BH = 96f;
            var r = new Rect((UI.screenWidth - BW) / 2f, UI.screenHeight - 35f - BH + 4f, BW, BH);
            if (TopBarTex.Bottom != null) GUI.DrawTexture(r, TopBarTex.Bottom);
            Faction me = Faction.OfPlayer;
            var med = new Rect(r.x + 260f - 27f, r.y + 48f - 27f, 54f, 54f);
            if (me != null)
            {
                GUI.color = me.Color;
                GUI.DrawTexture(med, me.def.FactionIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(med, me.Name + "\n" + me.def.LabelCap);
                if (Widgets.ButtonInvisible(med)) OpenTab("Factions");
            }
            for (int i = 0; i < 4; i++)
            {
                var b = new Rect(r.x + SocketX[i] - 14f, r.y + 71f - 14f, 28f, 28f);
                if (TopBarTex.Buttons[i] != null) GUI.DrawTexture(b, TopBarTex.Buttons[i]);
                if (Mouse.IsOver(b)) Widgets.DrawTextureFitted(b.ExpandedBy(3f), TexUI.HighlightTex, 1f);
                TooltipHandler.TipRegion(b, ButtonTips[i]);
                if (Widgets.ButtonInvisible(b)) OpenTab(ButtonTabs[i]);
            }
            BlockClicks(new Rect(r.x + 14f, r.y + 50f, BW - 28f, BH - 50f));
            BlockClicks(new Rect(r.x + 214f, r.y, 92f, 50f));
        }

        private static void OpenTab(string name)
        {
            var def = DefDatabase<MainButtonDef>.GetNamedSilentFail(name);
            if (def != null) Find.MainTabsRoot.ToggleTab(def);
        }

        private static void Slot(Rect r, Texture2D icon, string text, Color col, string tip)
        {
            var ir = new Rect(r.x + 10f, r.y + (r.height - 26f) / 2f, 26f, 26f);
            if (icon != null) GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = col;
            Widgets.Label(new Rect(ir.xMax + 6f, r.y, r.width - 44f, r.height), text);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            TooltipHandler.TipRegion(r, tip);
        }
    }

    /// <summary>
    /// The world in miniature at the bottom right, on parchment: the lands of every people in its colour, your settlements
    /// as gold dots. Click it to go to that place on the world map.
    /// </summary>
    public static class Minimap
    {
        private const float FW = 260f, FH = 196f, B = 13f;
        private static Texture2D tex;
        private static Faction[] builtFrom;
        private static float minLon, maxLon, minLat, maxLat;

        private static Vector2 LL(int tile) => Find.WorldGrid.Surface.LongLatOf(tile);

        private static void Build()
        {
            var owners = Territory.Owners();
            if (tex != null && ReferenceEquals(owners, builtFrom)) return;
            builtFrom = owners;
            var layer = Find.WorldGrid.Surface;
            int n = layer.TilesCount;
            minLon = minLat = float.MaxValue; maxLon = maxLat = float.MinValue;
            var ll = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                ll[i] = LL(i);
                minLon = Mathf.Min(minLon, ll[i].x); maxLon = Mathf.Max(maxLon, ll[i].x);
                minLat = Mathf.Min(minLat, ll[i].y); maxLat = Mathf.Max(maxLat, ll[i].y);
            }
            int w = 234, h = 170;
            var px = new Color[w * h];
            var own = new int[w * h];
            for (int k = 0; k < own.Length; k++) own[k] = -2;
            int dot = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(w * h * 1.6f / Mathf.Max(1, n))), 1, 4);
            var land = new Color(0.86f, 0.78f, 0.61f, 0f);
            var sea = new Color(0.42f, 0.52f, 0.52f, 0.55f);
            for (int i = 0; i < n; i++)
            {
                var t = layer[i];
                if (t == null) continue;
                bool water = t.WaterCovered;
                Faction f = !water && i < owners.Length ? owners[i] : null;
                Color c = water ? sea : f != null ? new Color(f.Color.r, f.Color.g, f.Color.b, 0.62f) : land;
                int id = water ? -1 : f != null ? f.loadID : 0;
                int x = Mathf.RoundToInt((ll[i].x - minLon) / Mathf.Max(0.01f, maxLon - minLon) * (w - 1));
                int y = Mathf.RoundToInt((ll[i].y - minLat) / Mathf.Max(0.01f, maxLat - minLat) * (h - 1));
                for (int dx = 0; dx < dot; dx++)
                    for (int dy = 0; dy < dot; dy++)
                    {
                        int xx = x + dx - dot / 2, yy = y + dy - dot / 2;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        px[yy * w + xx] = c; own[yy * w + xx] = id;
                    }
            }
            // borders between peoples, and coastlines, drawn as dark ink
            var ink = new Color(0.22f, 0.16f, 0.1f, 0.8f);
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int o = own[y * w + x];
                    if (o < 0) continue;
                    int a = own[y * w + x + 1], b = own[(y + 1) * w + x];
                    if ((a != o && a != -2) || (b != o && b != -2)) px[y * w + x] = ink;
                }
            if (tex == null) tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply();
        }

        public static void Draw()
        {
            if (Find.World == null || Find.WorldGrid == null) return;
            Build();
            var r = new Rect(UI.screenWidth - FW - 214f, UI.screenHeight - 35f - FH - 6f, FW, FH);
            var inner = new Rect(r.x + B, r.y + B, FW - 2 * B, FH - 2 * B);
            if (TopBarTex.Parchment != null) GUI.DrawTexture(inner, TopBarTex.Parchment);
            if (tex != null) GUI.DrawTexture(inner, tex);
            // your settlements
            foreach (var s in Find.WorldObjects.Settlements)
            {
                if (s.Faction == null || !s.Faction.IsPlayer || !s.Tile.Valid || s.Tile.Layer == null || !s.Tile.Layer.IsRootSurface) continue;
                var p = ToScreen(inner, LL(s.Tile.tileId));
                Widgets.DrawBoxSolid(new Rect(p.x - 3.5f, p.y - 3.5f, 7f, 7f), new Color(0.1f, 0.07f, 0.03f));
                Widgets.DrawBoxSolid(new Rect(p.x - 2.5f, p.y - 2.5f, 5f, 5f), new Color(1f, 0.82f, 0.3f));
            }
            if (TopBarTex.MapFrame != null) GUI.DrawTexture(r, TopBarTex.MapFrame);
            TooltipHandler.TipRegion(inner, "The known world. Click to go there on the world map.");
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(inner))
            {
                Vector2 m = Event.current.mousePosition;
                float lon = minLon + (m.x - inner.x) / inner.width * (maxLon - minLon);
                float lat = minLat + (inner.yMax - m.y) / inner.height * (maxLat - minLat);
                var layer = Find.WorldGrid.Surface;
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < layer.TilesCount; i++)
                {
                    var v = LL(i);
                    float d = (v.x - lon) * (v.x - lon) + (v.y - lat) * (v.y - lat);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best >= 0) CameraJumper.TryJump(new GlobalTargetInfo(new PlanetTile(best, layer)));
                Event.current.Use();
            }
            GameComponent_TopBar.BlockClicks(r);
        }

        private static Vector2 ToScreen(Rect inner, Vector2 ll) =>
            new Vector2(inner.x + (ll.x - minLon) / Mathf.Max(0.01f, maxLon - minLon) * inner.width,
                        inner.yMax - (ll.y - minLat) / Mathf.Max(0.01f, maxLat - minLat) * inner.height);
    }

    /// <summary>The calendar begins in 395 AD, the year the empire was divided, instead of 5500.</summary>
    [HarmonyPatch(typeof(GenDate), nameof(GenDate.Year), new[] { typeof(long), typeof(float) })]
    public static class Patch_YearAD
    {
        public static void Postfix(ref int __result)
        {
            if (AoMMod.Settings?.yearAD ?? true) __result = __result - 5500 + 395;
        }
    }
}
