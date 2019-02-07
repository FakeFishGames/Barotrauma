using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class DamageModifier
    {
        [Serialize(1.0f, false)]
        public float DamageMultiplier
        {
            get;
            private set;
        }
        
        [Serialize("0.0,360", false)]
        public Vector2 ArmorSector
        {
            get;
            private set;
        }

        [Serialize(true, false)]
        public bool IsArmor
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool DeflectProjectiles
        {
            get;
            private set;
        }

        public string[] AfflictionIdentifiers
        {
            get;
            private set;
        }

        public string[] AfflictionTypes
        {
            get;
            private set;
        }

        public DamageModifier(XElement element, string parentDebugName)
        {
            SerializableProperty.DeserializeProperties(this, element);
            ArmorSector = new Vector2(MathHelper.ToRadians(ArmorSector.X), MathHelper.ToRadians(ArmorSector.Y));

            if (element.Attribute("afflictionnames") != null)
            {
                DebugConsole.ThrowError("Error in DamageModifier config (" + parentDebugName + ") - define afflictions using identifiers or types instead of names.");
            }

            AfflictionIdentifiers = element.GetAttributeStringArray("afflictionidentifiers", new string[0]);
            for (int i = 0; i < AfflictionIdentifiers.Length; i++)
            {
                AfflictionIdentifiers[i] = AfflictionIdentifiers[i].ToLowerInvariant();
            }
            AfflictionTypes = element.GetAttributeStringArray("afflictiontypes", new string[0]);
            for (int i = 0; i < AfflictionTypes.Length; i++)
            {
                AfflictionTypes[i] = AfflictionTypes[i].ToLowerInvariant();
            }
        }

        public bool MatchesAffliction(Affliction affliction)
        {
            if (AfflictionIdentifiers.Length == 0) { return true; }

            foreach (string afflictionName in AfflictionIdentifiers)
            {
                if (affliction.Prefab.Identifier.ToLowerInvariant() == afflictionName) return true;
            }
            foreach (string afflictionType in AfflictionTypes)
            {
                if (affliction.Prefab.AfflictionType.ToLowerInvariant() == afflictionType) return true;
            }
            return false;
        }
    }
}
