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
            if (!ModsConfig.IdeologyActive || Find.FactionManager == null)
                return;

            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                Ideo ideo = faction.ideos?.PrimaryIdeo;
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

        /// <summary>At the start of a new game the Vandals are at war with both Roman empires.</summary>
        private static void MakeEnemies()
        {
            if (Find.FactionManager == null)
                return;
            var all = Find.FactionManager.AllFactionsListForReading;
            foreach (Faction vandals in all)
            {
                if (vandals.def.defName != "LR_Vandals")
                    continue;
                foreach (Faction rome in all)
                {
                    if (rome.def.defName != "LR_WesternEmpire" && rome.def.defName != "LR_EasternEmpire")
                        continue;
                    SetHostile(vandals, rome);
                    SetHostile(rome, vandals);
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
