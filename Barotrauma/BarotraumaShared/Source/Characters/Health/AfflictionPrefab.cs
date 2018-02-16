using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class AfflictionPrefab
    {
        public static AfflictionPrefab InternalDamage;
        public static AfflictionPrefab Bleeding;
        public static AfflictionPrefab Burn;

        public static List<AfflictionPrefab> List = new List<AfflictionPrefab>();

        //Arbitrary string that is used to identify the type of the affliction.
        //Afflictions with the same type stack up, and items may be configured to cure specific types of afflictions.
        public readonly string AfflictionType;

        //Does the affliction affect a specific limb or the whole character
        public readonly bool LimbSpecific;

        public readonly string Name, Description;

        //how high the strength has to be for the affliction to take affect
        public readonly float ActivationThreshold = 0.0f;

        //how much the strength of the affliction changes per second
        public readonly float StrengthChange = 0.0f;

        public readonly float MaxVitalityDecrease = 100.0f;
        public readonly float MaxStrength = 100.0f;

        public readonly Sprite Icon;

        public float BurnOverlayAlpha;
        public float DamageOverlayAlpha;

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

            ActivationThreshold = element.GetAttributeFloat("activationthreshold", 0.0f);

            MaxVitalityDecrease = element.GetAttributeFloat("maxvitalitydecrease", 100.0f);
            MaxStrength = element.GetAttributeFloat("maxstrength", 100.0f);
            StrengthChange = element.GetAttributeFloat("strengthchange", 0.0f);

            DamageOverlayAlpha = element.GetAttributeFloat("damageoverlayalpha", 0.0f);
            BurnOverlayAlpha = element.GetAttributeFloat("burnoverlayalpha", 0.0f);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() == "icon")
                {
                    Icon = new Sprite(subElement);
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
    }
}
