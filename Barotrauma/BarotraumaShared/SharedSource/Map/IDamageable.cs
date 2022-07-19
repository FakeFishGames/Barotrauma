using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    interface IDamageable
    {
        Vector2 SimPosition { get; }
        Vector2 WorldPosition { get; }
        float Health { get; }
        
        AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound=true);
        
        
        public readonly struct AttackEventData
        {
            public readonly ISpatialEntity Attacker;
            public readonly IDamageable TargetEntity;
            public readonly Limb TargetLimb;
            public readonly Vector2 AttackSimPosition;

            public AttackEventData(ISpatialEntity attacker, IDamageable targetEntity, Limb targetLimb, Vector2 attackSimPosition)
            {
                Attacker = attacker;
                TargetEntity = targetEntity;
                TargetLimb = targetLimb;
                AttackSimPosition = attackSimPosition;
            }
        }
    }
}
