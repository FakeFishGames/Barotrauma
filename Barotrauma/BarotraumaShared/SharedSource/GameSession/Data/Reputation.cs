using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class Reputation
    {
        public const float HostileThreshold = 0.2f;
        public const float ReputationLossPerNPCDamage = 0.05f;
        public const float ReputationLossPerWallDamage = 0.05f;
        public const float ReputationLossPerStolenItemPrice = 0.005f;
        public const float MinReputationLossPerStolenItem = 0.05f;
        public const float MaxReputationLossPerStolenItem = 1.0f;

        public Identifier Identifier { get; }
        public int MinReputation { get; }
        public int MaxReputation { get; }
        public int InitialReputation { get; }
        public CampaignMetadata Metadata { get; }

        private readonly Identifier metaDataIdentifier;

        /// <summary>
        /// Reputation value normalized to the range of 0-1
        /// </summary>
        public float NormalizedValue
        {
            get { return MathUtils.InverseLerp(MinReputation, MaxReputation, Value); }
        }

        public float Value
        {
            get => Math.Min(MaxReputation, Metadata.GetFloat(metaDataIdentifier, InitialReputation));
            private set
            {
                if (MathUtils.NearlyEqual(Value, value)) { return; }

                float prevValue = Value;

                Metadata.SetValue(metaDataIdentifier, Math.Clamp(value, MinReputation, MaxReputation));
                OnReputationValueChanged?.Invoke(this);
                OnAnyReputationValueChanged?.Invoke(this);
#if CLIENT
                int increase = (int)Value - (int)prevValue;
                if (increase != 0 && Character.Controlled != null)
                {
                    Character.Controlled.AddMessage(
                        TextManager.GetWithVariable("reputationgainnotification", "[reputationname]", Location?.Name ?? Faction.Prefab.Name).Value,
                        increase > 0 ? GUIStyle.Green : GUIStyle.Red,
                        playSound: true, Identifier, increase, lifetime: 5.0f);                    
                }
#endif
            }
        }

        public void SetReputation(float newReputation)
        {
            Value = newReputation;
        }

        public float GetReputationChangeMultiplier(float reputationChange)
        {
            if (reputationChange > 0f)
            {
                float reputationGainMultiplier = 1f;
                foreach (Character character in GameSession.GetSessionCrewCharacters(CharacterType.Both))
                {
                    reputationGainMultiplier *= 1f + character.GetStatValue(StatTypes.ReputationGainMultiplier, includeSaved: false);
                    reputationGainMultiplier *= 1f + character.Info?.GetSavedStatValue(StatTypes.ReputationGainMultiplier, Identifier) ?? 0;
                }
                return reputationGainMultiplier;
            }
            else if (reputationChange < 0f)
            {
                float reputationLossMultiplier = 1f;
                foreach (Character character in GameSession.GetSessionCrewCharacters(CharacterType.Both))
                {
                    reputationLossMultiplier *= 1f + character.GetStatValue(StatTypes.ReputationLossMultiplier, includeSaved: false);
                    reputationLossMultiplier *= 1f + character.Info?.GetSavedStatValue(StatTypes.ReputationLossMultiplier, Identifier) ?? 0;
                }
                return reputationLossMultiplier;
            }
            return 1.0f;
        }

        public void AddReputation(float reputationChange)
        {
            Value += reputationChange * GetReputationChangeMultiplier(reputationChange);
        }

        public readonly NamedEvent<Reputation> OnReputationValueChanged = new NamedEvent<Reputation>();
        public static readonly NamedEvent<Reputation> OnAnyReputationValueChanged = new NamedEvent<Reputation>();

        public readonly Faction Faction;
        public readonly Location Location;


        public Reputation(CampaignMetadata metadata, Location location, Identifier identifier, int minReputation, int maxReputation, int initialReputation)
            : this(metadata, null, location, identifier, minReputation, maxReputation, initialReputation)
        {
        }

        public Reputation(CampaignMetadata metadata, Faction faction, int minReputation, int maxReputation, int initialReputation)
            : this(metadata, faction, null, $"faction.{faction.Prefab.Identifier}".ToIdentifier(), minReputation, maxReputation, initialReputation)
        {
        }

        private Reputation(CampaignMetadata metadata, Faction faction, Location location, Identifier identifier, int minReputation, int maxReputation, int initialReputation)
        {
            System.Diagnostics.Debug.Assert(metadata != null);
            System.Diagnostics.Debug.Assert(faction != null || location != null);
            Metadata = metadata;
            Identifier = identifier;
            metaDataIdentifier = $"reputation.{Identifier}".ToIdentifier();
            MinReputation = minReputation;
            MaxReputation = maxReputation;
            InitialReputation = initialReputation;
            Faction = faction;
            Location = location;
        }

        public LocalizedString GetReputationName()
        {
            return GetReputationName(NormalizedValue);
        }

        public static LocalizedString GetReputationName(float normalizedValue)
        {
            if (normalizedValue < HostileThreshold)
            {
                return TextManager.Get("reputationverylow");
            }
            else if (normalizedValue < 0.4f)
            {
                return TextManager.Get("reputationlow");
            }
            else if (normalizedValue < 0.6f)
            {
                return TextManager.Get("reputationneutral");
            }
            else if (normalizedValue < 0.8f)
            {
                return TextManager.Get("reputationhigh");
            }
            return TextManager.Get("reputationveryhigh");
        }

#if CLIENT
        public static Color GetReputationColor(float normalizedValue)
        {
            if (normalizedValue < HostileThreshold)
            {
                return GUIStyle.ColorReputationVeryLow;
            }
            else if (normalizedValue < 0.4f)
            {
                return GUIStyle.ColorReputationLow;
            }
            else if (normalizedValue < 0.6f)
            {
                return GUIStyle.ColorReputationNeutral;
            }
            else if (normalizedValue < 0.8f)
            {
                return GUIStyle.ColorReputationHigh;
            }
            return GUIStyle.ColorReputationVeryHigh;
        }
        public LocalizedString GetFormattedReputationText(bool addColorTags = false)
        {
            return GetFormattedReputationText(NormalizedValue, Value, addColorTags);
        }

        public static LocalizedString GetFormattedReputationText(float normalizedValue, float value, bool addColorTags = false)
        {
            LocalizedString reputationName = GetReputationName(normalizedValue);
            LocalizedString formattedReputation = TextManager.GetWithVariables("reputationformat",
                ("[reputationname]", reputationName),
                ("[reputationvalue]", ((int)Math.Round(value)).ToString()));
            if (addColorTags)
            {
                formattedReputation = $"‖color:{XMLExtensions.ToStringHex(GetReputationColor(normalizedValue))}‖{formattedReputation}‖end‖";
            }
            return formattedReputation;
        }
#endif
    }
}