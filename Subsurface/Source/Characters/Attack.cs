using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;


namespace Subsurface
{

    public enum DamageType { None, Blunt, Slash }

    public enum AttackType
    {
        None, PinchCW, PinchCCW
    }

    struct AttackResult
    {
        public readonly float Damage;
        public readonly float Bleeding;

        public readonly bool HitArmor;

        public AttackResult(float damage, float bleeding, bool hitArmor=false)
        {
            this.Damage = damage;
            this.Bleeding = bleeding;

            this.HitArmor = hitArmor;
        }
    }

    class Attack
    {

        public readonly AttackType Type;
        public readonly float Range;
        public readonly float Duration;

        public readonly DamageType DamageType;

        public readonly float StructureDamage;
        public readonly float Damage;
        public readonly float BleedingDamage;

        public readonly float Stun;

        private float priority;

        public Attack(XElement element)
        {
            try
            {
                Type = (AttackType)Enum.Parse(typeof(AttackType), element.Attribute("type").Value, true);
            }
            catch
            {
                Type = AttackType.None;
            }

            try
            {
                DamageType = (DamageType)Enum.Parse(typeof(DamageType), ToolBox.GetAttributeString(element, "damagetype", "None"), true);
            }
            catch
            {
                DamageType = DamageType.None;
            }


            Damage = ToolBox.GetAttributeFloat(element, "damage", 0.0f);
            StructureDamage = ToolBox.GetAttributeFloat(element, "structuredamage", 0.0f);
            BleedingDamage = ToolBox.GetAttributeFloat(element, "bleedingdamage", 0.0f);

            Stun = ToolBox.GetAttributeFloat(element, "stun", 0.0f);


            Range = FarseerPhysics.ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "range", 0.0f));

            Duration = ToolBox.GetAttributeFloat(element, "duration", 0.0f); 

            priority = ToolBox.GetAttributeFloat(element, "priority", 1.0f);
        }

        public AttackResult DoDamage(IDamageable target, Vector2 position, float deltaTime, bool playSound = true)
        {
            float damageAmount = 0.0f;
            //DamageSoundType damageSoundType = DamageSoundType.None;

            if (target as Character == null)
            {
                damageAmount = StructureDamage;
                //damageSoundType = (damageType == DamageType.Blunt) ? DamageSoundType.StructureBlunt: DamageSoundType.StructureSlash;

            }
            else
            {
                damageAmount = Damage;
                //damageSoundType = (damageType == DamageType.Blunt) ? DamageSoundType.LimbBlunt : DamageSoundType.LimbSlash;
            }
            //damageSoundType = (damageType == DamageType.Blunt) ? DamageSoundType.StructureBlunt : DamageSoundType.StructureSlash;
            //if (playSound) AmbientSoundManager.PlayDamageSound(damageSoundType, damageAmount, position);

            if (Duration > 0.0f) damageAmount *= deltaTime;
            float bleedingAmount = (Duration == 0.0f) ? BleedingDamage : BleedingDamage * deltaTime;

            if (damageAmount > 0.0f)
            {
                return target.AddDamage(position, DamageType, damageAmount, bleedingAmount, Stun, playSound);
            }
            else
            {
                return new AttackResult(0.0f, 0.0f);
            }
        }
    }
}
