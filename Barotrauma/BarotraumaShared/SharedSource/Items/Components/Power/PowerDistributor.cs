#nullable enable
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    internal partial class PowerDistributor : PowerTransfer
    {
        private const int MaxNameLength = 32;

        private const int SupplyRatioSteps = 20;
        private const float SupplyRatioStep = 1f / SupplyRatioSteps;

        private partial class PowerGroup
        {
            private readonly PowerDistributor distributor;
            public readonly Connection PowerOut;
            public readonly Connection? RatioInput, RatioOutput;

            private string name;
            public string Name
            {
                get => name;
                set
                {
                    name = value;
                    DisplayName = TextManager.Get(name).Fallback(name);
#if CLIENT
                    UpdateNameBox();
#endif
                }
            }

            public LocalizedString? DisplayName { get; private set; }

            private float supplyRatio = 1f;
            public float SupplyRatio
            {
                get => supplyRatio;
                set
                {
                    if (!MathUtils.IsValid(value)) { return; }
                    supplyRatio = MathUtils.RoundTowardsClosest(MathHelper.Clamp(value, 0f, 1f), SupplyRatioStep);
#if CLIENT
                    UpdateSlider();
#endif
                }
            }

            public float DisplayRatio
            {
                get => MathUtils.RoundToInt(supplyRatio * 100);
                set => SupplyRatio = value / 100f;
            }

            public float Load;
            public float ModifiedLoad => Load * SupplyRatio;

            public PowerGroup(PowerDistributor distributor, Connection power, XElement? element = null, Connection? ratioInput = null, Connection? ratioOutput = null)
            {
                this.distributor = distributor;
                PowerOut = power;

                RatioInput = ratioInput;
                RatioOutput = ratioOutput;

                distributor.powerGroups.Add(this);

                name = TextManager.GetWithVariable("groupx", "[num]", distributor.powerGroups.Count.ToString()).Value;
                SupplyRatio = 1f;

                if (element != null)
                {
                    name = element.GetAttributeString("name", name);
                    SupplyRatio = element.GetAttributeFloat("ratio", SupplyRatio);
                }

#if CLIENT
                CreateGUI();
                if (Screen.Selected is not { IsEditor: true })
                {
                    //set text via the property to refresh the UI
                    Name = name;
                }
#endif
            }

            #region Signals
            public void ReceiveRatioSignal(Signal signal)
            {
                if (!float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float receivedSignal) || !MathUtils.IsValid(receivedSignal)) { return; }
                DisplayRatio = receivedSignal;
            }

            public void SendRatioSignal() => distributor.item.SendSignal(new Signal(DisplayRatio.ToString()), RatioOutput);
            #endregion
        }

        private readonly List<PowerGroup> powerGroups = new List<PowerGroup>();

        protected override PowerPriority Priority => PowerPriority.Relay;

        public PowerDistributor(Item item, ContentXElement element) : base(item, element) { }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();

            IEnumerable<Connection> ratioInputs = Item.Connections.Where(static conn => !conn.IsOutput && conn.Name.StartsWith("set_supply_ratio"));
            IEnumerable<Connection> ratioOutputs = Item.Connections.Where(static conn => conn.IsOutput && conn.Name.StartsWith("supply_ratio_out"));

            for (int i = 0; i < powerOuts.Count; i++)
            {
                new PowerGroup(this, powerOuts[i], cachedGroupData.ElementAtOrDefault(i), ratioInputs.ElementAtOrDefault(i), ratioOutputs.ElementAtOrDefault(i));
            }

            cachedGroupData.Clear();
        }

        public override void Clone(ItemComponent original)
        {
            if (original is not PowerDistributor originalPowerDistributor) { return; }
            for (int i = 0; i < powerOuts.Count; i++)
            {
                powerGroups[i].SupplyRatio = originalPowerDistributor.powerGroups[i].SupplyRatio;
                powerGroups[i].Name = originalPowerDistributor.powerGroups[i].Name;
            }
        }

        #region Signals
        protected override void SendSignals()
        {
            item.SendSignal(MathUtils.RoundToInt(powerIn.Grid?.Power ?? 0f).ToString(), "power_value_out");
            item.SendSignal(MathUtils.RoundToInt(GetCurrentPowerConsumption(powerIn)).ToString(), "load_value_out");
            powerGroups.ForEach(static group => group.SendRatioSignal());
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (item.Condition <= 0f || connection.IsPower) { return; }
            if (connection.IsOutput) { return; }

            powerGroups.FirstOrDefault(group => group.RatioInput == connection)?.ReceiveRatioSignal(signal);
        }
        #endregion

        #region Power Calculation
        private bool IsShortCircuited(Connection conn) => powerIn.Grid == conn.Grid;

        public override float GetCurrentPowerConsumption(Connection? connection = null)
        {
            if (connection != powerIn) { return -1f; }
            if (isBroken) { return 0f; }
            return powerGroups.Sum(group => IsShortCircuited(group.PowerOut) ? 0f : group.ModifiedLoad) + ExtraLoad;
        }

        private float CalculatePowerOut(PowerGroup group)
        {
            if (isBroken || powerIn.Grid == null || IsShortCircuited(group.PowerOut)) { return 0f; }
            return Math.Max(group.ModifiedLoad * Voltage, 0f);
        }

        public override float GetConnectionPowerOut(Connection connection, float power, PowerRange minMaxPower, float load)
        {
            if (connection == powerIn) { return 0f; }
            PowerGroup group = powerGroups.First(group => group.PowerOut == connection);
            group.Load = load;
            return CalculatePowerOut(group);
        }
        #endregion

        #region Serialization
        private readonly List<XElement> cachedGroupData = new List<XElement>();

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);
            foreach (PowerGroup powerGroup in powerGroups)
            {
                componentElement.Add(new XElement("PowerGroup",
                    new XAttribute("name", powerGroup.Name),
                    new XAttribute("ratio", powerGroup.SupplyRatio)));
            }
            return componentElement;
        }

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
        {
            base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);
            if (usePrefabValues) { return; }

            foreach (XElement element in componentElement.Elements())
            {
                cachedGroupData.Add(element);
            }
        }
        #endregion

        #region Networking
        private enum EventType { NameChange, RatioChange }

        private void SharedEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null)
        {
            EventData data = ExtractEventData<EventData>(extraData);
            msg.WriteRangedInteger((int)data.EventType, 0, 1);
            msg.WriteRangedInteger(powerGroups.IndexOf(data.PowerGroup), 0, powerGroups.Count - 1);
            switch (data.EventType)
            {
                case EventType.NameChange:
                    msg.WriteString(data.PowerGroup.Name);
                    break;
                case EventType.RatioChange:
                    msg.WriteRangedInteger(MathUtils.RoundToInt(data.PowerGroup.SupplyRatio / SupplyRatioStep), 0, SupplyRatioSteps);
                    break;
            }
        }

        private void SharedEventRead(IReadMessage msg, out EventType eventType, out PowerGroup powerGroup, out string newName, out float newRatio)
        {
            eventType = (EventType)msg.ReadRangedInteger(0, 1);
            powerGroup = powerGroups[msg.ReadRangedInteger(0, powerGroups.Count - 1)];

            newName = eventType == EventType.NameChange ? string.Concat(msg.ReadString().Take(MaxNameLength)) : powerGroup.Name;
            newRatio = eventType == EventType.RatioChange ? msg.ReadRangedInteger(0, SupplyRatioSteps) * SupplyRatioStep : powerGroup.SupplyRatio;
        }

        private readonly record struct EventData(PowerGroup PowerGroup, EventType EventType) : IEventData;
        #endregion
    }
}