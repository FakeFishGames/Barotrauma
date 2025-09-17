﻿using Barotrauma.MapCreatures.Behavior;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Pump : Powered, IServerSerializable, IClientSerializable, IDeteriorateUnderStress
    {
        private float flowPercentage;
        private float maxFlow;

        public float? TargetLevel;

        private bool hijacked;
        public bool Hijacked
        {
            get { return hijacked; }
            set 
            {
                if (value == hijacked) { return; }
                hijacked = value;
#if SERVER
                if (!Submarine.Unloading)
                {
                    item.CreateServerEvent(this);
                }
#endif
            }
        }

        public float CurrentBrokenVolume
        {
            get
            {
                if (item.ConditionPercentage > 10.0f || !IsActive || Disabled) { return 0.0f; }
                return (1.0f - item.ConditionPercentage / 10.0f) * 100.0f;
            }
        }

        private float pumpSpeedLockTimer, isActiveLockTimer;

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "How fast the item is currently pumping water (-100 = full speed out, 100 = full speed in). Intended to be used by StatusEffect conditionals (setting this value in XML has no effect).")]
        public float FlowPercentage
        {
            get { return flowPercentage; }
            set
            {
                if (!MathUtils.IsValid(flowPercentage)) { return; }
                flowPercentage = MathHelper.Clamp(value, -100.0f, 100.0f);
                flowPercentage = MathF.Round(flowPercentage);
            }
        }

        [Editable, Serialize(80.0f, IsPropertySaveable.No, description: "How fast the item pumps water in/out when operating at 100%.", alwaysUseInstanceValues: true)]
        public float MaxFlow
        {
            get => maxFlow;
            set => maxFlow = value;
        }

        private bool disabled;
        [Serialize(false, IsPropertySaveable.Yes, description: "If true, the pump is unable to pump water.", alwaysUseInstanceValues: true)]
        public bool Disabled 
        {
            get => disabled;
            set
            {
                if (disabled == value) { return; }
                disabled = value;
#if SERVER
                //send a network update soon
                //don't force to 0 though so this doesn't lead to spam if the property is toggled rapidly
                networkUpdateTimer = Math.Min(networkUpdateTimer, 0.5f);
#endif
            }
        }

        [Editable, Serialize(true, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public bool IsOn
        {
            get { return IsActive; }
            set { IsActive = value; }
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool CanCauseLethalPressure { get; set; }

        private float currFlow;
        public float CurrFlow => IsActive ? Math.Abs(currFlow) : 0.0f;

        public bool IsHullFull => item.CurrentHull != null && item.CurrentHull.WaterVolume >= item.CurrentHull.Volume * Hull.MaxCompress;

        public override bool HasPower => IsActive && Voltage >= MinVoltage;
        public bool IsAutoControlled => pumpSpeedLockTimer > 0.0f || isActiveLockTimer > 0.0f;

        private const float TinkeringSpeedIncrease = 4.0f;

        public override bool UpdateWhenInactive => true;

        public float CurrentStress => IsActive ? Math.Abs(flowPercentage / 100.0f) : 0.0f;

        public Pump(Item item, ContentXElement element)
            : base(item, element)
        {
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(ContentXElement element);

        private readonly List<Hull> linkedHulls = [];

        public override void Update(float deltaTime, Camera cam)
        {
            pumpSpeedLockTimer -= deltaTime;
            isActiveLockTimer -= deltaTime;

            currFlow = 0f;

            if (item.CurrentHull == null)
            {
                if (TargetLevel != null) { FlowPercentage = 0f; }
                return;
            }

            if (TargetLevel != null)
            {
                float hullWaterVolume = item.CurrentHull.WaterVolume;
                float totalHullVolume = item.CurrentHull.Volume;

                linkedHulls.Clear();
                //hidden hulls still affect buoyancy, include them here
                item.CurrentHull.GetLinkedHulls(linkedHulls, includeHiddenHulls: true);
                foreach (var linkedHull in linkedHulls)
                {
                    hullWaterVolume += linkedHull.WaterVolume;
                    totalHullVolume += linkedHull.Volume;
                }
                float hullPercentage = hullWaterVolume / totalHullVolume * 100.0f;
                FlowPercentage = ((float)TargetLevel - hullPercentage) * 10.0f;
            }

            UpdateNetworking(deltaTime);

            if (!IsActive || Disabled) { return; }
            if (flowPercentage <= 0f && item.CurrentHull.WaterVolume <= 0f) { return; }

            float powerFactor = Math.Min(currPowerConsumption <= 0.0f || MinVoltage <= 0.0f ? 1.0f : Voltage, MaxOverVoltageFactor);

            currFlow = flowPercentage / 100.0f * MaxFlow * powerFactor;
            if (item.GetComponent<Repairable>() is { IsTinkering: true } repairable)
            {
                currFlow *= 1f + repairable.TinkeringStrength * TinkeringSpeedIncrease;
            }
            currFlow = item.StatManager.GetAdjustedValueMultiplicative(ItemTalentStats.PumpSpeed, currFlow);

            //less effective when in a bad condition
            currFlow *= MathHelper.Lerp(0.5f, 1.0f, item.Condition / item.MaxCondition);

            if (MathUtils.NearlyEqual(currFlow, 0f, epsilon: 0.01f))
            {
                currFlow = 0f; // Set to 0 for conditionals.
                return;
            }

            item.CurrentHull.WaterVolume += currFlow * deltaTime * Timing.FixedUpdateRate;

            if (flowPercentage > 0f && item.CurrentHull.WaterVolume > item.CurrentHull.Volume)
            {
                item.CurrentHull.Pressure += 30f * deltaTime;
                if (CanCauseLethalPressure) { item.CurrentHull.LethalPressure += Hull.PressureBuildUpSpeed * deltaTime; }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime);
            UpdateProjSpecific(deltaTime);
        }

        public void InfectBallast(Identifier identifier, bool allowMultiplePerShip = false)
        {
            Hull hull = item.CurrentHull;
            if (hull == null) { return; }

            if (!allowMultiplePerShip)
            {
                // if the ship is already infected then do nothing
                if (Hull.HullList.Where(h => h.Submarine == hull.Submarine).Any(h => h.BallastFlora != null)) { return; }
            }

            if (hull.BallastFlora != null) { return; }

            var ballastFloraPrefab = BallastFloraPrefab.Find(identifier);
            if (ballastFloraPrefab == null)
            {
                DebugConsole.ThrowError($"Failed to infect a ballast pump (could not find a ballast flora prefab with the identifier \"{identifier}\").\n" + Environment.StackTrace);
                return;
            }

            Vector2 offset = item.WorldPosition - hull.WorldPosition;
            hull.BallastFlora = new BallastFloraBehavior(hull, ballastFloraPrefab, offset, firstGrowth: true);

#if SERVER
            hull.BallastFlora.CreateNetworkMessage(new BallastFloraBehavior.SpawnEventData());
#endif
        }

        /// <summary>
        /// Power consumption of the Pump. Only consume power when active and adjust consumption based on condition.
        /// </summary>
        public override float GetCurrentPowerConsumption(Connection connection = null)
        {
            //There shouldn't be other power connections to this
            if (connection != this.powerIn || !IsActive || Disabled)
            {
                return 0;
            }
            
            currPowerConsumption = powerConsumption * Math.Abs(flowPercentage / 100.0f);
            //pumps consume more power when in a bad condition
            item.GetComponent<Repairable>()?.AdjustPowerConsumption(ref currPowerConsumption);

            return currPowerConsumption;
        }

        partial void UpdateProjSpecific(float deltaTime);
        
        partial void UpdateNetworking(float deltaTime);

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (Hijacked) { return; }

            if (connection.Name == "toggle")
            {
                IsActive = !IsActive;
                isActiveLockTimer = 0.1f;
            }
            else if (connection.Name == "set_active")
            {
                IsActive = signal.value != "0";
                isActiveLockTimer = 0.1f;
            }
            else if (connection.Name == "set_speed")
            {
                if (float.TryParse(signal.value, NumberStyles.Any, CultureInfo.InvariantCulture, out float tempSpeed))
                {
                    flowPercentage = MathHelper.Clamp(tempSpeed, -100.0f, 100.0f);
                    TargetLevel = null;
                    pumpSpeedLockTimer = 0.1f;
                }
            }
            else if (connection.Name == "set_targetlevel")
            {
                if (float.TryParse(signal.value, NumberStyles.Any, CultureInfo.InvariantCulture, out float tempTarget))
                {
                    TargetLevel = MathUtils.InverseLerp(-100.0f, 100.0f, tempTarget) * 100.0f;
                    pumpSpeedLockTimer = 0.1f;
                }
            }
        }

        public override bool CrewAIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
#if CLIENT
            if (GameMain.Client != null) { return false; }
#endif

            switch (objective.Option.Value.ToLowerInvariant())
            {
                case "pumpout":
#if SERVER
                    if (objective.Override || !IsActive || FlowPercentage > -100.0f)
                    {
                        item.CreateServerEvent(this);
                    }
#endif
                    IsActive = true;
                    FlowPercentage = -100.0f;
                    break;
                case "pumpin":
#if SERVER
                    if (objective.Override || !IsActive || FlowPercentage < 100.0f)
                    {
                        item.CreateServerEvent(this);
                    }
#endif
                    IsActive = true;
                    FlowPercentage = 100.0f;
                    break;
                case "stoppumping":
#if SERVER
                    if (objective.Override || FlowPercentage > 0.0f)
                    {
                        item.CreateServerEvent(this);
                    }
#endif
                    IsActive = false;
                    FlowPercentage = 0.0f;
                    break;
            }
            return true;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            linkedHulls.Clear();
        }
    }
}
