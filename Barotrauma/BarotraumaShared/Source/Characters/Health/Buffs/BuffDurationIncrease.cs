namespace Barotrauma
{
    class BuffDurationIncrease : Affliction
    {
        private const float multiplier = 1.25f;
        private bool activated = false;

        public BuffDurationIncrease(AfflictionPrefab prefab, float strength) : base(prefab, strength)
        {

        }

        public override void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            if (activated) return;

            var afflictions = characterHealth.GetAllAfflictions();
            foreach (Affliction affliction in afflictions)
            {
                if (!affliction.Prefab.IsBuff || affliction == this) continue;
                affliction.Strength *= multiplier;
            }

            activated = true;
        }
    }
}
