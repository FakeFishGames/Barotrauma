namespace Barotrauma
{
    partial class CharacterHealth
    {
        partial void UpdateLimbAfflictionOverlays()
        {
            foreach (Limb limb in character.AnimController.Limbs)
            {
                limb.BurnOverlayStrength = 0.0f;
                limb.DamageOverlayStrength = 0.0f;
                if (limbHealths[limb.HealthIndex].Afflictions.Count == 0) continue;
                foreach (Affliction a in limbHealths[limb.HealthIndex].Afflictions)
                {
                    limb.BurnOverlayStrength += a.Strength / a.Prefab.MaxStrength * a.Prefab.BurnOverlayAlpha;
                    limb.DamageOverlayStrength += a.Strength / a.Prefab.MaxStrength * a.Prefab.DamageOverlayAlpha;
                }
                limb.BurnOverlayStrength /= limbHealths[limb.HealthIndex].Afflictions.Count;
                limb.DamageOverlayStrength /= limbHealths[limb.HealthIndex].Afflictions.Count;
            }
        }
    }
}
