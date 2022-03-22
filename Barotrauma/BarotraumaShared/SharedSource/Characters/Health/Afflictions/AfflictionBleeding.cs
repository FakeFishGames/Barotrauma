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
            characterHealth.BloodlossAmount += Strength * (1.0f / 60.0f) * deltaTime;
            if (Source != null)
            {
                characterHealth.BloodlossAffliction.Source = Source;
            }
        }
    }
}
