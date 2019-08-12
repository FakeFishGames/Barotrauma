using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class DamageModifier : ISerializableEntity
    {
        public string Name => "Damage Modifier";

        public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }

        [Serialize(1.0f, false), Editable]
        public float DamageMultiplier
        {
            get;
            private set;
        }

        [Serialize("0.0,360", false), Editable]
        public Vector2 ArmorSector
        {
            get;
            private set;
        }

        public Vector2 ArmorSectorInRadians => new Vector2(MathHelper.ToRadians(ArmorSector.X), MathHelper.ToRadians(ArmorSector.Y));

        [Serialize(false, false), Editable]
        public bool DeflectProjectiles
        {
            get;
            private set;
        }

        [Serialize("", true), Editable]
        public string AfflictionIdentifiers
        {
            get
            {
                return rawAfflictionIdentifierString;
            }
            private set
            {
                rawAfflictionIdentifierString = value;
                ParseAfflictionIdentifiers();
            }
        }

        [Serialize("", true), Editable]
        public string AfflictionTypes
        {
            get
            {
                return rawAfflictionTypeString;
            }
            private set
            {
                rawAfflictionTypeString = value;
                ParseAfflictionTypes();
            }
        }

        private string rawAfflictionIdentifierString;
        private string rawAfflictionTypeString;
        private string[] parsedAfflictionIdentifiers;
        private string[] parsedAfflictionTypes;

        public DamageModifier(XElement element, string parentDebugName)
        {
            Deserialize(element);
            if (element.Attribute("afflictionnames") != null)
            {
                DebugConsole.ThrowError("Error in DamageModifier config (" + parentDebugName + ") - define afflictions using identifiers or types instead of names.");
            }
        }

        private void ParseAfflictionTypes()
        {
            string[] splitValue = rawAfflictionTypeString.Split(',', '，');
            for (int i = 0; i < splitValue.Length; i++)
            {
                splitValue[i] = splitValue[i].ToLowerInvariant().Trim();
            }
            parsedAfflictionTypes = splitValue;
        }

        private void ParseAfflictionIdentifiers()
        {
            string[] splitValue = rawAfflictionIdentifierString.Split(',', '，');
            for (int i = 0; i < splitValue.Length; i++)
            {
                splitValue[i] = splitValue[i].ToLowerInvariant().Trim();
            }
            parsedAfflictionIdentifiers = splitValue;
        }

        public bool MatchesAffliction(Affliction affliction)
        {
            //if no identifiers or types have been defined, the damage modifier affects all afflictions
            if (AfflictionIdentifiers.Length == 0 && AfflictionTypes.Length == 0) { return true; }
            return parsedAfflictionIdentifiers.Any(id => id.Equals(affliction.Identifier, StringComparison.OrdinalIgnoreCase)) 
                || parsedAfflictionTypes.Any(t => t.Equals(affliction.Prefab.AfflictionType, StringComparison.OrdinalIgnoreCase));
        }

        public void Serialize(XElement element)
        {
            if (element == null) { return; }
            SerializableProperty.SerializeProperties(this, element);
        }

        public void Deserialize(XElement element)
        {
            if (element == null) { return; }
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }
    }
}
