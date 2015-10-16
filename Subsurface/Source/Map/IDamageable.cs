using Microsoft.Xna.Framework;

namespace Barotrauma
{
    interface IDamageable
    {
        Vector2 SimPosition
        {
            get;
        }

        float Health
        {
            get;
        }

        AITarget AiTarget
        {
            get;
        }

        AttackResult AddDamage(IDamageable attacker, Vector2 position, Attack attack, float deltaTime, bool playSound=true);
    }
}
