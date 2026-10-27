using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AgeOfMigrations
{
    /// <summary>Marks a research project, research tab or player faction as belonging to one people: Roman, Barbarian or Persian.</summary>
    public class ResearchCulture : DefModExtension
    {
        public string culture;
    }

    // ------------------------------------------------------------------ settings

    public class AoMSettings : ModSettings
    {
        // everything that does not fit the period is off by default; each can be switched back on
        public bool removeVanillaFactions = true;
        public bool removeModernThings = true;
        public bool hideModernResearch = true;
        public bool hideVanillaScenarios = true;
        public bool removeSciFiEvents = true;
        public bool showAllCultureTrees;

        public override void ExposeData()
        {
            // keys carry a version so the new defaults (everything removed) apply to anyone who saved the old settings
            Scribe_Values.Look(ref removeVanillaFactions, "removeVanillaFactions_v2", true);
            Scribe_Values.Look(ref removeModernThings, "removeModernThings_v2", true);
            Scribe_Values.Look(ref hideModernResearch, "hideModernResearch_v2", true);
            Scribe_Values.Look(ref hideVanillaScenarios, "hideVanillaScenarios_v2", true);
            Scribe_Values.Look(ref removeSciFiEvents, "removeSciFiEvents_v2", true);
            Scribe_Values.Look(ref showAllCultureTrees, "showAllCultureTrees", false);
        }
    }

    public class AoMMod : Mod
    {
        public static AoMSettings Settings;

        public AoMMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<AoMSettings>();
        }

        public override string SettingsCategory() => "Age of Migrations: Late Rome";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            l.Label("Everything that does not belong in the late antique world is removed. Untick a box to bring it back. " +
                    "Changes take effect after you restart the game; changes to factions only affect new worlds.");
            l.GapLine();
            l.CheckboxLabeled("Remove the vanilla factions", ref Settings.removeVanillaFactions,
                "Outlanders, tribes, pirates, the Empire and the other vanilla factions no longer appear in new worlds. " +
                "Only the peoples of this mod are left.");
            l.CheckboxLabeled("Remove modern and sci-fi items", ref Settings.removeModernThings,
                "Every vanilla item from the industrial age onwards: guns, grenades, charge and plasma weapons, flak and power armour, " +
                "components, industrial and glitterworld medicine, modern drugs and the like. They are no longer sold, carried by raiders, " +
                "given as rewards or made at a bench.");
            l.CheckboxLabeled("Hide industrial and space-age research", ref Settings.hideModernResearch,
                "Removes the vanilla research projects from the industrial age onwards (electricity, guns, machining, spacer tech) " +
                "and with them every modern building they unlock.");
            l.CheckboxLabeled("Hide the vanilla scenarios", ref Settings.hideVanillaScenarios,
                "Crashlanded, The Rich Explorer and the other vanilla scenarios are hidden: start as Roman settlers, a barbarian war band or Persian settlers.");
            l.CheckboxLabeled("Turn off sci-fi events", ref Settings.removeSciFiEvents,
                "No orbital traders, drop pods, crashed ship parts, mechanoid clusters or mechanoid raids.");
            l.GapLine();
            l.CheckboxLabeled("Show every people's research tree", ref Settings.showAllCultureTrees,
                "Normally a Roman colony only sees the Roman tree, a barbarian band the barbarian tree and a Persian colony the Persian tree. " +
                "Turn this on to see all three.");
            l.End();
        }
    }

    // ------------------------------------------------------------------ removing defs from the database

    internal static class DefDb<T> where T : Def, new()
    {
        private static readonly MethodInfo RemoveMethod = AccessTools.Method(typeof(DefDatabase<T>), "Remove");

        public static void Remove(T def)
        {
            if (DefDatabase<T>.GetNamedSilentFail(def.defName) == null) return;
            if (RemoveMethod != null)
            {
                RemoveMethod.Invoke(null, new object[] { def });
                return;
            }
            (AccessTools.Field(typeof(DefDatabase<T>), "defsList")?.GetValue(null) as List<T>)?.Remove(def);
            (AccessTools.Field(typeof(DefDatabase<T>), "defsByName")?.GetValue(null) as Dictionary<string, T>)?.Remove(def.defName);
            (AccessTools.Field(typeof(DefDatabase<T>), "defsByShortHash")?.GetValue(null) as Dictionary<ushort, T>)?.Remove(def.shortHash);
        }

        public static void Add(T def)
        {
            if (DefDatabase<T>.GetNamedSilentFail(def.defName) == null) DefDatabase<T>.Add(def);
        }
    }

    // ------------------------------------------------------------------ one research tree per people

    [StaticConstructorOnStartup]
    public static class CultureResearch
    {
        private static readonly List<ResearchProjectDef> Projects;
        private static readonly List<ResearchTabDef> Tabs;

        static CultureResearch()
        {
            Projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(d => d.GetModExtension<ResearchCulture>() != null).ToList();
            Tabs = DefDatabase<ResearchTabDef>.AllDefsListForReading.Where(d => d.GetModExtension<ResearchCulture>() != null).ToList();
            try { VanillaRemoval.Apply(); }
            catch (Exception e) { Log.Error("[Age of Migrations] removing vanilla content failed: " + e); }
        }

        /// <summary>Put every people's research back, so a save can find all its projects while it loads.</summary>
        public static void RestoreAll()
        {
            if (Projects == null) return;
            foreach (var t in Tabs) DefDb<ResearchTabDef>.Add(t);
            foreach (var p in Projects) DefDb<ResearchProjectDef>.Add(p);
        }

        /// <summary>Keep only the research of the player's own people.</summary>
        public static void Apply()
        {
            if (Projects == null) return;
            RestoreAll();
            string culture = Faction.OfPlayerSilentFail?.def?.GetModExtension<ResearchCulture>()?.culture;
            if (culture == null || (AoMMod.Settings?.showAllCultureTrees ?? false)) return;
            foreach (var p in Projects)
                if (p.GetModExtension<ResearchCulture>().culture != culture)
                    DefDb<ResearchProjectDef>.Remove(p);
            foreach (var t in Tabs)
                if (t.GetModExtension<ResearchCulture>().culture != culture)
                    DefDb<ResearchTabDef>.Remove(t);
        }
    }

    public class GameComponent_CultureResearch : GameComponent
    {
        public GameComponent_CultureResearch(Game game)
        {
            // Called before a save is read: every project must exist then.
            CultureResearch.RestoreAll();
        }

        public override void FinalizeInit()
        {
            CultureResearch.Apply();
        }
    }

    // ------------------------------------------------------------------ turning off vanilla content

    internal static class VanillaRemoval
    {
        // the game needs these to exist; mechanoids are kept out of raids by the sci-fi events setting instead
        private static readonly HashSet<string> KeepFactions = new HashSet<string>
        {
            "Mechanoid", "Insect", "Ancients", "AncientsHostile", "Entities", "HoraxCult",
        };

        private static readonly string[] SciFiIncidents =
        {
            "OrbitalTraderArrival", "ResourcePodCrash", "RefugeePodCrash", "ShipChunkDrop", "PsychicEmanatorShipPartCrash",
            "DefoliatorShipPartCrash", "MechCluster", "MechanoidRaid", "WastepackInfestation",
        };

        private static bool Official(Def d) => d.modContentPack != null && d.modContentPack.IsOfficialMod;

        public static void Apply()
        {
            var s = AoMMod.Settings;
            if (s == null) return;
            Run("factions", s.removeVanillaFactions, RemoveFactions);
            Run("items", s.removeModernThings, RemoveModernThings);
            Run("research", s.hideModernResearch, HideModernResearch);
            Run("scenarios", s.hideVanillaScenarios, HideScenarios);
            Run("events", s.removeSciFiEvents, RemoveSciFiEvents);
        }

        private static void Run(string what, bool on, Action act)
        {
            if (!on) return;
            try { act(); }
            catch (Exception e) { Log.Error($"[Age of Migrations] removing vanilla {what} failed: {e}"); }
        }

        private static void RemoveFactions()
        {
            var display = AccessTools.Field(typeof(FactionDef), "displayInFactionSelection");
            int n = 0;
            foreach (var f in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                if (!Official(f) || f.isPlayer || f.hidden || KeepFactions.Contains(f.defName)) continue;
                f.startingCountAtWorldCreation = 0;
                f.maxConfigurableAtWorldCreation = 0;
                f.requiredCountAtGameStart = 0;
                if (display != null && display.FieldType == typeof(bool)) display.SetValue(f, false);
                n++;
            }
            Log.Message($"[Age of Migrations] removed {n} vanilla factions from world generation.");
        }

        private static void RemoveModernThings()
        {
            var hidden = new HashSet<ThingDef>();
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!Official(d) || d.techLevel < TechLevel.Industrial || d.category != ThingCategory.Item) continue;
                d.tradeability = Tradeability.None;
                d.weaponTags?.Clear();
                d.apparel?.tags?.Clear();
                d.thingSetMakerTags?.Clear();
                d.generateCommonality = 0f;
                hidden.Add(d);
            }
            if (hidden.Count == 0) return;
            var recipes = DefDatabase<RecipeDef>.AllDefsListForReading
                .Where(r => r.products != null && r.products.Any(p => hidden.Contains(p.thingDef))).ToList();
            var cache = AccessTools.Field(typeof(ThingDef), "allRecipesCached");
            foreach (var r in recipes) r.recipeUsers?.Clear();
            foreach (var b in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (b.recipes != null) b.recipes.RemoveAll(recipes.Contains);
                cache?.SetValue(b, null);
            }
            Log.Message($"[Age of Migrations] removed {hidden.Count} modern and sci-fi items and {recipes.Count} recipes.");
        }

        private static void HideModernResearch()
        {
            var gone = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => Official(p) && p.techLevel >= TechLevel.Industrial).ToList();
            foreach (var p in gone) DefDb<ResearchProjectDef>.Remove(p);
            var used = new HashSet<ResearchTabDef>(DefDatabase<ResearchProjectDef>.AllDefsListForReading.Select(p => p.tab));
            foreach (var t in DefDatabase<ResearchTabDef>.AllDefsListForReading.ToList())
                if (t.defName != "Main" && !used.Contains(t) && t.GetModExtension<ResearchCulture>() == null)
                    DefDb<ResearchTabDef>.Remove(t);
            Log.Message($"[Age of Migrations] hid {gone.Count} industrial and space-age research projects.");
        }

        private static void HideScenarios()
        {
            var gone = DefDatabase<ScenarioDef>.AllDefsListForReading.Where(Official).ToList();
            if (!DefDatabase<ScenarioDef>.AllDefsListForReading.Any(d => !Official(d))) return;   // never leave the list empty
            foreach (var d in gone) DefDb<ScenarioDef>.Remove(d);
            Log.Message($"[Age of Migrations] hid {gone.Count} vanilla scenarios.");
        }

        private static void RemoveSciFiEvents()
        {
            var royalty = AccessTools.Field(typeof(IncidentDef), "baseChanceWithRoyalty");
            int n = 0;
            foreach (var name in SciFiIncidents)
            {
                var inc = DefDatabase<IncidentDef>.GetNamedSilentFail(name);
                if (inc == null) continue;
                inc.baseChance = 0f;
                if (royalty != null && royalty.FieldType == typeof(float)) royalty.SetValue(inc, 0f);
                n++;
            }
            // mechanoids stay in the game (it needs them) but never raid
            var mech = DefDatabase<FactionDef>.GetNamedSilentFail("Mechanoid");
            if (mech != null) mech.earliestRaidDays = 99999999f;
            Log.Message($"[Age of Migrations] turned off {n} sci-fi events and mechanoid raids.");
        }
    }
}
