using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class SkillSettings : ISerializableEntity
    {
        public static SkillSettings Current
        {
            get;
            private set;
        }

        [Serialize(4.0f, true)]
        public float SingleRoundSkillGainMultiplier { get; set; }
        

        private float skillIncreasePerRepair;
        [Serialize(5.0f, true)]
        public float SkillIncreasePerRepair
        {
            get { return skillIncreasePerRepair * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerRepair = value; }
        }

        private float skillIncreasePerSabotage;
        [Serialize(3.0f, true)]
        public float SkillIncreasePerSabotage
        {
            get { return skillIncreasePerSabotage * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerSabotage = value; }
        }

        private float skillIncreasePerCprRevive;
        [Serialize(0.5f, true)]
        public float SkillIncreasePerCprRevive
        {
            get { return skillIncreasePerCprRevive * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerCprRevive = value; }
        }

        private float skillIncreasePerRepairedStructureDamage;
        [Serialize(0.0025f, true)]
        public float SkillIncreasePerRepairedStructureDamage
        {
            get { return skillIncreasePerRepairedStructureDamage * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerRepairedStructureDamage = value; }
        }

        private float skillIncreasePerSecondWhenSteering;
        [Serialize(0.005f, true)]
        public float SkillIncreasePerSecondWhenSteering
        {
            get { return skillIncreasePerSecondWhenSteering * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerSecondWhenSteering = value; }
        }

        private float skillIncreasePerFabricatorRequiredSkill;
        [Serialize(0.5f, true)]
        public float SkillIncreasePerFabricatorRequiredSkill
        {
            get { return skillIncreasePerFabricatorRequiredSkill * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerFabricatorRequiredSkill = value; }
        }

        private float skillIncreasePerHostileDamage;
        [Serialize(0.01f, true)]
        public float SkillIncreasePerHostileDamage
        {
            get { return skillIncreasePerHostileDamage * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerHostileDamage = value; }
        }

        private float skillIncreasePerSecondWhenOperatingTurret;
        [Serialize(0.001f, true)]
        public float SkillIncreasePerSecondWhenOperatingTurret
        {
            get { return skillIncreasePerSecondWhenOperatingTurret * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerSecondWhenOperatingTurret = value; }
        }

        private float skillIncreasePerFriendlyHealed;
        [Serialize(0.001f, true)]
        public float SkillIncreasePerFriendlyHealed
        {
            get { return skillIncreasePerFriendlyHealed * GetCurrentSkillGainMultiplier(); }
            set { skillIncreasePerFriendlyHealed = value; }
        }

        [Serialize(1.1f, true)]
        public float AssistantSkillIncreaseMultiplier
        {
            get;
            set;
        }

        [Serialize(200.0f, true)]
        public float MaximumSkillWithTalents
        {
            get;
            set;
        }

        private SkillSettings(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public string Name => "SkillSettings";

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            set;
        }

        public static void Load(IEnumerable<ContentFile> files)
        {
            //reverse order to respect content package load order (last file overrides others)
            foreach (ContentFile file in files.Reverse())
            {
                if (file.Type != ContentType.SkillSettings)
                {
                    throw new ArgumentException();
                }

                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                if (doc == null) { continue; }

                Current = new SkillSettings(doc.Root);
                break;
            }

            if (Current == null)
            {
                DebugConsole.NewMessage("No skill settings found in the selected content packages. Using default values.");
                Current = new SkillSettings(null);
            }
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
    }
}
