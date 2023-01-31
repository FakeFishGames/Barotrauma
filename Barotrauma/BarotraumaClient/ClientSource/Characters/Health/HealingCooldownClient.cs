#nullable enable

using System;

namespace Barotrauma
{
    internal static class HealingCooldown
    {
        public static float NormalizedCooldown => MathF.Min((float) (DateTimeOffset.UtcNow - OnCooldownUntil).TotalSeconds / CooldownDuration, 0f);
        public static bool IsOnCooldown => DateTimeOffset.UtcNow < OnCooldownUntil;

        private static DateTimeOffset OnCooldownUntil = DateTimeOffset.MinValue;
        private const float CooldownDuration = 0.5f;

        public static readonly Identifier MedicalItemTag = new Identifier("medical");

        public static void PutOnCooldown()
        {
            OnCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(CooldownDuration);
        }
    }
}