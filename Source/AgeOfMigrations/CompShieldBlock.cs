using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace AgeOfMigrations
{
    /// <summary>Settings for a hand-held shield. Set per shield in its ThingDef.</summary>
    public class CompProperties_ShieldBlock : CompProperties
    {
        /// <summary>Chance to stop a bullet, arrow or thrown weapon coming from the front.</summary>
        public float rangedBlockChance = 0.7f;
        /// <summary>Chance to stop a melee hit coming from the front.</summary>
        public float meleeBlockChance = 0.2f;
        /// <summary>Hits within this many degrees of where the pawn faces can be blocked (90 = front and sides).</summary>
        public float blockArc = 100f;
        /// <summary>Share of a blocked hit's damage that goes into the shield's hit points.</summary>
        public float shieldDamageFactor = 0.4f;

        public CompProperties_ShieldBlock()
        {
            compClass = typeof(CompShieldBlock);
        }
    }

    /// <summary>
    /// Lets worn apparel block incoming hits. The game asks every piece of worn apparel whether it
    /// absorbs a hit before the pawn takes damage (the same hook the vanilla shield belt uses), so no
    /// Harmony patch is needed. The shield sits in its own apparel layer, so any weapon can still be used.
    /// </summary>
    public class CompShieldBlock : ThingComp
    {
        // True while we damage the shield itself, so that hit doesn't go through the blocking logic.
        private static bool damagingShield;

        private CompProperties_ShieldBlock Props => (CompProperties_ShieldBlock)props;

        private Pawn Wearer => (parent as Apparel)?.Wearer;

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            if (damagingShield)
                return;

            Pawn pawn = Wearer;
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || !pawn.Awake())
                return;

            DamageDef def = dinfo.Def;
            if (def == null || def.isExplosive || !def.harmsHealth)
                return;

            // Only physical hits (sharp or blunt). Fire, gas, surgery etc. pass through.
            StatDef armorStat = def.armorCategory?.armorRatingStat;
            if (armorStat != StatDefOf.ArmorRating_Sharp && armorStat != StatDefOf.ArmorRating_Blunt)
                return;

            if (dinfo.Instigator == pawn)
                return;

            if (!ComesFromFront(pawn, dinfo))
                return;

            float chance = IsRanged(dinfo) ? Props.rangedBlockChance : Props.meleeBlockChance;
            if (!Rand.Chance(chance))
                return;

            absorbed = true;
            OnBlocked(pawn, dinfo);
        }

        private static bool IsRanged(DamageInfo dinfo)
        {
            if (dinfo.Weapon != null && dinfo.Weapon.IsRangedWeapon)
                return true;
            string name = dinfo.Def.defName;
            return name == "Bullet" || name == "Arrow";
        }

        private bool ComesFromFront(Pawn pawn, DamageInfo dinfo)
        {
            Vector3 toAttacker;
            Thing attacker = dinfo.Instigator;
            if (attacker != null && attacker.Spawned && attacker.Map == pawn.Map && attacker.Position != pawn.Position)
            {
                toAttacker = attacker.DrawPos - pawn.DrawPos;
            }
            else if (dinfo.Angle >= 0f)
            {
                // dinfo.Angle is the direction the projectile travels; the attacker is the opposite way.
                toAttacker = -Vector3Utility.FromAngleFlat(dinfo.Angle);
            }
            else
            {
                return true; // no direction known: give the shield a chance
            }

            float attackerDir = toAttacker.AngleFlat();
            float facing = pawn.Rotation.AsAngle;
            return Mathf.Abs(Mathf.DeltaAngle(attackerDir, facing)) <= Props.blockArc;
        }

        private void OnBlocked(Pawn pawn, DamageInfo dinfo)
        {
            Map map = pawn.Map;
            IntVec3 pos = pawn.Position;
            MoteMaker.ThrowText(pawn.DrawPos + new Vector3(0f, 0f, 0.6f), map, "Blocked", 1.9f);

            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("Deflect_Metal")
                             ?? DefDatabase<SoundDef>.GetNamedSilentFail("MeleeHit_Metal_Sharp");
            sound?.PlayOneShot(new TargetInfo(pos, map));

            int shieldDamage = Mathf.Max(1, Mathf.RoundToInt(dinfo.Amount * Props.shieldDamageFactor));
            string shieldLabel = parent.LabelShort;
            damagingShield = true;
            try
            {
                parent.TakeDamage(new DamageInfo(DamageDefOf.Blunt, shieldDamage));
            }
            finally
            {
                damagingShield = false;
            }

            if (parent.Destroyed && pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message($"{pawn.LabelShort}'s {shieldLabel} broke apart.", new LookTargets(pawn),
                    MessageTypeDefOf.NegativeEvent);
            }
        }
    }
}
