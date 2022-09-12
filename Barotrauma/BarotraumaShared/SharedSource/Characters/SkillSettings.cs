using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class SkillSettings : Prefab, ISerializableEntity
    {
        public readonly static PrefabSelector<SkillSettings> Prefabs = new PrefabSelector<SkillSettings>();
        public static SkillSettings Current => Prefabs.ActivePrefab;

        [Serialize(4.0f, IsPropertySaveable.Yes)]
        public float SingleRoundSkillGainMultiplier { get; set; }
        

        private float skillIncreasePerRepair;
        [Serialize(5.0f, IsPropertySaveable.Yes)]
        public float SkillIncreasePerRepair
        {
            get { return skillIncreasePerRepair * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerRepair = value; }
        }

        private float skillIncreasePerSabotage;
        [Serialize(3.0f, IsPropertySaveable.Yes)]
        public float SkillIncreasePerSabotage
        {
            get { return skillIncreasePerSabotage * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerSabotage = value; }
        }

        private float skillIncreasePerCprRevive;
        [Serialize(0.5f, IsPropertySaveable.Yes)]
        public float SkillIncreasePerCprRevive
        {
            get { return skillIncreasePerCprRevive * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerCprRevive = value; }
        }

        private float skillIncreasePerRepairedStructureDamage;
        [Serialize(0.0025f, IsPropertySaveable.Yes)]
        public float SkillIncreasePerRepairedStructureDamage
        {
            get { return skillIncreasePerRepairedStructureDamage * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerRepairedStructureDamage = value; }
        }

        private float skillIncreasePerSecondWhenSteering;
        [Serialize(0.005f, IsPropertySaveable.Yes)]
        public float SkillIncreasePerSecondWhenSteering
        {
            get { return skillIncreasePerSecondWhenSteering * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerSecondWhenSteering = value; }
        }

        private float skillIncreasePerFabricatorRequiredSkill;
        [Serialize(0.5f, IsPropertySaveable.Yes)]
        public float SkillIncreasePerFabricatorRequiredSkill
        {
            get { return skillIncreasePerFabricatorRequiredSkill * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerFabricatorRequiredSkill = value; }
        }

        private float skillIncreasePerHostileDamage;
        [Serialize(0.01f, IsPropertySaveable.Yes)]
        public float SkillIncreasePerHostileDamage
        {
            get { return skillIncreasePerHostileDamage * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerHostileDamage = value; }
        }

        private float skillIncreasePerSecondWhenOperatingTurret;
        [Serialize(0.001f, IsPropertySaveable.Yes)]
        public float SkillIncreasePerSecondWhenOperatingTurret
        {
            get { return skillIncreasePerSecondWhenOperatingTurret * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerSecondWhenOperatingTurret = value; }
        }

        private float skillIncreasePerFriendlyHealed;
        [Serialize(0.001f, IsPropertySaveable.Yes)]
        public float SkillIncreasePerFriendlyHealed
        {
            get { return skillIncreasePerFriendlyHealed * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerFriendlyHealed = value; }
        }

        [Serialize(1.1f, IsPropertySaveable.Yes)]
        public float AssistantSkillIncreaseMultiplier
        {
            get;
            set;
        }

        [Serialize(200.0f, IsPropertySaveable.Yes)]
        public float MaximumSkillWithTalents
        {
            get;
            set;
        }

        public SkillSettings(XElement element, SkillSettingsFile file) : base(file, element.FromContent(file.Path))
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

		protected override Identifier DetermineIdentifier(XElement element)
		{
            return "SkillSettings".ToIdentifier();
		}

		public string Name => "SkillSettings";

        public Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get;
            set;
        }

        private float GetCurrentSkillGainMultiplier()
        {
            if (GameMain.GameSession?.GameMode is CampaignMode)
            {
                return 1.0f;
            }
            else
            {
                return SingleRoundSkillGainMultiplier;
            }
        }

        public override void Dispose() { }
    }
}
