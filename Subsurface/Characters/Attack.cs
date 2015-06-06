using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface
{

    public enum DamageType { None, Blunt, Slash };

    struct AttackResult
    {
        public readonly float damage;
        public readonly float bleeding;

        public readonly bool hitArmor;

        public AttackResult(float damage, float bleeding, bool hitArmor=false)
        {
            this.damage = damage;
            this.bleeding = bleeding;

            this.hitArmor = hitArmor;
        }
    }

    class Attack
    {
        public enum Type
        {
            None, PinchCW, PinchCCW
        };
        public readonly Type type;
        public readonly float range;
        public readonly float duration;

        public readonly DamageType damageType;

        public readonly float structureDamage;
        public readonly float damage;
        public readonly float bleedingDamage;

        public readonly float stun;

        private float priority;

        public Attack(XElement element)
        {
            try
            {
                type = (Type)Enum.Parse(typeof(Type), element.Attribute("type").Value, true);
            }
            catch
            {
                type = Type.None;
            }

            try
            {
                damageType = (DamageType)Enum.Parse(typeof(DamageType), ToolBox.GetAttributeString(element, "damagetype", "None"), true);
            }
            catch
            {
                damageType = DamageType.None;
            }


            damage = ToolBox.GetAttributeFloat(element, "damage", 0.0f);
            structureDamage = ToolBox.GetAttributeFloat(element, "damage", 0.0f);
            bleedingDamage = ToolBox.GetAttributeFloat(element, "bleedingdamage", 0.0f);

            stun = ToolBox.GetAttributeFloat(element, "stun", 0.0f);


            range = FarseerPhysics.ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "range", 0.0f));

            duration = ToolBox.GetAttributeFloat(element, "duration", 0.0f); 

            priority = ToolBox.GetAttributeFloat(element, "priority", 1.0f);
        }

        public AttackResult DoDamage(IDamageable target, Vector2 position, float deltaTime, bool playSound=true)
        {
            float damageAmount = 0.0f;
            //DamageSoundType damageSoundType = DamageSoundType.None;

            if (target as Character == null)
            {
                damageAmount = structureDamage;
                //damageSoundType = (damageType == DamageType.Blunt) ? DamageSoundType.StructureBlunt: DamageSoundType.StructureSlash;

            }
            else
            {
                damageAmount = damage;
                //damageSoundType = (damageType == DamageType.Blunt) ? DamageSoundType.LimbBlunt : DamageSoundType.LimbSlash;
            }
            //damageSoundType = (damageType == DamageType.Blunt) ? DamageSoundType.StructureBlunt : DamageSoundType.StructureSlash;
            //if (playSound) AmbientSoundManager.PlayDamageSound(damageSoundType, damageAmount, position);

            if (duration > 0.0f) damageAmount *= deltaTime;
            float bleedingAmount = (duration == 0.0f) ? bleedingDamage : bleedingDamage * deltaTime;

            if (damageAmount > 0.0f)
            {
                return target.AddDamage(position, damageType, damageAmount, bleedingAmount, stun, playSound);
            }
            else
            {
                return new AttackResult(0.0f, 0.0f);
            }
        }
    }
}
