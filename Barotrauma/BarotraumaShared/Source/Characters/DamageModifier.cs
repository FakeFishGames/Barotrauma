using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    class DamageModifier
    {
        [Serialize(DamageType.None, false)]
        public DamageType DamageType
        {
            get;
            private set;
        }

        [Serialize(1.0f, false)]
        public float DamageMultiplier
        {
            get;
            private set;
        }

        [Serialize(1.0f, false)]
        public float BleedingMultiplier
        {
            get;
            private set;
        }

        [Serialize("0.0,360", false)]
        public Vector2 ArmorSector
        {
            get;
            private set;
        }

        [Serialize(true, false)]
        public bool IsArmor
        {
            get;
            private set;
        }

        [Serialize(true, false)]
        public bool DeflectProjectiles
        {
            get;
            private set;
        }


#if CLIENT
        [Serialize(DamageSoundType.None, false)]
        public DamageSoundType DamageSoundType
        {
            get;
            private set;
        }
#endif

        public DamageModifier(XElement element)
        {
            SerializableProperty.DeserializeProperties(this, element);
            ArmorSector = new Vector2(MathHelper.ToRadians(ArmorSector.X), MathHelper.ToRadians(ArmorSector.Y));
        }
    }
}
