using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    class Reputation
    {
        public const float HostileThreshold = 0.2f;
        public const float ReputationLossPerNPCDamage = 0.1f;
        public const float ReputationLossPerStolenItemPrice = 0.01f;
        public const float ReputationLossPerWallDamage = 0.1f;
        public const float MinReputationLossPerStolenItem = 0.5f;
        public const float MaxReputationLossPerStolenItem = 10.0f;

        public string Identifier { get; }
        public int MinReputation { get; }
        public int MaxReputation { get; }
        public int InitialReputation { get; }
        public CampaignMetadata Metadata { get; }

        private readonly string metaDataIdentifier;

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
                OnReputationValueChanged?.Invoke();
                OnAnyReputationValueChanged?.Invoke();
#if CLIENT
                int increase = (int)Value - (int)prevValue;
                if (increase != 0 && Character.Controlled != null)
                {
                    Character.Controlled.AddMessage(
                        TextManager.GetWithVariable("reputationgainnotification", "[reputationname]", Location?.Name ?? Faction.Prefab.Name),
                        increase > 0 ? GUI.Style.Green : GUI.Style.Red,
                        playSound: true, Identifier, increase, lifetime: 5.0f);                    
                }
#endif
            }
        }

        public void SetReputation(float newReputation)
        {
            Value = newReputation;
        }

        public void AddReputation(float reputationChange)
        {
            if (reputationChange > 0f)
            {
                float reputationGainMultiplier = 1f;
                foreach (Character character in GameSession.GetSessionCrewCharacters())
                {
                    reputationGainMultiplier += character.GetStatValue(StatTypes.ReputationGainMultiplier);
                }
                reputationChange *= reputationGainMultiplier;
            }
            Value += reputationChange;
        }

        public Action OnReputationValueChanged;
        public static Action OnAnyReputationValueChanged;

        public readonly Faction Faction;
        public readonly Location Location;


        public Reputation(CampaignMetadata metadata, Location location, string identifier, int minReputation, int maxReputation, int initialReputation)
            : this(metadata, null, location, identifier, minReputation, maxReputation, initialReputation)
        {
        }

        public Reputation(CampaignMetadata metadata, Faction faction, int minReputation, int maxReputation, int initialReputation)
            : this(metadata, faction, null, $"faction.{faction.Prefab.Identifier}", minReputation, maxReputation, initialReputation)
        {
        }

        private Reputation(CampaignMetadata metadata, Faction faction, Location location, string identifier, int minReputation, int maxReputation, int initialReputation)
        {
            System.Diagnostics.Debug.Assert(metadata != null);
            System.Diagnostics.Debug.Assert(faction != null || location != null);
            Metadata = metadata;
            Identifier = identifier.ToLowerInvariant();
            metaDataIdentifier = $"reputation.{Identifier}";
            MinReputation = minReputation;
            MaxReputation = maxReputation;
            InitialReputation = initialReputation;
            Faction = faction;
            Location = location;
        }

        public string GetReputationName()
        {
            return GetReputationName(NormalizedValue);
        }

        public static string GetReputationName(float normalizedValue)
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
                return GUI.Style.ColorReputationVeryLow;
            }
            else if (normalizedValue < 0.4f)
            {
                return GUI.Style.ColorReputationLow;
            }
            else if (normalizedValue < 0.6f)
            {
                return GUI.Style.ColorReputationNeutral;
            }
            else if (normalizedValue < 0.8f)
            {
                return GUI.Style.ColorReputationHigh;
            }
            return GUI.Style.ColorReputationVeryHigh;
        }
        public string GetFormattedReputationText(bool addColorTags = false)
        {
            return GetFormattedReputationText(NormalizedValue, Value, addColorTags);
        }

        public static string GetFormattedReputationText(float normalizedValue, float value, bool addColorTags = false)
        {
            string reputationName = GetReputationName(normalizedValue);
            string formattedReputation = TextManager.GetWithVariables("reputationformat",
                new string[] { "[reputationname]", "[reputationvalue]" },
                new string[] { reputationName, ((int)Math.Round(value)).ToString() });
            if (addColorTags)
            {
                formattedReputation = $"‖color:{XMLExtensions.ColorToString(GetReputationColor(normalizedValue))}‖{formattedReputation}‖end‖";
            }
            return formattedReputation;
        }
#endif
    }
}