using System;

namespace Barotrauma
{
    class Reputation
    {
        public const float HostileThreshold = 0.1f;
        public const float ReputationLossPerNPCDamage = 0.1f;
        public const float ReputationLossPerStolenItemPrice = 0.01f;
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
    }
}