using System;
using System.Collections.Generic;
using System.Reflection;
using BattleTech;
using BattleTech.UI;
using Harmony;
using HBS;
using SVGImporter;
using HBS.Logging;
using UnityEngine;
using Newtonsoft.Json;

namespace CBTHeat
{
    [HarmonyPatch(typeof(Mech), "Init")]
    public static class Mech_Init_Patch
    {
        private static void Postfix(Mech __instance, Vector3 position, float facing, bool checkEncounterCells)
        {
            __instance.StatCollection.AddStatistic<int>("TurnsOverheated", 0);
        }
    }

    [HarmonyPatch(typeof(ToHit), "GetHeatModifier")]
    public static class ToHit_GetHeatModifier_Patch
    {
        private static void Postfix(ToHit __instance, ref float __result, AbstractActor attacker)
        {
            if (attacker is Mech)
            {
                Mech mech = (Mech)attacker;

                int turnsOverheated = mech.StatCollection.GetValue<int>("TurnsOverheated");
                if (turnsOverheated > 0)
                {
                    float modifier = CBTHeat.getHeatToHitModifierForTurn(turnsOverheated);

                    __result = modifier;
                } else {
                    __result = 0f;
                }
            }
        }
    }

    [HarmonyPatch(typeof(CombatHUDStatusPanel), "ShowShutDownIndicator", null)]
    public static class CombatHUDStatusPanel_ShowShutDownIndicator_Patch
    {
        private static bool Prefix(AbstractActor __instance)
        {
            return false;
        }

        private static void Postfix(CombatHUDStatusPanel __instance, Mech mech)
        {
            var type = __instance.GetType();
            MethodInfo methodInfo = type.GetMethod("ShowDebuff", (BindingFlags.NonPublic | BindingFlags.Instance), null, new Type[] { typeof(SVGAsset), typeof(string), typeof(string), typeof(Vector3), typeof(bool) }, new ParameterModifier[5]);
            int turnsOverheated = mech.StatCollection.GetValue<int>("TurnsOverheated");

            if (mech.IsShutDown)
            {
                methodInfo.Invoke(__instance, new object[] { LazySingletonBehavior<UIManager>.Instance.UILookAndColorConstants.StatusShutDownIcon, "SHUT DOWN", "This target is easier to hit, and Called Shots can be made against this target.", __instance.defaultIconScale, false });
            }
            else if (mech.IsOverheated)
            {
                string descr = string.Format("This unit is in danger of Shutting Down and having Ammo Explode each turn while Overheating.\nShutdown Chance: {0:P2}\nAmmo Explosion Chance: {1:P2}", CBTHeat.getShutdownPercentageForTurn(turnsOverheated), CBTHeat.getAmmoExplosionPercentageForTurn(turnsOverheated));
                methodInfo.Invoke(__instance, new object[] { LazySingletonBehavior<UIManager>.Instance.UILookAndColorConstants.StatusOverheatingIcon, "OVERHEATING", descr, __instance.defaultIconScale, false });
            }
        }
    }

    [HarmonyPatch(typeof(Mech), "OnActivationEnd")]
    public static class Mech_OnActivationEnd_Patch
    {
        private static void Prefix(Mech __instance, string sourceID, int stackItemID)
        {
            ILog heatLogger = HBS.Logging.Logger.GetLogger("CombatLog.Heat", LogLevel.Warning);

            if (heatLogger.IsLogEnabled)
            {
                heatLogger.Log(string.Format("[CBTHeat] Is Overheated: {0}", __instance.IsOverheated));
            }

            if (__instance.IsOverheated)
            {
                float skillRoll = __instance.Combat.NetworkRandom.Float();
                float ammoRoll = __instance.Combat.NetworkRandom.Float();

                int turnsOverheated = __instance.StatCollection.GetValue<int>("TurnsOverheated");
                float shutdownPercentage = CBTHeat.getShutdownPercentageForTurn(turnsOverheated);
                float ammoExplosionPercentage = CBTHeat.getAmmoExplosionPercentageForTurn(turnsOverheated);

                if (heatLogger.IsLogEnabled)
                {
                    heatLogger.Log(string.Format("[CBTHeat] Turns Overheated: {0}", turnsOverheated));
                    heatLogger.Log(string.Format("[CBTHeat] Intiating Shutdown Override Check"));
                    heatLogger.Log(string.Format("[CBTHeat] Skill Roll: {0}", skillRoll));
                    heatLogger.Log(string.Format("[CBTHeat] Ammo Roll: {0}", ammoRoll));
                    heatLogger.Log(string.Format("[CBTHeat] Skill Target: {0}", shutdownPercentage));
                    heatLogger.Log(string.Format("[CBTHeat] Ammo Roll Target: {0}", ammoExplosionPercentage));
                }

                CameraControl.Instance.ShowDeathCam(__instance, false, -1f);
                             
                if (ammoRoll < ammoExplosionPercentage)
                {
                    CameraControl.Instance.ShowDeathCam(__instance, false, -1f);

                    __instance.Combat.MessageCenter.PublishMessage(new FloatieMessage(__instance.GUID, __instance.GUID, "Ammo Overheated!", FloatieMessage.MessageNature.CriticalHit));

                    foreach (AmmunitionBox ammoBox in __instance.ammoBoxes)
                    {
                        WeaponHitInfo fakeHit = new WeaponHitInfo(stackItemID, -1, -1, -1, string.Empty, string.Empty, -1, null, null, null, null, null, null, null, AttackDirection.None, Vector2.zero, null);

                        Vector3 onUnitSphere = UnityEngine.Random.onUnitSphere;
                        __instance.NukeStructureLocation(fakeHit, ammoBox.Location, (ChassisLocations)ammoBox.Location, onUnitSphere, true);
                        ChassisLocations dependentLocation = MechStructureRules.GetDependentLocation((ChassisLocations)ammoBox.Location);

                        if (dependentLocation != ChassisLocations.None && !((Mech)ammoBox.parent).IsLocationDestroyed(dependentLocation))
                        {
                            ((Mech)ammoBox.parent).NukeStructureLocation(fakeHit, ammoBox.Location, dependentLocation, onUnitSphere, true);
                        }
                    }
                } 
                else
                {
                    CameraControl.Instance.ShowDeathCam(__instance, false, -1f);

                    __instance.Combat.MessageCenter.PublishMessage(new FloatieMessage(__instance.GUID, __instance.GUID, "Ammo Explosion Avoided!", FloatieMessage.MessageNature.Debuff));

                    if (!__instance.IsPastMaxHeat)
                    {
                        __instance.Combat.MessageCenter.PublishMessage(new FloatieMessage(__instance.GUID, __instance.GUID, "Emergency Shutdown Initiated!", FloatieMessage.MessageNature.Debuff));

                        if (skillRoll < shutdownPercentage)
                        {
                            if (heatLogger.IsLogEnabled)
                            {
                                heatLogger.Log(string.Format("[CBTHeat] Skill Check Failed! Initiating Shutdown"));
                            }

                            CameraControl.Instance.ShowDeathCam(__instance, false, -1f);

                            MechHeatSequence previousSequence = new MechHeatSequence(__instance, false, false, null);
                            __instance.GenerateOverheatedSequence(previousSequence);

                            __instance.StatCollection.Set<int>("TurnsOverheated", 0);
                        }
                        else
                        {
                            if (heatLogger.IsLogEnabled)
                            {
                                heatLogger.Log(string.Format("[CBTHeat] Skill Check Succeeded!"));
                            }

                            CameraControl.Instance.ShowDeathCam(__instance, false, -1f);

                            __instance.Combat.MessageCenter.PublishMessage(new FloatieMessage(__instance.GUID, __instance.GUID, "Shutdown Override Succeeded!", FloatieMessage.MessageNature.Buff));

                            turnsOverheated += 1;
                            __instance.StatCollection.Set<int>("TurnsOverheated", turnsOverheated);
                        }
                    }
                }

                __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(new DelaySequence(__instance.Combat, 2f)));
            }
        }
    }

    internal class ModSettings
    {
        [JsonProperty("ShutdownPercentages")]
        //public IList<float> ShutdownPercentages = new List<float>() { 0.083f, 0.278f, 0.583f, 0.833f };
        public IList<float> ShutdownPercentages { get; set; }

        [JsonProperty("AmmoExplosionPercentages")]
        //public IList<float> AmmoExplosionPercentages = new List<float>() { 0f, 0.083f, 0.278f, 0.583f };
        public IList<float> AmmoExplosionPercentages { get; set; }

        [JsonProperty("HeatToHitModifiers")]
        //public IList<int> HeatToHitModifiers = new List<int>() { 1, 2, 3, 4 };
        public IList<int> HeatToHitModifiers { get; set; }
    }

    public static class CBTHeat
    {
        internal static ModSettings Settings;

        public static void Init(string modDir, string modSettings)
        {
            var harmony = HarmonyInstance.Create("io.github.guetler.CBTHeat");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            try
            {
                Settings = JsonConvert.DeserializeObject<ModSettings>(modSettings);
            }
            catch (Exception)
            {
                Settings = new ModSettings();
            }
        }

        public static float getShutdownPercentageForTurn(int turn)
        {
            int count = CBTHeat.Settings.ShutdownPercentages.Count;

            if (turn <= 0)
            {
                return CBTHeat.Settings.ShutdownPercentages[0];
            }

            if (turn > count - 1)
            {
                turn = count - 1;
            }

            return CBTHeat.Settings.ShutdownPercentages[turn];
        }

        public static float getAmmoExplosionPercentageForTurn(int turn)
        {
            int count = CBTHeat.Settings.AmmoExplosionPercentages.Count;

            if (turn <= 0)
            {
                return CBTHeat.Settings.AmmoExplosionPercentages[0];
            }

            if (turn > count - 1)
            {
                turn = count - 1;
            }

            return CBTHeat.Settings.AmmoExplosionPercentages[turn];
        }

        public static float getHeatToHitModifierForTurn(int turn)
        {
            int count = CBTHeat.Settings.HeatToHitModifiers.Count;

            if (turn <= 0)
            {
                return (float)CBTHeat.Settings.HeatToHitModifiers[0];
            }

            if (turn > count)
            {
                turn = count;
            }

            return (float)CBTHeat.Settings.HeatToHitModifiers[turn - 1];
        }
    }
}