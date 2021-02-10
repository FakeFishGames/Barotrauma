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

        [Serialize(1.0f, false), Editable(DecimalCount = 2)]
        public float DamageMultiplier
        {
            get;
            private set;
        }

        [Serialize(1.0f, false), Editable(DecimalCount = 2, MinValueFloat = 0, MaxValueFloat = 1)]
        public float ProbabilityMultiplier
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
            if (string.IsNullOrWhiteSpace(rawAfflictionTypeString)) 
            {
                parsedAfflictionTypes = new string[0];
                return;
            }
            string[] splitValue = rawAfflictionTypeString.Split(',', '，');
            for (int i = 0; i < splitValue.Length; i++)
            {
                splitValue[i] = splitValue[i].ToLowerInvariant().Trim();
            }
            parsedAfflictionTypes = splitValue;
        }

        private void ParseAfflictionIdentifiers()
        {
            if (string.IsNullOrWhiteSpace(rawAfflictionIdentifierString))
            {
                parsedAfflictionIdentifiers = new string[0];
                return;
            }
            string[] splitValue = rawAfflictionIdentifierString.Split(',', '，');
            for (int i = 0; i < splitValue.Length; i++)
            {
                splitValue[i] = splitValue[i].ToLowerInvariant().Trim();
            }
            parsedAfflictionIdentifiers = splitValue;
        }

        public bool MatchesAfflictionIdentifier(string identifier)
        {
            //if no identifiers have been defined, the damage modifier affects all afflictions
            if (AfflictionIdentifiers.Length == 0) { return true; }
            return parsedAfflictionIdentifiers.Any(id => id.Equals(identifier, StringComparison.OrdinalIgnoreCase));
        }

        public bool MatchesAfflictionType(string type)
        {
            //if no types have been defined, the damage modifier affects all afflictions
            if (AfflictionTypes.Length == 0) { return true; }
            return parsedAfflictionTypes.Any(t => t.Equals(type, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns true if the type or the identifier matches the defined types/identifiers.
        /// </summary>
        public bool MatchesAffliction(string identifier, string type)
        {
            //if no identifiers or types have been defined, the damage modifier affects all afflictions
            if (AfflictionIdentifiers.Length == 0 && AfflictionTypes.Length == 0) { return true; }
            return parsedAfflictionIdentifiers.Any(id => id.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                || parsedAfflictionTypes.Any(t => t.Equals(type, StringComparison.OrdinalIgnoreCase));
        }

        public bool MatchesAffliction(Affliction affliction) => MatchesAffliction(affliction.Identifier, affliction.Prefab.AfflictionType);

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
