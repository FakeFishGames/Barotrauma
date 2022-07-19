using System;

namespace Barotrauma
{
    partial class Item
    {
        private readonly struct CombineEventData : IEventData
        {
            public EventType EventType => EventType.Combine;
            public readonly Item CombineTarget;

            public CombineEventData(Item combineTarget)
            {
                CombineTarget = combineTarget;
            }
        }
        
        private readonly struct TreatmentEventData : IEventData
        {
            public EventType EventType => EventType.Treatment;
            public readonly Character TargetCharacter;
            public readonly Limb TargetLimb;
            public byte LimbIndex
                => TargetCharacter?.AnimController?.Limbs is { } limbs
                    ? (byte)Array.IndexOf(limbs, TargetLimb)
                    : byte.MaxValue;

            public TreatmentEventData(Character targetCharacter, Limb targetLimb)
            {
                TargetCharacter = targetCharacter;
                TargetLimb = targetLimb;
            }
        }
    }
}
