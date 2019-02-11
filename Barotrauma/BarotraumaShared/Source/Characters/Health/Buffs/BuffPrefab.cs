using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    class BuffPrefab
    {
        public class Effect
        {
            //this effect is applied when the strength is within this range
            public float MinStrength, MaxStrength;

            //how much the strength of the buff changes per second
            public readonly float StrengthChange = 0.0f;

            public string DialogFlag;

            //statuseffects applied on the character when the buff is active
            public readonly List<StatusEffect> StatusEffects = new List<StatusEffect>();

            public Effect(XElement element, string parentDebugName)
            {
                MinStrength = element.GetAttributeFloat("minstrength", 0);
                MaxStrength = element.GetAttributeFloat("maxstrength", 0);

                DialogFlag = element.GetAttributeString("dialogflag", "");

                StrengthChange = element.GetAttributeFloat("strengthchange", 0.0f);

                foreach (XElement subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "statuseffect":
                            StatusEffects.Add(StatusEffect.Load(subElement, parentDebugName));
                            break;
                    }
                }
            }
        }

        public static BuffPrefab DurationIncrease;
        public static BuffPrefab Haste;
        public static BuffPrefab HuskInfectionResistance;
        public static BuffPrefab PsychosisResistance;
        public static BuffPrefab PressureResistance;
        public static BuffPrefab Strengthen;

        public static List<BuffPrefab> List = new List<BuffPrefab>();

        //Arbitrary string that is used to identify the type of the buff.
        //Buffs with the same type stack up
        public readonly string BuffType;

        //Does the buff affect a specific limb or the whole character
        public readonly bool LimbSpecific;

        //If not a limb-specific buff, which limb is the indicator shown on in the health menu
        //(e.g. mental health buffs on head...)
        public readonly LimbType IndicatorLimb;

        public readonly string Identifier;

        public readonly string Name, Description;

        //how high the strength has to be for the buff to take affect
        public readonly float ActivationThreshold = 0.0f;
        //how high the strength has to be for the buff icon to be shown in the UI
        public readonly float ShowIconThreshold = 0.0f;
        public readonly float MaxStrength = 100.0f;

        //steam achievement given when the buff is gained on the controlled character
        public readonly string AchievementOnGained;

        public readonly Sprite Icon;
        public readonly Color IconColor;

        private List<Effect> effects = new List<Effect>();

        private readonly string typeName;

        private readonly ConstructorInfo constructor;

        public static void LoadAll(IEnumerable<string> filePaths)
        {
            foreach (string filePath in filePaths)
            {
                XDocument doc = XMLExtensions.TryLoadXml(filePath);
                if (doc == null || doc.Root == null) continue;

                foreach (XElement element in doc.Root.Elements())
                {
                    switch (element.Name.ToString().ToLowerInvariant())
                    {
                        case "durationincrease":
                            List.Add(DurationIncrease = new BuffPrefab(element, typeof(Buff)));
                            break;
                        case "haste":
                            List.Add(Haste = new BuffPrefab(element, typeof(Buff)));
                            break;
                        case "huskinfectionresistance":
                            List.Add(HuskInfectionResistance = new BuffPrefab(element, typeof(Buff)));
                            break;
                        case "psychosisresistance":
                            List.Add(PsychosisResistance = new BuffPrefab(element, typeof(Buff)));
                            break;
                        case "pressureresistance":
                            List.Add(PressureResistance = new BuffPrefab(element, typeof(Buff)));
                            break;
                        case "strengthen":
                            List.Add(Strengthen = new BuffPrefab(element, typeof(Buff)));
                            break;
                        default:
                            List.Add(new BuffPrefab(element));
                            break;
                    }
                }
            }

            if (DurationIncrease == null) DebugConsole.ThrowError("Buff \"Duration Increase\" not defined in the buff prefabs.");
            if (Haste == null) DebugConsole.ThrowError("Buff \"Haste\" not defined in the buff prefabs.");
            if (HuskInfectionResistance == null) DebugConsole.ThrowError("Buff \"Husk Infection Resistance\" not defined in the buff prefabs.");
            if (PsychosisResistance == null) DebugConsole.ThrowError("Buff \"Psychosis Resistance\" not defined in the buff prefabs.");
            if (PressureResistance == null) DebugConsole.ThrowError("Buff \"Pressure Resistance\" not defined in the buff prefabs.");
            if (Strengthen == null) DebugConsole.ThrowError("Buff \"Strengthen\" not defined in the buff prefabs.");
        }

        public BuffPrefab(XElement element, Type type = null)
        {
            typeName = type == null ? element.Name.ToString() : type.Name;

            Identifier = element.GetAttributeString("identifier", "");

            BuffType = element.GetAttributeString("type", "");
            Name = TextManager.Get("BuffName." + Identifier, true) ?? element.GetAttributeString("name", "");
            Description = TextManager.Get("BuffDescription." + Identifier, true) ?? element.GetAttributeString("description", "");

            LimbSpecific = element.GetAttributeBool("limbspecific", false);
            if (!LimbSpecific)
            {
                string indicatorLimbName = element.GetAttributeString("indicatorlimb", "Torso");
                if (!Enum.TryParse(indicatorLimbName, out IndicatorLimb))
                {
                    DebugConsole.ThrowError("Error in buff prefab " + Name + " - limb type \"" + indicatorLimbName + "\" not found.");
                }
            }

            ActivationThreshold = element.GetAttributeFloat("activationthreshold", 0.0f);
            ShowIconThreshold = element.GetAttributeFloat("showiconthreshold", ActivationThreshold);
            MaxStrength = element.GetAttributeFloat("maxstrength", 100.0f);

            AchievementOnGained = element.GetAttributeString("achievementongained", "");

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "icon":
                        Icon = new Sprite(subElement);
                        IconColor = subElement.GetAttributeColor("color", Color.White);
                        break;
                    case "effect":
                        effects.Add(new Effect(subElement, Name));
                        break;
                }
            }

            try
            {
                if (type == null)
                {
                    type = Type.GetType("Barotrauma." + typeName, true, true);
                    if (type == null)
                    {
                        DebugConsole.ThrowError("Could not find an buff class of the type \"" + typeName + "\".");
                        return;
                    }
                }
            }
            catch
            {
                DebugConsole.ThrowError("Could not find an buff class of the type \"" + typeName + "\".");
                return;
            }

            constructor = type.GetConstructor(new[] { typeof(BuffPrefab), typeof(float) });
        }

        public override string ToString()
        {
            return "BuffPrefab (" + Name + ")";
        }

        public Buff Instantiate(float strength, Character source = null)
        {
            object instance = null;
            try
            {
                instance = constructor.Invoke(new object[] { this, strength });
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError(ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString());
            }
            Buff buff = instance as Buff;
            buff.Source = source;
            return buff;
        }

        public Effect GetActiveEffect(float currentStrength)
        {
            foreach (Effect effect in effects)
            {
                if (currentStrength > effect.MinStrength && currentStrength <= effect.MaxStrength) return effect;
            }

            //if above the strength range of all effects, use the highest strength effect
            Effect strongestEffect = null;
            float largestStrength = currentStrength;
            foreach (Effect effect in effects)
            {
                if (currentStrength > effect.MaxStrength &&
                    (strongestEffect == null || effect.MaxStrength > largestStrength))
                {
                    strongestEffect = effect;
                    largestStrength = effect.MaxStrength;
                }
            }
            return strongestEffect;
        }
    }
}
