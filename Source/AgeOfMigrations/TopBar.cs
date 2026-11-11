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
    }

    /// <summary>
    /// The Attila-style bar at the top of the screen: treasury in gold coins, the gold gained or lost over the last day,
    /// food in store, and the year. Shown on the colony map and on the world map.
    /// </summary>
    public class GameComponent_TopBar : GameComponent
    {
        private const float W = 600f, H = 46f, Cap = 34f;
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
                float x0 = r.x + Cap + 14f, slot = (W - 2 * Cap - 28f) / 4f;

                int colonists = Find.Maps.Where(m => m.IsPlayerHome).Sum(m => m.mapPawns.FreeColonistsCount);
                float perDay = Mathf.Max(1, colonists) * 1.6f;
                string season = Find.CurrentMap != null ? GenLocalDate.Season(Find.CurrentMap).Label() : "";

                Slot(new Rect(x0, 0, slot, H - 10f), ThingDefOf.Silver.uiIcon, cachedGold.ToString("N0"), Color.white,
                    $"Treasury: {cachedGold:N0} gold coins in your settlements and caravans.");
                Slot(new Rect(x0 + slot, 0, slot, H - 10f), TopBarTex.Income, (income >= 0 ? "+" : "") + income.ToString("N0"),
                    income >= 0 ? new Color(0.55f, 0.85f, 0.45f) : new Color(0.95f, 0.4f, 0.32f),
                    $"Gold gained or lost over the last day: {income:+#,0;-#,0;0}.");
                var hay = DefDatabase<ThingDef>.GetNamedSilentFail("Hay");
                Slot(new Rect(x0 + 2 * slot, 0, slot, H - 10f), hay?.uiIcon, cachedFood.ToString("N0"),
                    foodChange < 0 ? new Color(0.95f, 0.75f, 0.4f) : Color.white,
                    $"Food in store: {cachedFood:N0} nutrition, enough for about {cachedFood / perDay:N0} days for {colonists} people.\nChange over the last day: {foodChange:+#,0;-#,0;0}.");
                Slot(new Rect(x0 + 3 * slot, 0, slot, H - 10f), TopBarTex.Year, YearAD() + " AD", Color.white,
                    $"The year {YearAD()} after Christ{(season.NullOrEmpty() ? "" : ", " + season)}.");
            }
            catch (Exception e) { Log.ErrorOnce("[Age of Migrations] top bar: " + e, 81240); }
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
