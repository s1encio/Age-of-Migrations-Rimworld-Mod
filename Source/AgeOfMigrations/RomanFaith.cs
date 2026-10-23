using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AgeOfMigrations
{
    /// <summary>
    /// Gives each Roman faction's ideoligion its historical name and description.
    /// The beliefs themselves (Christian structure, memes, banned precepts) come from the FactionDefs;
    /// this only names them, so it also works on worlds that already exist.
    /// </summary>
    public class GameComponent_RomanFaith : GameComponent
    {
        public GameComponent_RomanFaith(Game game) { }

        public override void StartedNewGame()
        {
            Apply();
            MakeEnemies();
        }
        public override void LoadedGame() => Apply();

        private static void Apply()
        {
            if (!ModsConfig.IdeologyActive || Find.FactionManager == null || Find.IdeoManager == null)
                return;
            // Classic mode: everyone shares one generic belief system, so there is nothing to name.
            if (Find.IdeoManager.classicMode)
                return;

            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading.ToList())
            {
                if (faction.def.defName != "LR_WesternEmpire" && faction.def.defName != "LR_EasternEmpire" && faction.def.defName != "LR_Vandals" && faction.def.defName != "LR_Visigoths" && faction.def.defName != "LR_Huns")
                    continue;
                Ideo ideo = faction.ideos?.PrimaryIdeo;
                if (ideo == null)
                    continue;
                // Never rename a religion another faction (or the player) also follows: give this faction its own instead.
                if (SharedWithOthers(faction, ideo))
                    ideo = GiveOwnIdeo(faction, ideo);
                if (ideo == null)
                    continue;

                switch (faction.def.defName)
                {
                    case "LR_WesternEmpire":
                        Set(ideo, "Catholic Church", "Catholic", "Catholic",
                            "The Nicene Christian faith as taught by the bishop of Rome and the Latin fathers: Ambrose, " +
                            "Jerome and Augustine of Hippo. Made the religion of the state by the Edict of Thessalonica in 380, " +
                            "it worships one God in the Holy Trinity and holds its services in Latin. Its followers call their " +
                            "faith catholic, meaning universal, and look on the Arian Christianity of the Goths and Vandals as heresy.");
                        break;
                    case "LR_EasternEmpire":
                        Set(ideo, "Orthodox Church", "Orthodox", "Orthodox",
                            "The Nicene Christian faith of Constantinople, Alexandria and Antioch, as defined by the councils of " +
                            "Nicaea and Constantinople. It worships one God in the Holy Trinity and holds its services in Greek. " +
                            "Its followers call their faith orthodox, meaning right belief. The emperor calls its great councils " +
                            "and sees himself as the protector of the true faith.");
                        break;
                    case "LR_Huns":
                        Set(ideo, "Tengri", "Tengrist", "Tengrist",
                            "The old faith of the steppe: worship of Tengri, the eternal blue sky that sees everything, and reverence for " +
                            "the earth, the ancestors and the spirits of places. Shamans read the will of heaven in the cracks of burned " +
                            "shoulder-blades before a battle. The Huns tell how a herdsman found the sword of the war god sticking out of " +
                            "the ground and brought it to Attila, a sign that heaven had given him lordship over the world.");
                        break;
                    case "LR_Visigoths":
                        Set(ideo, "Gothic Church", "Gothic", "Gothic Arian",
                            "The Arian Christianity of the Goths, taught by their bishop Ulfilas, who invented an alphabet for their " +
                            "language to write the Bible in it. It holds that the Son was begotten by the Father and is like him, but " +
                            "not of the same substance. Its priests read and preach in Gothic, and its churches are the pride of " +
                            "the Gothic kings, who hang crowns of gold and jewels above their altars.");
                        break;
                    case "LR_Vandals":
                        Set(ideo, "Arian Church", "Arian", "Arian",
                            "Christianity as taught to the Goths by the bishop Ulfilas, who translated the Bible into Gothic, and passed " +
                            "on from them to the Vandals. It holds that the Son was made by the Father and is less than him, the creed " +
                            "of the council of Ariminum that Nicaea and Rome condemned. Its priests preach in the Germanic tongue, " +
                            "and its kings look on the Nicene churches of the Romans as heretics to be humbled, as the Romans do them.");
                        break;
                }
            }
        }

        private static bool SharedWithOthers(Faction faction, Ideo ideo)
        {
            foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
            {
                if (other != faction && other.ideos != null && other.ideos.PrimaryIdeo == ideo)
                    return true;
            }
            return false;
        }

        private static Ideo GiveOwnIdeo(Faction faction, Ideo old)
        {
            Ideo fresh = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(faction.def));
            if (fresh == null)
                return null;
            Find.IdeoManager.Add(fresh);
            faction.ideos.SetPrimary(fresh);
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
            {
                if (pawn.Faction == faction && pawn.ideo != null && pawn.Ideo == old)
                    pawn.ideo.SetIdeo(fresh);
            }
            return fresh;
        }

        /// <summary>At the start of a new game the Vandals are at war with both Roman empires, and the Visigoths with the Vandals.</summary>
        private static void MakeEnemies()
        {
            if (Find.FactionManager == null)
                return;
            var all = Find.FactionManager.AllFactionsListForReading;
            // the Huns are at war with both empires and with the Goths who fled from them
            foreach (Faction huns in all)
            {
                if (huns.def.defName != "LR_Huns")
                    continue;
                foreach (Faction other in all)
                {
                    string d = other.def.defName;
                    if (d != "LR_WesternEmpire" && d != "LR_EasternEmpire" && d != "LR_Visigoths")
                        continue;
                    SetHostile(huns, other);
                    SetHostile(other, huns);
                }
            }
            foreach (Faction vandals in all)
            {
                if (vandals.def.defName != "LR_Vandals")
                    continue;
                foreach (Faction other in all)
                {
                    string d = other.def.defName;
                    if (d != "LR_WesternEmpire" && d != "LR_EasternEmpire" && d != "LR_Visigoths")
                        continue;
                    SetHostile(vandals, other);
                    SetHostile(other, vandals);
                }
            }
        }

        private static void SetHostile(Faction a, Faction b)
        {
            FactionRelation rel = a.RelationWith(b, true);
            if (rel == null)
                return;
            rel.baseGoodwill = -80;
            rel.kind = FactionRelationKind.Hostile;
        }

        private static void Set(Ideo ideo, string name, string adjective, string member, string description)
        {
            ideo.name = name;
            ideo.memberName = member;
            // Set through Traverse so a renamed field in a future game version is skipped instead of breaking the build.
            Traverse t = Traverse.Create(ideo);
            t.Field("adjective").SetValue(adjective);
            t.Field("description").SetValue(description);
            t.Field("descriptionLocked").SetValue(true);
        }
    }
}
