using System;
using System.Linq;
using HarmonyLib;
using KSA;

namespace CharacterManager;

/// The game's private members this mod reaches by reflection. A game update can rename them; AccessTools.FieldRefAccess then
/// throws when AnimationExtensions is first used, inside the game's per-frame animation update. So they are checked once at
/// boot, and the animation extensions stay off (with an error in the log) when one is missing.
internal static class GameMembers
{
    private static readonly (Type Type, string Name)[] Animation =
    {
        (typeof(KittenRenderable), "_ladderAnim"), (typeof(KittenRenderable), "_lastLadderRailT"),
        (typeof(KittenRenderable), "_characterAvatar"), (typeof(KittenRenderable), "_walkPairSampler"),
        (typeof(KittenRenderable), "_runPairSampler"), (typeof(KittenRenderable), "_groundRunAnim"),
        (typeof(KittenRenderable), "_groundIdleAnim"), (typeof(KittenRenderable), "_groundWalkAnim"),
        (typeof(KittenRenderable), "_moonWalkAnim"), (typeof(KittenRenderable), "_moonRunAnim"),
        (typeof(KittenRenderable), "_jumpIntroAnim"), (typeof(KittenRenderable), "_jumpLandAnim"),
        (typeof(KittenRenderable), "_flailAnim"), (typeof(KittenRenderable), "LoadGroundAnim"), (typeof(Mod), "Assets"),
    };

    /// Every member the animation extensions need exists.
    internal static readonly bool AnimationOk = Check(Animation, "animation extensions");

    private static bool Check((Type Type, string Name)[] members, string feature)
    {
        string[] missing = members.Where(m => AccessTools.Field(m.Type, m.Name) == null && AccessTools.Method(m.Type, m.Name) == null)
            .Select(m => $"{m.Type.Name}.{m.Name}").ToArray();
        if (missing.Length > 0)
            Log.Error($"{feature} are off: this game version has no {string.Join(", ", missing)}");
        return missing.Length == 0;
    }

    /// Applies each patch class on its own, so one target a game update removed doesn't keep the others from applying.
    internal static void Patch(Harmony harmony)
    {
        foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(GameMembers).Assembly).Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0))
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception e)
            {
                Log.Error($"patch {type.Name} could not be applied (a game update?)", e);
            }
        }
    }
}
