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

            public readonly float MinVitalityDecrease = 0.0f;
            public readonly float MaxVitalityDecrease = 0.0f;
            
            //how much the strength of the affliction changes per second
            public readonly float StrengthChange = 0.0f;

            public readonly bool MultiplyByMaxVitality;

            public float MinScreenBlurStrength, MaxScreenBlurStrength;
            public float MinScreenDistortStrength, MaxScreenDistortStrength;

            //statuseffects applied on the character when the affliction is active
            public readonly List<StatusEffect> StatusEffects = new List<StatusEffect>();

            public Effect(XElement element)
            {
                MinStrength =  element.GetAttributeFloat("minstrength", 0);
                MaxStrength =  element.GetAttributeFloat("maxstrength", 0);

                MultiplyByMaxVitality = element.GetAttributeBool("multiplybymaxvitality", false);

                MinVitalityDecrease = element.GetAttributeFloat("minvitalitydecrease", 0.0f);
                MaxVitalityDecrease = element.GetAttributeFloat("maxvitalitydecrease", 0.0f);
                MaxVitalityDecrease = Math.Max(MinVitalityDecrease, MaxVitalityDecrease);

                MinScreenDistortStrength = element.GetAttributeFloat("minscreendistort", 0.0f);
                MaxScreenDistortStrength = element.GetAttributeFloat("maxscreendistort", 0.0f);
                MaxScreenDistortStrength = Math.Max(MinScreenDistortStrength, MaxScreenDistortStrength);

                MinScreenBlurStrength = element.GetAttributeFloat("minscreenblur", 0.0f);
                MaxScreenBlurStrength = element.GetAttributeFloat("maxscreenblur", 0.0f);
                MaxScreenBlurStrength = Math.Max(MinScreenBlurStrength, MaxScreenBlurStrength);


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
        public static AfflictionPrefab Stun;
        public static AfflictionPrefab Husk;

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

        public readonly string CauseOfDeathDescription, SelfCauseOfDeathDescription;

        //how high the strength has to be for the affliction to take affect
        public readonly float ActivationThreshold = 0.0f;
        //how high the strength has to be for the affliction icon to be shown in the UI
        public readonly float ShowIconThreshold = 0.0f;
        public readonly float MaxStrength = 100.0f;

        public float BurnOverlayAlpha;
        public float DamageOverlayAlpha;

        public readonly Sprite Icon;

        private List<Effect> effects = new List<Effect>();

        private Dictionary<string, float> treatmentSuitability = new Dictionary<string, float>();

        private readonly string typeName;

        private readonly ConstructorInfo constructor;

        public static void LoadAll(List<string> filePaths)
        {
            foreach (string filePath in filePaths)
            {
                XDocument doc = XMLExtensions.TryLoadXml(filePath);
                if (doc == null || doc.Root == null) continue;

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
                        case "stun":
                            List.Add(Stun = new AfflictionPrefab(element, typeof(Affliction)));
                            break;
                        case "husk":
                            List.Add(Husk = new AfflictionPrefab(element, typeof(Affliction)));
                            break;
                        default:
                            List.Add(new AfflictionPrefab(element));
                            break;
                    }
                }
            }
        }

        public AfflictionPrefab(XElement element, Type type = null)
        {
            typeName = type == null ? element.Name.ToString() : type.Name;

            AfflictionType  = element.GetAttributeString("type", "");
            Name            = element.GetAttributeString("name", "");
            Description     = element.GetAttributeString("description", "");

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
            ShowIconThreshold   = element.GetAttributeFloat("showiconthreshold", ActivationThreshold);
            MaxStrength         = element.GetAttributeFloat("maxstrength", 100.0f);

            DamageOverlayAlpha  = element.GetAttributeFloat("damageoverlayalpha", 0.0f);
            BurnOverlayAlpha    = element.GetAttributeFloat("burnoverlayalpha", 0.0f);

            CauseOfDeathDescription     = element.GetAttributeString("causeofdeathdescription", "");
            SelfCauseOfDeathDescription = element.GetAttributeString("selfcauseofdeathdescription", "");

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
                    case "suitabletreatment":
                        string treatmentName = subElement.GetAttributeString("name", "").ToLowerInvariant();
                        if (treatmentSuitability.ContainsKey(treatmentName))
                        {
                            DebugConsole.ThrowError("Error in affliction \"" + Name + "\" - treatment \"" + treatmentName + "\" defined multiple times");
                            continue;
                        }
                        treatmentSuitability.Add(treatmentName, subElement.GetAttributeFloat("suitability", 0.0f));
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

        public float GetTreatmentSuitability(Item item)
        {
            if (item == null || !treatmentSuitability.ContainsKey(item.Name.ToLowerInvariant())) return 0.0f;
            return treatmentSuitability[item.Name.ToLowerInvariant()];
        }
    }
}
