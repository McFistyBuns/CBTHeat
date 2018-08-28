using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BattleTech;
using BattleTech.UI;
using HBS.Util;
using Harmony;
using HBS;
using Localize;
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
                    float modifier = CBTHeat.GetHeatToHitModifierForTurn(turnsOverheated);

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
            MethodInfo methodInfo = type.GetMethod("ShowDebuff", (BindingFlags.NonPublic | BindingFlags.Instance), null, new Type[] { typeof(SVGAsset), typeof(Text), typeof(Text), typeof(Vector3), typeof(bool) }, new ParameterModifier[5]);
            int turnsOverheated = mech.StatCollection.GetValue<int>("TurnsOverheated");

            if (mech.IsShutDown)
            {
                methodInfo.Invoke(__instance, new object[] { LazySingletonBehavior<UIManager>.Instance.UILookAndColorConstants.StatusShutDownIcon, new Text("SHUT DOWN", new object[0]), new Text("This target is easier to hit, and Called Shots can be made against this target.", new object[0]), __instance.defaultIconScale, false });
            }
            else if (mech.IsOverheated)
            {
                string descr = string.Format("This unit may trigger a Shutdown at the end of the turn unless heat falls below critical levels.\nShutdown Chance: {0:P2}\nAmmo Explosion Chance: {1:P2}", CBTHeat.GetShutdownPercentageForTurn(turnsOverheated), CBTHeat.GetAmmoExplosionPercentageForTurn(turnsOverheated));
                methodInfo.Invoke(__instance, new object[] { LazySingletonBehavior<UIManager>.Instance.UILookAndColorConstants.StatusOverheatingIcon, new Text("OVERHEATING", new object[0]), new Text(descr, new object[0]), __instance.defaultIconScale, false });
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
                CBTPilotingRules rules = new CBTPilotingRules(__instance.Combat);
                float gutsTestChance = rules.GetGutsModifier(__instance);
                float skillRoll = __instance.Combat.NetworkRandom.Float();
                float ammoRoll = __instance.Combat.NetworkRandom.Float();

                int turnsOverheated = __instance.StatCollection.GetValue<int>("TurnsOverheated");
                float shutdownPercentage = CBTHeat.GetShutdownPercentageForTurn(turnsOverheated);
                float ammoExplosionPercentage = CBTHeat.GetAmmoExplosionPercentageForTurn(turnsOverheated);

                if (heatLogger.IsLogEnabled)
                {
                    heatLogger.Log(string.Format("[CBTHeat] Turns Overheated: {0}", turnsOverheated));
                    heatLogger.Log(string.Format("[CBTHeat] Intiating Shutdown Override Check"));
                    heatLogger.Log(string.Format("[CBTHeat] Guts Skill: {0}", (float)__instance.SkillGuts));
                    heatLogger.Log(string.Format("[CBTHeat] Guts Divisor: {0}", CBTHeat.Settings.GutsDivisor));
                    heatLogger.Log(string.Format("[CBTHeat] Guts Bonus: {0}", gutsTestChance));
                    heatLogger.Log(string.Format("[CBTHeat] Skill Roll: {0}", skillRoll));
                    heatLogger.Log(string.Format("[CBTHeat] Skill + Guts Roll: {0}", skillRoll+gutsTestChance));
                    heatLogger.Log(string.Format("[CBTHeat] Ammo Roll: {0}", ammoRoll));
                    heatLogger.Log(string.Format("[CBTHeat] Ammo + Guts Roll: {0}", ammoRoll+gutsTestChance));
                    heatLogger.Log(string.Format("[CBTHeat] Skill Target: {0}", shutdownPercentage));
                    heatLogger.Log(string.Format("[CBTHeat] Ammo Roll Target: {0}", ammoExplosionPercentage));
                }

                if (CBTHeat.Settings.UseGuts)
                {
                    ammoRoll = ammoRoll + gutsTestChance;
                    skillRoll = skillRoll + gutsTestChance;
                }

                MultiSequence sequence = new MultiSequence(__instance.Combat);
                sequence.SetCamera(CameraControl.Instance.ShowDeathCam(__instance, false, -1f), 0);

                if (CBTHeat.CanAmmoExplode(__instance))
                {
                    if (ammoRoll < ammoExplosionPercentage)
                    {
                        __instance.Combat.MessageCenter.PublishMessage(new FloatieMessage(__instance.GUID, __instance.GUID, "Ammo Overheated!", FloatieMessage.MessageNature.CriticalHit));

                        var ammoBox = __instance.ammoBoxes.Where(box => box.CurrentAmmo > 0).OrderByDescending(box => box.CurrentAmmo / box.AmmoCapacity).FirstOrDefault();
                        if (ammoBox != null)
                        {
                            var fakeHit = new WeaponHitInfo(stackItemID, -1, -1, -1, string.Empty, string.Empty, -1, null, null, null, null, null, null, null, AttackDirection.None, Vector2.zero, null);
                            ammoBox.DamageComponent(fakeHit, ComponentDamageLevel.Destroyed, true);
                        }

                        return;
                    }

                    sequence.AddChildSequence(new ShowActorInfoSequence(__instance, "Ammo Explosion Avoided!", FloatieMessage.MessageNature.Debuff, true), sequence.ChildSequenceCount - 1);
                } 
                
                if (!__instance.IsPastMaxHeat)
                {
                    if (skillRoll < shutdownPercentage)
                    {
                        if (heatLogger.IsLogEnabled)
                        {
                            heatLogger.Log(string.Format("[CBTHeat] Skill Check Failed! Initiating Shutdown"));
                        }

                        MechEmergencyShutdownSequence mechShutdownSequence = new MechEmergencyShutdownSequence(__instance);
                        sequence.AddChildSequence(mechShutdownSequence, sequence.ChildSequenceCount - 1);

                        __instance.StatCollection.Set<int>("TurnsOverheated", 0);
                    }
                    else
                    {
                        if (heatLogger.IsLogEnabled)
                        {
                            heatLogger.Log(string.Format("[CBTHeat] Skill Check Succeeded!"));
                        }

                        sequence.AddChildSequence(new ShowActorInfoSequence(__instance, "Shutdown Override Successful!", FloatieMessage.MessageNature.Buff, true), sequence.ChildSequenceCount - 1);

                        turnsOverheated += 1;
                        __instance.StatCollection.Set<int>("TurnsOverheated", turnsOverheated);
                    }
                }

                sequence.AddChildSequence(new DelaySequence(__instance.Combat, 2f), sequence.ChildSequenceCount - 1);

                __instance.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(sequence));
            } else {
                int turnsOverheated = __instance.StatCollection.GetValue<int>("TurnsOverheated");

                if (turnsOverheated > 0)
                {
                    __instance.StatCollection.Set<int>("TurnsOverheated", 0);
                }
            }
        }
    }


    [HarmonyPatch(typeof(Mech))]
    [HarmonyPatch("MoveMultiplier", MethodType.Getter)]
    public static class Mech_MoveMultiplier_Patch
    {
        private static void Postfix(Mech __instance, ref float __result)
        {
            int turnsOverheated = __instance.StatCollection.GetValue<int>("TurnsOverheated");

            if (__instance.IsOverheated && turnsOverheated > 0)
            {
                __result -= CBTHeat.GetOverheatedMovePenaltyForTurn(turnsOverheated);
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

        [JsonProperty("UseGuts")]
        public bool UseGuts { get; set; }

        [JsonProperty("GutsDivisor")]
        public int GutsDivisor { get; set; }

        [JsonProperty("OverheatedMovePenalty")]
        //public IList<float> OverheatedMovePenalty = new List<float>() { 0.1f, 0.2f, 0.3f, 0.4f };
        public IList<float> OverheatedMovePenalty { get; set; }
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

        public static float GetShutdownPercentageForTurn(int turn)
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

        public static float GetAmmoExplosionPercentageForTurn(int turn)
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

        public static float GetOverheatedMovePenaltyForTurn(int turn)
        {
            int count = CBTHeat.Settings.OverheatedMovePenalty.Count;

            if (turn <= 0)
            {
                return (float)CBTHeat.Settings.OverheatedMovePenalty[0];
            }

            if (turn > count)
            {
                turn = count;
            }

            return CBTHeat.Settings.OverheatedMovePenalty[turn - 1];
        }

        public static float GetHeatToHitModifierForTurn(int turn)
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

        public static bool CanAmmoExplode(Mech mech) {
            if (mech.ammoBoxes.Count == 0)
            {
                return false;
            }

            int ammoCount = 0;

            foreach (var ammoBox in mech.ammoBoxes)
            {
                ammoCount += ammoBox.CurrentAmmo;
            }

            if (ammoCount > 0)
            {
                return true;
            }

            return false;
        }
    }

    public class CBTPilotingRules
    {
        private readonly CombatGameState combat;

        public CBTPilotingRules(CombatGameState combat)
        {
            this.combat = combat;
        }

        public float GetGutsModifier(AbstractActor actor)
        {
            Pilot pilot = actor.GetPilot();

            float num = (pilot != null) ? ((float)pilot.Guts) : 1f;
            float gutsDivisor = CBTHeat.Settings.GutsDivisor;
            return num / gutsDivisor;
        }
    }
}

namespace BattleTech {
    public class MechEmergencyShutdownSequence : MultiSequence
    {
        public MechEmergencyShutdownSequence(Mech mech) : base(mech.Combat)
        {
            this.OwningMech = mech;
            this.setState();
        }

        private Mech OwningMech { get; set; }

        private void setState()
        {
            Mech.heatLogger.Log("Mech " + this.OwningMech.DisplayName + " shuts down from overheating");
            this.OwningMech.IsShutDown = true;
            this.OwningMech.DumpAllEvasivePips();
        }

        public override void OnAdded()
        {
            base.OnAdded();
            if (this.OwningMech.GameRep != null)
            {
                string text = string.Format("MechOverheatSequence_{0}_{1}", base.RootSequenceGUID, base.SequenceGUID);
                AudioEventManager.CreateVOQueue(text, -1f, null, null);
                AudioEventManager.QueueVOEvent(text, VOEvents.Mech_Overheat_Shutdown, this.OwningMech);
                AudioEventManager.StartVOQueue(1f);
                this.OwningMech.GameRep.PlayVFX(1, this.OwningMech.Combat.Constants.VFXNames.heat_heatShutdown, true, Vector3.zero, false, -1f);
                this.AddChildSequence(new ShowActorInfoSequence(this.OwningMech, "Emergency Shutdown Initiated!", FloatieMessage.MessageNature.Debuff, true), this.ChildSequenceCount - 1);
                WwiseManager.PostEvent<AudioEventList_ui>(AudioEventList_ui.ui_overheat_alarm_3, WwiseManager.GlobalAudioObject, null, null);
                if (this.OwningMech.team.LocalPlayerControlsTeam)
                {
                    AudioEventManager.PlayAudioEvent("audioeventdef_musictriggers_combat", "friendly_overheating", null, null);
                }
            }
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
        }

        public override void OnSuspend()
        {
            base.OnSuspend();
        }

        public override void OnResume()
        {
            base.OnResume();
        }

        public override void OnComplete()
        {
            base.OnComplete();
        }

        public override bool IsValidMultiSequenceChild
        {
            get
            {
                return true;
            }
        }

        public override bool IsParallelInterruptable
        {
            get
            {
                return false;
            }
        }

        public override bool IsCancelable
        {
            get
            {
                return false;
            }
        }

        public override bool IsComplete
        {
            get
            {
                return base.IsComplete;
            }
        }

        public override int Size()
        {
            return 0;
        }

        public override bool ShouldSave()
        {
            return false;
        }
    }
}