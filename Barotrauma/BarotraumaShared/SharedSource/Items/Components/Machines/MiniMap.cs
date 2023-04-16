using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class MiniMap : Powered
    {
        internal class HullData
        {
            public float? HullOxygenAmount,
                          HullWaterAmount;

            public float? ReceivedOxygenAmount,
                          ReceivedWaterAmount;

            public double LastOxygenDataTime, LastWaterDataTime;

            public readonly HashSet<IdCard> Cards = new HashSet<IdCard>();

            public bool Distort;
            public float DistortionTimer;

            public List<Hull> LinkedHulls = new List<Hull>();
        }

        private bool hasPower;

        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "Does the machine require inputs from water detectors in order to show the water levels inside rooms.")]
        public bool RequireWaterDetectors
        {
            get;
            set;
        }

        [Editable, Serialize(true, IsPropertySaveable.Yes, description: "Does the machine require inputs from oxygen detectors in order to show the oxygen levels inside rooms.")]
        public bool RequireOxygenDetectors
        {
            get;
            set;
        }

        [Editable, Serialize(true, IsPropertySaveable.Yes, description: "Should damaged walls be displayed by the machine.")]
        public bool ShowHullIntegrity
        {
            get;
            set;
        }

        [Editable, Serialize(true, IsPropertySaveable.Yes, description: "Enable hull status mode.")]
        public bool EnableHullStatus
        {
            get;
            set;
        }

        [Editable, Serialize(true, IsPropertySaveable.Yes, description: "Enable electrical view mode.")]
        public bool EnableElectricalView
        {
            get;
            set;
        }

        [Editable, Serialize(true, IsPropertySaveable.Yes, description: "Enable item finder mode.")]
        public bool EnableItemFinder
        {
            get;
            set;
        }

        public MiniMap(Item item, ContentXElement element)
            : base(item, element)
        {
            IsActive = true;
            InitProjSpecific();
        }

        partial void InitProjSpecific();

        public override void Update(float deltaTime, Camera cam)
        {
            hasPower = Voltage > MinVoltage;
            if (hasPower)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }
        }

        /// <summary>
        /// Power consumption of the MiniMap. Only consume power when active and adjust consumption based on condition.
        /// </summary>
        public override float GetCurrentPowerConsumption(Connection connection = null)
        {
            if (connection != powerIn || !IsActive)
            {
                return 0;
            }

            return PowerConsumption * MathHelper.Lerp(1.5f, 1.0f, item.Condition / item.MaxCondition);
        }

        public override bool Pick(Character picker)
        {
            return picker != null;
        }
    }
}
