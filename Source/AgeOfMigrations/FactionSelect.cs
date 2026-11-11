using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Profile;
using Verse.Sound;

namespace AgeOfMigrations
{
    /// <summary>One playable faction on the campaign screen.</summary>
    public class CampaignEntry
    {
        public string group, scenario, faction, label, description;
        public int difficulty;
        public string[] traits;
        public CampaignEntry(string group, string scenario, string faction, string label, int difficulty, string description, params string[] traits)
        {
            this.group = group; this.scenario = scenario; this.faction = faction; this.label = label;
            this.difficulty = difficulty; this.description = description; this.traits = traits;
        }
    }

    [StaticConstructorOnStartup]
    public static class CampaignTex
    {
        public static readonly Texture2D Bg = ContentFinder<Texture2D>.Get("UI/LR_CampaignBg", false);
        public static readonly Texture2D Frame = ContentFinder<Texture2D>.Get("UI/LR_MinimapFrame", false);
        public static readonly Texture2D Plaque = ContentFinder<Texture2D>.Get("UI/LR_TopBar", false);
        private static readonly Dictionary<string, Texture2D> maps = new Dictionary<string, Texture2D>();
        public static Texture2D Map(string faction)
        {
            string key = faction ?? "All";
            if (!maps.TryGetValue(key, out var t))
            {
                t = ContentFinder<Texture2D>.Get("UI/Campaign/LR_Campaign_" + key, false) ?? ContentFinder<Texture2D>.Get("UI/Campaign/LR_Campaign_All", false);
                maps[key] = t;
            }
            return t;
        }
    }

    /// <summary>
    /// The Total War style start: pick a faction from the peoples of 395, a campaign difficulty, and begin. The world is
    /// generated with the standard settings, the settlement is placed inside the faction's lands, and the game starts.
    /// </summary>
    public class Page_ChooseFaction : Window
    {
        public static bool allowVanilla;

        private static readonly List<CampaignEntry> Entries = new List<CampaignEntry>
        {
            new CampaignEntry("Roman Empires", "LR_ScenarioRomans", "LR_WesternEmpire", "Western Roman Empire", 4,
                "Theodosius is dead and the empire is divided between his sons. In the West the boy Honorius reigns from Ravenna, but Stilicho, " +
                "the half-Vandal general, holds the real power. The frontiers on the Rhine and the Danube are thin, the treasury is strained, " +
                "and the barbarians are already within the gates.",
                "Roman research tree: engineering, law and the legions", "Legionaries, archers and cavalry in Roman arms",
                "Stone towns, city walls and roads", "Franks and Saxons on the Rhine, Vandals and Goths on the Danube"),
            new CampaignEntry("Roman Empires", "LR_ScenarioRomansEast", "LR_EasternEmpire", "Eastern Roman Empire", 2,
                "Arcadius rules from Constantinople, the richest city in the world, behind walls no enemy has ever breached. The East has gold, " +
                "the grain of Egypt and a trained army, but the Goths roam the Balkans, the Huns watch from the steppe, and Persia waits beyond the Euphrates.",
                "Roman research tree: engineering, law and the legions", "Wealthy lands from the Danube to Egypt",
                "Stone towns, city walls and roads", "Goths in the Balkans, Huns beyond the Danube, Persia in the east"),
            new CampaignEntry("Great Migrations", "LR_ScenarioVisigoths", "LR_Visigoths", "Visigoths", 3,
                "Alaric's Goths, settled south of the Danube as federates after their victory at Adrianople, are tired of broken Roman promises. " +
                "They are a people under arms, inside the empire: take land from Rome, or carve a kingdom from its ruins.",
                "Barbarian research tree", "Arian Christian warriors with spatha and rhomphaia",
                "Start deep inside Roman lands", "Every neighbour is the Eastern Empire"),
            new CampaignEntry("Great Migrations", "LR_ScenarioOstrogoths", "LR_Ostrogoths", "Ostrogoths", 4,
                "The Greuthungi Goths north of the Black Sea live under the heel of the Huns, and pay them in blood and tribute. " +
                "Endure, grow strong, and one day break free.",
                "Barbarian research tree", "Gothic spearmen and noble horsemen", "Hemmed in between the Huns and the empire"),
            new CampaignEntry("Great Migrations", "LR_ScenarioBarbarians", "LR_Vandals", "Vandals", 4,
                "The Vandals of the Silesian forests and the upper Tisza are restless. Within a few years they will cross the frozen Rhine; " +
                "within a generation they will rule Africa. Their story starts here.",
                "Barbarian research tree", "Fierce infantry with seax, axe and angon", "Germanic tribes to the west, Huns to the east"),
            new CampaignEntry("Barbarian Kingdoms", "LR_ScenarioFranks", "LR_Franks", "Franks", 3,
                "Franks of the lower Rhine: half Roman federates, half raiders. Their throwing axes are feared on both banks of the river, " +
                "and the rich towns of Gaul lie open before them.",
                "Barbarian research tree", "Throwing axes and angons", "The Western Empire just across the Rhine"),
            new CampaignEntry("Barbarian Kingdoms", "LR_ScenarioSaxons", "LR_Saxons", "Saxons", 3,
                "Saxon sea-raiders of the North Sea coast, who worship the old gods and look across the water to Britain. " +
                "The shores of the empire are poorly guarded.",
                "Barbarian research tree", "Seax, spear and round shield", "Pagan faith of the old gods", "Britain and Gaul across the sea"),
            new CampaignEntry("Nomadic Horde", "LR_ScenarioHuns", "LR_Huns", "Huns", 3,
                "The Huns have driven the Goths before them and now rule the great plain from the Volga to the Danube. " +
                "Their horse archers strike and vanish; every people of Europe fears them.",
                "Barbarian research tree", "Mounted horse archers, with Giddy-Up", "Herds of horses and felt tents",
                "Goths, Romans and Persians all within reach"),
            new CampaignEntry("Eastern Empire", "LR_ScenarioPersians", "LR_Sassanids", "Sasanian Persia", 2,
                "The King of Kings rules Eranshahr from Ctesiphon, the old rival of Rome. Armoured cavalry, war elephants and the sacred fire: " +
                "an ancient empire at the height of its power.",
                "Persian research tree", "Armoured cavalry and war elephants", "Zoroastrian faith and fire temples", "Rome to the west, the steppe to the north"),
            new CampaignEntry("Freemen", "LR_ScenarioFreeman", null, "Lone freeman", 5,
                "No lord, no king, no people. One free man or woman against the age of migrations. Start wherever you like.",
                "Roman research tree", "Belong to no faction", "A knife and the clothes on your back"),
            new CampaignEntry("Freemen", "LR_ScenarioFreemen", null, "Three freemen", 4,
                "Three free people who owe nothing to anyone join together to start a farm of their own. Start wherever you like.",
                "Roman research tree", "Belong to no faction", "A few tools and supplies"),
        };

        private static readonly (string label, string def)[] Difficulties =
        {
            ("Easy", "Easy"), ("Normal", "Medium"), ("Hard", "Rough"), ("Legendary", "Hard"),
        };

        private CampaignEntry selected = Entries[0];
        private int difficulty = 1;
        private Vector2 listScroll, textScroll;

        public Page_ChooseFaction()
        {
            forcePause = true;
            doCloseX = false;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            preventCameraMotion = true;
        }

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);
        protected override float Margin => 0f;

        private static readonly Color Bronze = new Color(0.82f, 0.64f, 0.32f);
        private static readonly Color Ink = new Color(0.08f, 0.06f, 0.05f, 0.82f);

        private static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>
        {
            { "LR_WesternEmpire", new Color(0.62f, 0.16f, 0.13f) }, { "LR_EasternEmpire", new Color(0.45f, 0.12f, 0.42f) },
            { "LR_Vandals", new Color(0.16f, 0.55f, 0.55f) }, { "LR_Visigoths", new Color(0.16f, 0.35f, 0.78f) },
            { "LR_Ostrogoths", new Color(0.12f, 0.2f, 0.5f) }, { "LR_Franks", new Color(0.47f, 0.78f, 0.24f) },
            { "LR_Saxons", new Color(0.16f, 0.55f, 0.16f) }, { "LR_Huns", new Color(0.62f, 0.45f, 0.12f) },
            { "LR_Sassanids", new Color(0.75f, 0.6f, 0.15f) },
        };

        private static Color ColorOf(CampaignEntry e) =>
            e.faction != null && Colors.TryGetValue(e.faction, out var c) ? Color.Lerp(c, Color.white, 0.25f) : new Color(0.85f, 0.8f, 0.7f);

        private static FactionDef Def(CampaignEntry e) =>
            e.faction != null ? DefDatabase<FactionDef>.GetNamedSilentFail(e.faction) : DefDatabase<FactionDef>.GetNamedSilentFail("LR_PlayerFreemen");

        private static void Panel(Rect r)
        {
            Widgets.DrawBoxSolid(r, Ink);
            GUI.color = Bronze;
            Widgets.DrawBox(r, 2);
            GUI.color = new Color(Bronze.r, Bronze.g, Bronze.b, 0.35f);
            Widgets.DrawBox(r.ContractedBy(4f), 1);
            GUI.color = Color.white;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (CampaignTex.Bg != null) GUI.DrawTexture(inRect, CampaignTex.Bg, ScaleMode.ScaleAndCrop);
            else Widgets.DrawBoxSolid(inRect, new Color(0.1f, 0.09f, 0.08f));

            // title plaque
            var title = new Rect((inRect.width - 640f) / 2f, 0f, 640f, 54f);
            if (CampaignTex.Plaque != null) GUI.DrawTexture(title, CampaignTex.Plaque);
            Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.95f, 0.85f, 0.6f);
            Widgets.Label(new Rect(title.x, 2f, title.width, 34f), "Choose your faction  ·  Anno Domini 395");
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;

            float top = 70f, bottom = inRect.height - 24f;
            var left = new Rect(24f, top, 300f, bottom - top);
            var right = new Rect(inRect.width - 24f - 400f, top, 400f, bottom - top);
            var center = new Rect(left.xMax + 18f, top, right.x - left.xMax - 36f, bottom - top);
            DoList(left);
            DoMap(center);
            DoDetails(right);
        }

        private void DoList(Rect r)
        {
            Panel(r);
            var inner = r.ContractedBy(10f);
            float h = 0f;
            string g = null;
            foreach (var e in Entries) { if (e.group != g) { h += 34f; g = e.group; } h += 46f; }
            var view = new Rect(0f, 0f, inner.width - 16f, h);
            Widgets.BeginScrollView(inner, ref listScroll, view);
            float y = 0f; g = null;
            foreach (var e in Entries)
            {
                if (e.group != g)
                {
                    g = e.group;
                    Text.Font = GameFont.Small; GUI.color = Bronze;
                    Widgets.Label(new Rect(4f, y + 10f, view.width, 24f), g.ToUpper());
                    Widgets.DrawLineHorizontal(4f, y + 32f, view.width - 8f);
                    GUI.color = Color.white;
                    y += 34f;
                }
                var row = new Rect(0f, y, view.width, 44f);
                if (e == selected) Widgets.DrawBoxSolid(row, new Color(0.55f, 0.4f, 0.18f, 0.45f));
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.07f));
                var fd = Def(e);
                if (fd != null)
                {
                    GUI.color = ColorOf(e);
                    GUI.DrawTexture(new Rect(row.x + 6f, row.y + 6f, 32f, 32f), fd.FactionIcon, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(row.x + 48f, row.y, row.width - 48f, row.height), e.label);
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(row)) { selected = e; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                y += 46f;
            }
            Widgets.EndScrollView();
        }

        private void DoMap(Rect r)
        {
            var map = CampaignTex.Map(selected.faction);
            float aspect = map != null ? (float)map.width / map.height : 1100f / 640f;
            float w = r.width, h = w / aspect;
            if (h > r.height - 150f) { h = r.height - 150f; w = h * aspect; }
            var mr = new Rect(r.x + (r.width - w) / 2f, r.y, w, h);
            if (map != null) GUI.DrawTexture(mr, map);
            if (CampaignTex.Frame != null) GUI.DrawTexture(mr.ExpandedBy(12f), CampaignTex.Frame);

            var tr = new Rect(mr.x, mr.yMax + 22f, mr.width, r.yMax - mr.yMax - 22f);
            Panel(tr);
            var inner = tr.ContractedBy(14f);
            Text.Font = GameFont.Small;
            float th = Text.CalcHeight(selected.description, inner.width - 16f);
            var view = new Rect(0f, 0f, inner.width - 16f, th);
            Widgets.BeginScrollView(inner, ref textScroll, view);
            GUI.color = new Color(0.92f, 0.88f, 0.8f);
            Widgets.Label(view, selected.description);
            GUI.color = Color.white;
            Widgets.EndScrollView();
        }

        private void DoDetails(Rect r)
        {
            Panel(r);
            var inner = r.ContractedBy(16f);
            float y = inner.y;
            var fd = Def(selected);
            if (fd != null)
            {
                var er = new Rect(inner.x + (inner.width - 128f) / 2f, y, 128f, 128f);
                Widgets.DrawBoxSolid(er.ExpandedBy(6f), new Color(0f, 0f, 0f, 0.35f));
                GUI.color = ColorOf(selected);
                GUI.DrawTexture(er, fd.FactionIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }
            y += 140f;
            Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(inner.x, y, inner.width, 36f), selected.label);
            Text.Font = GameFont.Small; GUI.color = Bronze;
            Widgets.Label(new Rect(inner.x, y + 34f, inner.width, 24f), selected.group);
            GUI.color = Color.white;
            y += 64f;
            string stars = new string('★', selected.difficulty) + new string('☆', 5 - selected.difficulty);
            Widgets.Label(new Rect(inner.x, y, inner.width, 24f), "Difficulty  " + stars);
            Text.Anchor = TextAnchor.UpperLeft;
            y += 34f;
            GUI.color = Bronze; Widgets.DrawLineHorizontal(inner.x, y, inner.width); GUI.color = Color.white;
            y += 10f;
            foreach (string t in selected.traits)
            {
                float th = Text.CalcHeight("•  " + t, inner.width);
                Widgets.Label(new Rect(inner.x, y, inner.width, th), "•  " + t);
                y += th + 4f;
            }

            // campaign difficulty and start
            float by = inner.yMax - 210f;
            GUI.color = Bronze; Widgets.DrawLineHorizontal(inner.x, by - 10f, inner.width); GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(inner.x, by, inner.width, 24f), "Campaign difficulty");
            Text.Anchor = TextAnchor.UpperLeft;
            float bw = (inner.width - 12f) / 4f;
            for (int i = 0; i < Difficulties.Length; i++)
            {
                var b = new Rect(inner.x + i * (bw + 4f), by + 28f, bw, 32f);
                if (i == difficulty) Widgets.DrawBoxSolid(b.ExpandedBy(2f), Bronze);
                if (Widgets.ButtonText(b, Difficulties[i].label)) difficulty = i;
            }
            if (Widgets.ButtonText(new Rect(inner.x, by + 80f, inner.width, 52f), "Begin the campaign"))
                StartCampaign();
            if (Widgets.ButtonText(new Rect(inner.x, by + 142f, inner.width / 2f - 4f, 36f), "Back"))
                Close();
            if (Widgets.ButtonText(new Rect(inner.x + inner.width / 2f + 4f, by + 142f, inner.width / 2f - 4f, 36f), "Custom game"))
            {
                Close();
                allowVanilla = true;
                Find.WindowStack.Add(new Page_SelectScenario());
            }
        }

        private void StartCampaign()
        {
            var scenDef = DefDatabase<ScenarioDef>.GetNamedSilentFail(selected.scenario);
            if (scenDef == null) { Messages.Message("Scenario not found: " + selected.scenario, MessageTypeDefOf.RejectInput, false); return; }
            var entry = selected;
            var diff = DefDatabase<DifficultyDef>.GetNamedSilentFail(Difficulties[difficulty].def) ?? DefDatabase<DifficultyDef>.AllDefs.First();
            Close();
            try
            {
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Current.Game.Scenario = scenDef.scenario.CopyForEditing();
                Current.Game.Scenario.PreConfigure();
                Current.Game.storyteller = new Storyteller(StorytellerDefOf.Cassandra, diff);

                // the standard world settings, as the world page would have them
                var wp = new Page_CreateWorldParams();
                wp.Reset();
                var t = Traverse.Create(wp);
                float coverage = t.Field("planetCoverage").GetValue<float>();
                string seed = GenText.RandomSeedString();
                var rain = t.Field("rainfall").GetValue<OverallRainfall>();
                var temp = t.Field("temperature").GetValue<OverallTemperature>();
                var pop = t.Field("population").GetValue<OverallPopulation>();
                var lm = t.Field("landmarkDensity").GetValue<LandmarkDensity>();
                var factions = t.Field("factions").GetValue<List<FactionDef>>();
                float pollution = wp.pollution;

                LongEventHandler.QueueLongEvent(delegate
                {
                    AccessTools.Method(typeof(GameInitData), "ResetWorldRelatedMapInitData")?.Invoke(Find.GameInitData, null);
                    Current.Game.World = WorldGenerator.GenerateWorld(coverage, seed, rain, temp, pop, lm, factions, pollution);
                    LongEventHandler.ExecuteWhenFinished(delegate
                    {
                        try
                        {
                            Find.World.renderer.RegenerateAllLayersNow();
                            MemoryUtility.UnloadUnusedUnityAssets();
                            FinishSetup(entry);
                        }
                        catch (Exception e) { Log.Error("[Age of Migrations] starting the campaign failed: " + e); GenScene.GoToMainMenu(); }
                    });
                }, "GeneratingWorld", true, null);
            }
            catch (Exception e) { Log.Error("[Age of Migrations] starting the campaign failed: " + e); }
        }

        /// <summary>After the world exists: settlement site, faith, colonists, then the map.</summary>
        private static void FinishSetup(CampaignEntry entry)
        {
            var init = Find.GameInitData;
            init.startingTile = TileFinder.RandomStartingTile();   // inside the faction's lands (Patch_RandomStartInOwnLands)

            if (ModsConfig.IdeologyActive && Faction.OfPlayer?.ideos != null)
            {
                Ideo ideo = null;
                var people = entry.faction != null ? Find.FactionManager.AllFactions.FirstOrDefault(f => f.def.defName == entry.faction) : null;
                ideo = people?.ideos?.PrimaryIdeo;
                if (ideo == null)   // freemen: the faith of the land they live in
                    ideo = Find.FactionManager.AllFactions.FirstOrDefault(f => f.def.defName == "LR_WesternEmpire")?.ideos?.PrimaryIdeo
                           ?? Find.IdeoManager.IdeosListForReading.FirstOrDefault();
                if (ideo != null) Faction.OfPlayer.ideos.SetPrimary(ideo);
            }
            var pawns = Traverse.Create(init).Field("startingAndOptionalPawns").GetValue<List<Pawn>>();
            if (ModsConfig.IdeologyActive || pawns.NullOrEmpty())
                Find.Scenario.PostIdeoChosen();
            PageUtility.InitGameStart();
        }
    }

    /// <summary>"New colony" opens the faction screen instead of the scenario list (unless "Custom game" was chosen there).</summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    public static class Patch_NewColonyFactionScreen
    {
        public static bool Prefix(ref Window window)
        {
            if (!(window is Page_SelectScenario) || Current.ProgramState != ProgramState.Entry) return true;
            if (Page_ChooseFaction.allowVanilla) { Page_ChooseFaction.allowVanilla = false; return true; }
            if (!(AoMMod.Settings?.factionScreen ?? true)) return true;
            window = new Page_ChooseFaction();
            return true;
        }
    }
}
