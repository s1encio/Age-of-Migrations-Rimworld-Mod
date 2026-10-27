using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AgeOfMigrations
{
    /// <summary>A real faith of 395 offered as an ideoligion preset: its name, adjective, member name, structure and description.</summary>
    public class FaithPreset : DefModExtension
    {
        public string name, adjective, member, description;
        public MemeDef structure;
    }

    internal static class FaithPresets
    {
        public static FaithPreset Selected(Page_ChooseIdeoPreset page)
        {
            var t = Traverse.Create(page);
            if (t.Field("presetSelection").GetValue()?.ToString() != "Preset") return null;
            return (t.Field("selectedIdeo").GetValue() as IdeoPresetDef)?.GetModExtension<FaithPreset>();
        }
    }

    /// <summary>Choosing a real faith also fixes its structure (Christian, theist, animist), whatever the structure picker says.</summary>
    [HarmonyPatch]
    public static class Patch_FaithPreset_Structure
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Page_ChooseIdeoPreset), "DoNext");
        private static bool Prepare() => ModsConfig.IdeologyActive && TargetMethod() != null && AccessTools.Field(typeof(Page_ChooseIdeoPreset), "selectedStructure") != null;
        public static void Prefix(Page_ChooseIdeoPreset __instance)
        {
            FaithPreset f = FaithPresets.Selected(__instance);
            if (f?.structure != null) Traverse.Create(__instance).Field("selectedStructure").SetValue(f.structure);
        }
    }

    /// <summary>... and gives the new ideoligion the faith's real name and description.</summary>
    [HarmonyPatch]
    public static class Patch_FaithPreset_Name
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Page_ChooseIdeoPreset), "AssignIdeoToPlayer");
        private static bool Prepare() => ModsConfig.IdeologyActive && TargetMethod() != null;
        public static void Prefix(Page_ChooseIdeoPreset __instance, Ideo ideo)
        {
            FaithPreset f = FaithPresets.Selected(__instance);
            if (f == null || ideo == null) return;
            GameComponent_RomanFaith.Set(ideo, f.name, f.adjective, f.member, f.description);
        }
    }
}
