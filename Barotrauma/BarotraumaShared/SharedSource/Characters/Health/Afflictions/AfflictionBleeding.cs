namespace Barotrauma
{
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
