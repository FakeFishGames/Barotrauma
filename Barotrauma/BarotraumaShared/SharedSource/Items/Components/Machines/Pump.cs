using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Pump : Powered, IServerSerializable, IClientSerializable
    {
        private float flowPercentage;
        private float maxFlow;

        private float? targetLevel;

        private float pumpSpeedLockTimer, isActiveLockTimer;

        [Serialize(0.0f, true, description: "How fast the item is currently pumping water (-100 = full speed out, 100 = full speed in). Intended to be used by StatusEffect conditionals (setting this value in XML has no effect).")]
        public float FlowPercentage
        {
            get { return flowPercentage; }
            set
            {
                if (!MathUtils.IsValid(flowPercentage)) { return; }
                flowPercentage = MathHelper.Clamp(value, -100.0f, 100.0f);
                flowPercentage = MathUtils.Round(flowPercentage, 1.0f);
            }
        }

        [Editable, Serialize(80.0f, false, description: "How fast the item pumps water in/out when operating at 100%.", alwaysUseInstanceValues: true)]
        public float MaxFlow
        {
            get { return maxFlow; }
            set { maxFlow = value; } 
        }

        [Editable, Serialize(false, false, alwaysUseInstanceValues: true)]
        public bool IsOn
        {
            get { return IsActive; }
            set { IsActive = value; }
        }

        private float currFlow;
        public float CurrFlow
        {
            get
            {
                if (!IsActive) { return 0.0f; }
                return Math.Abs(currFlow);
            }
        }

        public bool HasPower => IsActive && Voltage >= MinVoltage;
        public bool IsAutoControlled => pumpSpeedLockTimer > 0.0f || isActiveLockTimer > 0.0f;

        public Pump(Item item, XElement element)
            : base(item, element)
        {
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);
        
        public override void Update(float deltaTime, Camera cam)
        {
            currFlow = 0.0f;

            if (targetLevel != null)
            {
                pumpSpeedLockTimer -= deltaTime;
                float hullPercentage = 0.0f;
                if (item.CurrentHull != null) { hullPercentage = (item.CurrentHull.WaterVolume / item.CurrentHull.Volume) * 100.0f; }
                FlowPercentage = ((float)targetLevel - hullPercentage) * 10.0f;
            }

            currPowerConsumption = powerConsumption * Math.Abs(flowPercentage / 100.0f);
            //pumps consume more power when in a bad condition
            item.GetComponent<Repairable>()?.AdjustPowerConsumption(ref currPowerConsumption);

            if (!HasPower) { return; }

            UpdateProjSpecific(deltaTime);

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (item.CurrentHull == null) { return; }      

            float powerFactor = Math.Min(currPowerConsumption <= 0.0f ? 1.0f : Voltage, 1.0f);

            currFlow = flowPercentage / 100.0f * maxFlow * powerFactor;
            //less effective when in a bad condition
            currFlow *= MathHelper.Lerp(0.5f, 1.0f, item.Condition / item.MaxCondition);

            item.CurrentHull.WaterVolume += currFlow;
            if (item.CurrentHull.WaterVolume > item.CurrentHull.Volume) { item.CurrentHull.Pressure += 0.5f; }
        }

        partial void UpdateProjSpecific(float deltaTime);
        
        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            if (connection.Name == "toggle")
            {
                IsActive = !IsActive;
                isActiveLockTimer = 0.1f;
            }
            else if (connection.Name == "set_active")
            {
                IsActive = signal != "0";
                isActiveLockTimer = 0.1f;
            }
            else if (connection.Name == "set_speed")
            {
                if (float.TryParse(signal, NumberStyles.Any, CultureInfo.InvariantCulture, out float tempSpeed))
                {
                    flowPercentage = MathHelper.Clamp(tempSpeed, -100.0f, 100.0f);
                    targetLevel = null;
                    pumpSpeedLockTimer = 0.1f;
                }
            }
            else if (connection.Name == "set_targetlevel")
            {
                if (float.TryParse(signal, NumberStyles.Any, CultureInfo.InvariantCulture, out float tempTarget))
                {
                    targetLevel = MathHelper.Clamp(tempTarget + 50.0f, 0.0f, 100.0f);
                    pumpSpeedLockTimer = 0.1f;
                }
            }
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
#if CLIENT
            if (GameMain.Client != null) { return false; }
#endif

            if (objective.Option.Equals("stoppumping", StringComparison.OrdinalIgnoreCase))
            {
#if SERVER
                if (objective.Override || FlowPercentage > 0.0f)
                {
                    item.CreateServerEvent(this);
                }
#endif
                IsActive = false;
                FlowPercentage = 0.0f;
            }
            else
            {
#if SERVER
                if (objective.Override || !IsActive || FlowPercentage > -100.0f)
                {
                    item.CreateServerEvent(this);
                }
#endif
                IsActive = true;
                FlowPercentage = -100.0f;
            }
            return true;
        }
    }
}
