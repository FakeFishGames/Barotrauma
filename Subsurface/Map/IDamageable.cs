using Microsoft.Xna.Framework;

namespace Subsurface
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

        void AddDamage(Vector2 position, DamageType damageType, float amount, float bleedingAmount, float stun, bool playSound=true);
    }
}
