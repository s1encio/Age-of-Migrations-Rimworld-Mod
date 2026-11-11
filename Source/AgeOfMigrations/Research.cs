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
        public bool accurateEarth = true;
        public bool hideVanillaFaiths = true;
        public bool showAllCultureTrees;
        public bool removeAnomaly = true;
        public bool showBorders = true;
        public bool showTopBar = true;
        public bool yearAD = true;

        // Realism mode: every removal on. Casual mode: every removal off.
        public bool AllRemovals(bool on) =>
            removeVanillaFactions == on && removeModernThings == on && hideModernResearch == on &&
            hideVanillaScenarios == on && removeSciFiEvents == on && hideVanillaFaiths == on && removeAnomaly == on;

        public void SetAll(bool on)
        {
            removeVanillaFactions = removeModernThings = hideModernResearch = on;
            hideVanillaScenarios = removeSciFiEvents = hideVanillaFaiths = removeAnomaly = on;
        }

        public override void ExposeData()
        {
            // keys carry a version so the new defaults (everything removed) apply to anyone who saved the old settings
            Scribe_Values.Look(ref removeVanillaFactions, "removeVanillaFactions_v2", true);
            Scribe_Values.Look(ref removeModernThings, "removeModernThings_v2", true);
            Scribe_Values.Look(ref hideModernResearch, "hideModernResearch_v2", true);
            Scribe_Values.Look(ref hideVanillaScenarios, "hideVanillaScenarios_v2", true);
            Scribe_Values.Look(ref removeSciFiEvents, "removeSciFiEvents_v2", true);
            Scribe_Values.Look(ref accurateEarth, "accurateEarth", true);
            Scribe_Values.Look(ref hideVanillaFaiths, "hideVanillaFaiths", true);
            Scribe_Values.Look(ref showAllCultureTrees, "showAllCultureTrees", false);
            Scribe_Values.Look(ref removeAnomaly, "removeAnomaly", true);
            Scribe_Values.Look(ref showBorders, "showBorders", true);
            Scribe_Values.Look(ref showTopBar, "showTopBar", true);
            Scribe_Values.Look(ref yearAD, "yearAD", true);
        }
    }

    public class AoMMod : Mod
    {
        public static AoMSettings Settings;
        public static AoMMod Instance;

        public AoMMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<AoMSettings>();
        }

        public override string SettingsCategory() => "Age of Migrations: Late Rome";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            var s = Settings;
            bool realism = s.AllRemovals(true), casual = s.AllRemovals(false);
            Text.Font = GameFont.Medium;
            l.Label("Mode: " + (realism ? "Realism" : casual ? "Casual" : "Custom"));
            Text.Font = GameFont.Small;
            if (l.RadioButton("Realism", realism)) s.SetAll(true);
            l.Label("    Only what fits the year 395: no guns, electricity, computers, components, spacecraft, vanilla factions, " +
                    "Anomaly horrors, insect hives, mechanoids or other sci-fi events.");
            if (l.RadioButton("Casual", casual)) s.SetAll(false);
            l.Label("    Nothing is taken out of vanilla. The mod only adds its peoples, buildings, gear and research.");
            l.Gap(6f);
            l.Label("Changes take effect after you restart the game; changes to factions only affect new worlds. Fine-tune below:");
            l.GapLine();
            l.CheckboxLabeled("Remove the vanilla factions", ref Settings.removeVanillaFactions,
                "Outlanders, tribes, pirates, the Empire and the other vanilla factions no longer appear in new worlds. " +
                "Only the peoples of this mod are left.");
            l.CheckboxLabeled("Remove modern and sci-fi items and buildings", ref Settings.removeModernThings,
                "Every vanilla item from the industrial age onwards: guns, grenades, charge and plasma weapons, flak and power armour, " +
                "components, industrial and glitterworld medicine, modern drugs and the like. They are no longer sold, carried by raiders, " +
                "given as rewards or made at a bench. Every building that runs on electricity or needs modern parts goes too: " +
                "generators, conduits, batteries, electric lamps and benches, computers and consoles, turrets and spaceship parts.");
            l.CheckboxLabeled("Hide industrial and space-age research", ref Settings.hideModernResearch,
                "Removes the vanilla research projects from the industrial age onwards (electricity, guns, machining, spacer tech) " +
                "and with them every modern building they unlock.");
            l.CheckboxLabeled("Hide the vanilla scenarios", ref Settings.hideVanillaScenarios,
                "Crashlanded, The Rich Explorer and the other vanilla scenarios are hidden: start as Roman settlers, a barbarian war band or Persian settlers.");
            l.CheckboxLabeled("Hide the vanilla ideoligion presets", ref Settings.hideVanillaFaiths,
                "When you create a world, choose one of the real faiths of 395 (Catholic, Orthodox, Arian, Gothic and Ostrogothic Churches, " +
                "the Old Gods, the Frankish Gods, Tengri, the Zoroastrian Faith) or make your own. Untick to see the vanilla presets as well.");
            l.CheckboxLabeled("Turn off sci-fi events", ref Settings.removeSciFiEvents,
                "No orbital traders, drop pods, crashed ship parts, mechanoid clusters or raids, insect infestations, " +
                "insect hives in caves or ancient shrines with cryptosleep caskets.");
            l.CheckboxLabeled("Remove the Anomaly expansion", ref Settings.removeAnomaly,
                "No monolith, no entities, no dark rituals: Anomaly's events, research, buildings and items are switched off.");
            l.GapLine();
            l.CheckboxLabeled("Accurate map of the late Roman world", ref Settings.accurateEarth,
                "New worlds become Europe, North Africa and the Near East as they really are: coasts, seas, mountains, climate and rivers, " +
                "with real latitudes and seasons. Every people starts where it was in 395 AD, and the cities stand where they really stood " +
                "(Roma, Ravenna, Constantinopolis, Ctesiphon, Londinium, Alexandria...). The seas, mountains and lands carry their Latin names. " +
                "Works with any planet coverage; 30% gives a good level of detail. Turn it off for a random RimWorld planet.");
            l.CheckboxLabeled("Show the treasury bar at the top of the screen", ref Settings.showTopBar,
                "Gold, daily income, food in store and the year, in a bar at the top of the screen.");
            l.CheckboxLabeled("Count the years from 395 AD", ref Settings.yearAD,
                "The calendar starts in the year 395 instead of 5500. Birth years, dates and the top bar follow it.");
            l.CheckboxLabeled("Show the borders of the peoples on the world map", ref Settings.showBorders,
                "Each people's lands are tinted in its colour, with a border along their edge.");
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
            "DefoliatorShipPartCrash", "MechCluster", "MechanoidRaid", "WastepackInfestation", "Infestation", "DeepDrillInfestation",
        };

        private static bool Official(Def d) => d.modContentPack != null && d.modContentPack.IsOfficialMod;
        private static bool Ours(Def d) => d.modContentPack != null && AoMMod.Instance != null && d.modContentPack == AoMMod.Instance.Content;
        private static bool FromAnomaly(Def d) =>
            d.modContentPack != null && string.Equals(d.modContentPack.PackageId, "ludeon.rimworld.anomaly", StringComparison.OrdinalIgnoreCase);

        private static void RemoveAnomaly()
        {
            var royalty = AccessTools.Field(typeof(IncidentDef), "baseChanceWithRoyalty");
            int inc = 0;
            foreach (var d in DefDatabase<IncidentDef>.AllDefsListForReading.Where(FromAnomaly))
            {
                d.baseChance = 0f;
                if (royalty != null && royalty.FieldType == typeof(float)) royalty.SetValue(d, 0f);
                inc++;
            }
            var research = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(FromAnomaly).ToList();
            foreach (var p in research) DefDb<ResearchProjectDef>.Remove(p);
            var used = new HashSet<ResearchTabDef>(DefDatabase<ResearchProjectDef>.AllDefsListForReading.Select(p => p.tab));
            foreach (var t in DefDatabase<ResearchTabDef>.AllDefsListForReading.Where(FromAnomaly).ToList())
                if (!used.Contains(t)) DefDb<ResearchTabDef>.Remove(t);
            var gone = new HashSet<BuildableDef>();
            var items = new HashSet<ThingDef>();
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading.Where(FromAnomaly))
            {
                if (d.designationCategory != null) { d.designationCategory = null; gone.Add(d); }
                if (d.category == ThingCategory.Item)
                {
                    d.tradeability = Tradeability.None; d.thingSetMakerTags?.Clear(); d.generateCommonality = 0f;
                    d.weaponTags?.Clear(); d.apparel?.tags?.Clear(); items.Add(d);
                }
            }
            foreach (var t in DefDatabase<TerrainDef>.AllDefsListForReading.Where(FromAnomaly))
                if (t.designationCategory != null) { t.designationCategory = null; gone.Add(t); }
            var recipes = DefDatabase<RecipeDef>.AllDefsListForReading
                .Where(r => FromAnomaly(r) || (r.products != null && r.products.Any(p => items.Contains(p.thingDef)))).ToList();
            var cache = AccessTools.Field(typeof(ThingDef), "allRecipesCached");
            foreach (var r in recipes) r.recipeUsers?.Clear();
            foreach (var b in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (b.recipes != null) b.recipes.RemoveAll(recipes.Contains);
                cache?.SetValue(b, null);
            }
            StripDesignators(gone);
            foreach (var c in DefDatabase<DesignationCategoryDef>.AllDefsListForReading.Where(FromAnomaly).ToList())
                DefDb<DesignationCategoryDef>.Remove(c);
            Log.Message($"[Age of Migrations] removed Anomaly: {inc} events, {research.Count} research projects, {gone.Count} buildings, {items.Count} items.");
        }

        private static void StripDesignators(HashSet<BuildableDef> gone)
        {
            if (gone.Count == 0) return;
            foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
                foreach (var f in AccessTools.GetDeclaredFields(typeof(DesignationCategoryDef)))
                    if (f.GetValue(cat) is List<Designator> list) Strip(list, gone);
        }

        public static void Apply()
        {
            var s = AoMMod.Settings;
            if (s == null) return;
            Run("factions", s.removeVanillaFactions, RemoveFactions);
            Run("items", s.removeModernThings, RemoveModernThings);
            Run("research", s.hideModernResearch, HideModernResearch);
            Run("scenarios", s.hideVanillaScenarios, HideScenarios);
            Run("events", s.removeSciFiEvents, RemoveSciFiEvents);
            Run("ideoligion presets", s.hideVanillaFaiths && ModsConfig.IdeologyActive, HideVanillaFaiths);
            Run("Anomaly content", s.removeAnomaly && ModsConfig.AnomalyActive, RemoveAnomaly);
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
                // every faction that is not one of our peoples: vanilla, DLC and other mods
                if (Ours(f) || f.isPlayer || f.hidden || KeepFactions.Contains(f.defName)) continue;
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
            RemoveModernBuildings();
        }

        private static readonly HashSet<string> ModernParts = new HashSet<string>
        {
            "ComponentIndustrial", "ComponentSpacer", "Plasteel", "Uranium", "Hyperweave", "Chemfuel",
        };

        private static bool ModernResearch(BuildableDef d) =>
            d.researchPrerequisites != null && d.researchPrerequisites.Any(r => r != null && r.techLevel >= TechLevel.Industrial);

        /// <summary>Electric, computer and spacer buildings leave the architect menu.</summary>
        private static void RemoveModernBuildings()
        {
            var gone = new HashSet<BuildableDef>();
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!Official(d) || d.category != ThingCategory.Building || d.designationCategory == null) continue;
                bool modern = d.techLevel >= TechLevel.Industrial || ModernResearch(d)
                              || (d.comps != null && d.comps.Any(c => c is CompProperties_Power))
                              || (d.costList != null && d.costList.Any(c => c?.thingDef != null && ModernParts.Contains(c.thingDef.defName)));
                if (!modern) continue;
                d.designationCategory = null;
                gone.Add(d);
            }
            foreach (var t in DefDatabase<TerrainDef>.AllDefsListForReading)
            {
                if (!Official(t) || t.designationCategory == null) continue;
                if (!ModernResearch(t) && !(t.costList != null && t.costList.Any(c => c?.thingDef != null && ModernParts.Contains(c.thingDef.defName)))) continue;
                t.designationCategory = null;
                gone.Add(t);
            }
            if (gone.Count == 0) return;
            StripDesignators(gone);
            Log.Message($"[Age of Migrations] removed {gone.Count} electric, computer and spacer buildings and floors.");
        }

        private static void Strip(List<Designator> list, HashSet<BuildableDef> gone)
        {
            list.RemoveAll(d => d is Designator_Build b && gone.Contains(b.PlacingDef));
            foreach (var dd in list.OfType<Designator_Dropdown>())
                if (AccessTools.Field(typeof(Designator_Dropdown), "elements")?.GetValue(dd) is List<Designator> inner) Strip(inner, gone);
            list.RemoveAll(d => d is Designator_Dropdown dd &&
                                AccessTools.Field(typeof(Designator_Dropdown), "elements")?.GetValue(dd) is List<Designator> inner && inner.Count == 0);
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

        private static void HideVanillaFaiths()
        {
            var ours = DefDatabase<IdeoPresetDef>.AllDefsListForReading.Where(d => !Official(d)).ToList();
            if (ours.Count == 0) return;   // never leave the page empty
            var gone = DefDatabase<IdeoPresetDef>.AllDefsListForReading.Where(Official).ToList();
            foreach (var d in gone) DefDb<IdeoPresetDef>.Remove(d);
            var used = new HashSet<IdeoPresetCategoryDef>(DefDatabase<IdeoPresetDef>.AllDefsListForReading.Select(d => d.categoryDef));
            foreach (var c in DefDatabase<IdeoPresetCategoryDef>.AllDefsListForReading.Where(c => Official(c) && !used.Contains(c)).ToList())
                DefDb<IdeoPresetCategoryDef>.Remove(c);
            Log.Message($"[Age of Migrations] hid {gone.Count} vanilla ideoligion presets.");
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

namespace AgeOfMigrations
{
    /// <summary>
    /// Makes our furniture inherit from a vanilla def. If the vanilla def already carries a Name (RimWorld 1.6 gives some of
    /// them one), the children use that Name; otherwise the vanilla def gets the fallback Name.
    /// </summary>
    public class PatchOperationLinkParent : PatchOperation
    {
        public string vanilla;
        public string fallbackName;
        public List<string> children = new List<string>();

        protected override bool ApplyWorker(System.Xml.XmlDocument xml)
        {
            var v = xml.SelectSingleNode($"Defs/ThingDef[defName=\"{vanilla}\"]") as System.Xml.XmlElement;
            if (v == null) return false;
            string name = v.GetAttribute("Name");
            if (string.IsNullOrEmpty(name))
            {
                name = fallbackName;
                v.SetAttribute("Name", name);
            }
            foreach (string c in children)
                foreach (System.Xml.XmlNode n in xml.SelectNodes($"Defs/ThingDef[defName=\"{c}\"]"))
                    (n as System.Xml.XmlElement)?.SetAttribute("ParentName", name);
            return true;
        }
    }
}

namespace AgeOfMigrations
{
    /// <summary>
    /// Adds names to a vanilla name rule pack, under whichever rule key(s) it actually uses for the final name
    /// (the keys no other rule refers to). Five Christian religions in one world use up the vanilla Christian deity names.
    /// </summary>
    public class PatchOperationAddNames : PatchOperation
    {
        public string rulePack;
        public List<string> names = new List<string>();

        protected override bool ApplyWorker(System.Xml.XmlDocument xml)
        {
            var def = xml.SelectSingleNode($"Defs/RulePackDef[defName=\"{rulePack}\"]") as System.Xml.XmlElement;
            if (def == null) return false;
            var keys = new HashSet<string>();
            var referenced = new HashSet<string>();
            System.Xml.XmlNode stringsNode = null;
            foreach (System.Xml.XmlNode li in def.SelectNodes(".//rulesStrings/li"))
            {
                stringsNode = stringsNode ?? li.ParentNode;
                string t = li.InnerText ?? "";
                int arrow = t.IndexOf("->", StringComparison.Ordinal);
                if (arrow <= 0) continue;
                string key = t.Substring(0, arrow).Trim();
                int paren = key.IndexOf('(');
                if (paren > 0) key = key.Substring(0, paren);
                keys.Add(key);
                string rhs = t.Substring(arrow + 2);
                for (int i = rhs.IndexOf('['); i >= 0; i = rhs.IndexOf('[', i + 1))
                {
                    int close = rhs.IndexOf(']', i);
                    if (close > i) referenced.Add(rhs.Substring(i + 1, close - i - 1));
                }
            }
            var roots = keys.Where(k => !referenced.Contains(k)).ToList();
            if (roots.Count == 0) roots.Add("r_name");
            if (stringsNode == null)
            {
                var pack = def.SelectSingleNode("rulePack") ?? def.AppendChild(xml.CreateElement("rulePack"));
                stringsNode = pack.AppendChild(xml.CreateElement("rulesStrings"));
            }
            foreach (string root in roots)
                foreach (string n in names)
                {
                    var li = xml.CreateElement("li");
                    li.InnerText = root + "->" + n;
                    stringsNode.AppendChild(li);
                }
            return true;
        }
    }
}
