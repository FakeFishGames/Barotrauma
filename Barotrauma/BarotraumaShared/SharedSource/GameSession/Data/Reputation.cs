using Microsoft.Xna.Framework;
using System;

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
            set
            {
                if (MathUtils.NearlyEqual(Value, value)) { return; }
                Metadata.SetValue(metaDataIdentifier, Math.Clamp(value, MinReputation, MaxReputation));
                OnReputationValueChanged?.Invoke();
                OnAnyReputationValueChanged?.Invoke();
            }
        }

        public Action OnReputationValueChanged;
        public static Action OnAnyReputationValueChanged;

        public Reputation(CampaignMetadata metadata, string identifier, int minReputation, int maxReputation, int initialReputation)
        {
            System.Diagnostics.Debug.Assert(metadata != null);
            Metadata = metadata;
            Identifier = identifier.ToLowerInvariant();
            metaDataIdentifier = $"reputation.{Identifier}";
            MinReputation = minReputation;
            MaxReputation = maxReputation;
            InitialReputation = initialReputation;
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