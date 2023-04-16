using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    partial class DamageModifier : ISerializableEntity
    {
        public string Name => "Damage Modifier";

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

        [Serialize(1.0f, IsPropertySaveable.No), Editable(DecimalCount = 2)]
        public float DamageMultiplier
        {
            get;
            private set;
        }

        [Serialize(1.0f, IsPropertySaveable.No), Editable(DecimalCount = 2, MinValueFloat = 0, MaxValueFloat = 1)]
        public float ProbabilityMultiplier
        {
            get;
            private set;
        }

        [Serialize("0.0,360", IsPropertySaveable.No), Editable]
        public Vector2 ArmorSector
        {
            get;
            private set;
        }

        public Vector2 ArmorSectorInRadians => new Vector2(MathHelper.ToRadians(ArmorSector.X), MathHelper.ToRadians(ArmorSector.Y));

        [Serialize(false, IsPropertySaveable.No), Editable]
        public bool DeflectProjectiles
        {
            get;
            private set;
        }

        [Serialize("", IsPropertySaveable.Yes), Editable]
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

        [Serialize("", IsPropertySaveable.Yes), Editable]
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
        private ImmutableArray<Identifier> parsedAfflictionIdentifiers;
        private ImmutableArray<Identifier> parsedAfflictionTypes;
        public ref readonly ImmutableArray<Identifier> ParsedAfflictionIdentifiers => ref parsedAfflictionIdentifiers;

        public ref readonly ImmutableArray<Identifier> ParsedAfflictionTypes => ref parsedAfflictionTypes;

        public DamageModifier(XElement element, string parentDebugName, bool checkErrors = true)
        {
            Deserialize(element);
            if (element.Attribute("afflictionnames") != null)
            {
                DebugConsole.ThrowError("Error in DamageModifier config (" + parentDebugName + ") - define afflictions using identifiers or types instead of names.");
            }
            if (checkErrors)
            {
                foreach (var afflictionType in parsedAfflictionTypes)
                {
                    if (!AfflictionPrefab.Prefabs.Any(p => p.AfflictionType == afflictionType))
                    {
                        createWarningOrError($"Potentially invalid damage modifier in \"{parentDebugName}\". Could not find any afflictions of the type \"{afflictionType}\". Did you mean to use an affliction identifier instead?");
                    }
                }
                foreach (var afflictionIdentifier in parsedAfflictionIdentifiers)
                {
                    if (!AfflictionPrefab.Prefabs.ContainsKey(afflictionIdentifier))
                    {
                        createWarningOrError($"Potentially invalid damage modifier in \"{parentDebugName}\". Could not find any afflictions with the identifier \"{afflictionIdentifier}\". Did you mean to use an affliction type instead?");
                    }
                }
                if (!parsedAfflictionTypes.Any() && !parsedAfflictionIdentifiers.Any())
                {
                    createWarningOrError($"Potentially invalid damage modifier in \"{parentDebugName}\". Neither affliction types of identifiers defined.");
                }
            }

            static void createWarningOrError(string msg)
            {
#if DEBUG
                DebugConsole.ThrowError(msg);
#else
                DebugConsole.AddWarning(msg);
#endif
            }
        }

        private void ParseAfflictionTypes()
        {
            if (string.IsNullOrWhiteSpace(rawAfflictionTypeString)) 
            {
                parsedAfflictionTypes = Enumerable.Empty<Identifier>().ToImmutableArray();
                return;
            }

            parsedAfflictionTypes = rawAfflictionTypeString.Split(',', '，')
                .Select(s => s.Trim()).ToIdentifiers().ToImmutableArray();
        }

        private void ParseAfflictionIdentifiers()
        {
            if (string.IsNullOrWhiteSpace(rawAfflictionIdentifierString))
            {
                parsedAfflictionIdentifiers = Enumerable.Empty<Identifier>().ToImmutableArray();
                return;
            }
            
            parsedAfflictionIdentifiers = rawAfflictionIdentifierString.Split(',', '，')
                .Select(s => s.Trim()).ToIdentifiers().ToImmutableArray();
        }

        public bool MatchesAfflictionIdentifier(string identifier) =>
            MatchesAfflictionIdentifier(identifier.ToIdentifier());
        
        public bool MatchesAfflictionIdentifier(Identifier identifier)
        {
            //if no identifiers have been defined, the damage modifier affects all afflictions
            if (AfflictionIdentifiers.Length == 0) { return true; }
            return parsedAfflictionIdentifiers.Any(id => id == identifier);
        }

        public bool MatchesAfflictionType(string type) =>
            MatchesAfflictionType(type.ToIdentifier());
        
        public bool MatchesAfflictionType(Identifier type)
        {
            //if no types have been defined, the damage modifier affects all afflictions
            if (AfflictionTypes.Length == 0) { return true; }
            return parsedAfflictionTypes.Any(t => t == type);
        }

        /// <summary>
        /// Returns true if the type or the identifier matches the defined types/identifiers.
        /// </summary>
        public bool MatchesAffliction(string identifier, string type) =>
            MatchesAffliction(identifier.ToIdentifier(), type.ToIdentifier());
        
        public bool MatchesAffliction(Identifier identifier, Identifier type)
        {
            //if no identifiers or types have been defined, the damage modifier affects all afflictions
            if (AfflictionIdentifiers.Length == 0 && AfflictionTypes.Length == 0) { return true; }
            return parsedAfflictionIdentifiers.Any(id => id == identifier)
                || parsedAfflictionTypes.Any(t => t == type);
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
