using Microsoft.Xna.Framework;

namespace Subsurface
{
    interface IDamageable
    {
        //float Damage
        //{
        //    get;
        //    set;
        //}

        Vector2 SimPosition
        {
            get;
        }

        void AddDamage(Vector2 position, float amount, float bleedingAmount, float stun);
    }
}
