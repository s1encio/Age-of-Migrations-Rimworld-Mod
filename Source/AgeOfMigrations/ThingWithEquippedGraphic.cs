using RimWorld;
using Verse;

namespace AgeOfMigrations
{
    /// <summary>Gives a weapon a different picture while a pawn holds it (e.g. a sling being swung).</summary>
    public class EquippedGraphicExtension : DefModExtension
    {
        public GraphicData equippedGraphicData;
    }

    public class ThingWithEquippedGraphic : ThingWithComps
    {
        private Graphic equippedGraphic;

        public override Graphic Graphic
        {
            get
            {
                if (ParentHolder is Pawn_EquipmentTracker)
                {
                    if (equippedGraphic == null)
                    {
                        GraphicData data = def.GetModExtension<EquippedGraphicExtension>()?.equippedGraphicData;
                        if (data != null)
                            equippedGraphic = data.GraphicColoredFor(this);
                    }
                    if (equippedGraphic != null)
                        return equippedGraphic;
                }
                return base.Graphic;
            }
        }
    }
}
