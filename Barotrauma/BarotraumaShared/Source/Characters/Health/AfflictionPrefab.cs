using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class AfflictionPrefab
    {
        public class Effect
        {
            //this effect is applied when the strength is within this range
            public float MinStrength, MaxStrength;

            public readonly float MaxVitalityDecrease = 100.0f;

            //how much the strength of the affliction changes per second
            public readonly float StrengthChange = 0.0f;

            //statuseffects applied on the character when the affliction is active
            public readonly List<StatusEffect> StatusEffects = new List<StatusEffect>();

            public Effect(XElement element)
            {
                MinStrength =  element.GetAttributeFloat("minstrength", 0);
                MaxStrength =  element.GetAttributeFloat("maxstrength", 0);

                MaxVitalityDecrease = element.GetAttributeFloat("maxvitalitydecrease", 100.0f);
                StrengthChange = element.GetAttributeFloat("strengthchange", 0.0f);

                foreach (XElement subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "statuseffect":
                            StatusEffects.Add(StatusEffect.Load(subElement));
                            break;
                    }
                }
            }
        }

        public static AfflictionPrefab InternalDamage;
        public static AfflictionPrefab Bleeding;
        public static AfflictionPrefab Burn;
        public static AfflictionPrefab OxygenLow;
        public static AfflictionPrefab Bloodloss;

        public static List<AfflictionPrefab> List = new List<AfflictionPrefab>();

        //Arbitrary string that is used to identify the type of the affliction.
        //Afflictions with the same type stack up, and items may be configured to cure specific types of afflictions.
        public readonly string AfflictionType;

        //Does the affliction affect a specific limb or the whole character
        public readonly bool LimbSpecific;

        //If not a limb-specific affliction, which limb is the indicator shown on in the health menu
        //(e.g. mental health problems on head, lack of oxygen on torso...)
        public readonly LimbType IndicatorLimb;

        public readonly string Name, Description;

        //how high the strength has to be for the affliction to take affect
        public readonly float ActivationThreshold = 0.0f;
        public readonly float MaxStrength = 100.0f;

        public float BurnOverlayAlpha;
        public float DamageOverlayAlpha;

        public readonly Sprite Icon;

        private List<Effect> effects = new List<Effect>();


        private readonly string typeName;

        private readonly ConstructorInfo constructor;

        public static void Init()
        {
            //TODO: load from content package
            XDocument doc = XMLExtensions.TryLoadXml("Content/Afflictions.xml");

            foreach (XElement element in doc.Root.Elements())
            {
                switch (element.Name.ToString().ToLowerInvariant())
                {
                    case "internaldamage":
                        List.Add(InternalDamage = new AfflictionPrefab(element, typeof(Affliction)));
                        break;
                    case "bleeding":
                        List.Add(Bleeding = new AfflictionPrefab(element, typeof(AfflictionBleeding)));
                        break;
                    case "burn":
                        List.Add(Burn = new AfflictionPrefab(element, typeof(Affliction)));
                        break;
                    case "oxygenlow":
                        List.Add(OxygenLow = new AfflictionPrefab(element, typeof(Affliction)));
                        break;
                    case "bloodloss":
                        List.Add(Bloodloss = new AfflictionPrefab(element, typeof(Affliction)));
                        break;
                    default:
                        List.Add(new AfflictionPrefab(element));
                        break;
                }
            }
        }

        public AfflictionPrefab(XElement element, Type type = null)
        {
            typeName = type == null ? element.Name.ToString() : type.Name;

            AfflictionType = element.GetAttributeString("type", "");
            Name = element.GetAttributeString("name", "");
            Description = element.GetAttributeString("description", "");

            LimbSpecific = element.GetAttributeBool("limbspecific", false);
            if (!LimbSpecific)
            {
                string indicatorLimbName = element.GetAttributeString("indicatorlimb", "Torso");
                if (!Enum.TryParse(indicatorLimbName, out IndicatorLimb))
                {
                    DebugConsole.ThrowError("Error in affliction prefab " + Name + " - limb type \"" + indicatorLimbName + "\" not found.");
                }
            }

            ActivationThreshold = element.GetAttributeFloat("activationthreshold", 0.0f);
            MaxStrength = element.GetAttributeFloat("maxstrength", 100.0f);

            DamageOverlayAlpha = element.GetAttributeFloat("damageoverlayalpha", 0.0f);
            BurnOverlayAlpha = element.GetAttributeFloat("burnoverlayalpha", 0.0f);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "icon":
                        Icon = new Sprite(subElement);
                        break;
                    case "effect":
                        effects.Add(new Effect(subElement));
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
                        DebugConsole.ThrowError("Could not find an affliction class of the type \"" + typeName + "\".");
                        return;
                    }
                }
            }
            catch
            {
                DebugConsole.ThrowError("Could not find an affliction class of the type \"" + typeName + "\".");
                return;
            }

            constructor = type.GetConstructor(new[] { typeof(AfflictionPrefab), typeof(float) });
        }

        public Affliction Instantiate(float strength)
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

            return instance as Affliction;
        }

        public Effect GetActiveEffect(float currentStrength)
        {
            foreach (Effect effect in effects)
            {
                if (currentStrength > effect.MinStrength && currentStrength <= effect.MaxStrength) return effect;
            }
            return null;
        }
    }
}
