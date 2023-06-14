namespace Barotrauma
{
    /// <summary>
    /// A special affliction type that increases the character's Bloodloss affliction with a rate relative to the strength of the bleeding.
    /// </summary>
    class AfflictionBleeding : Affliction
    {
        public AfflictionBleeding(AfflictionPrefab prefab, float strength) : 
            base(prefab, strength)
        {
        }

        public override void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            base.Update(characterHealth, targetLimb, deltaTime);
            float bloodlossResistance = GetResistance(characterHealth.BloodlossAffliction.Identifier);
            characterHealth.BloodlossAmount += Strength * (1.0f - bloodlossResistance) / 60.0f * deltaTime;
            if (Source != null)
            {
                characterHealth.BloodlossAffliction.Source = Source;
            }
        }
    }
}
