using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Particles;
#endif

namespace Barotrauma.Items.Components
{
    class Propulsion : ItemComponent
    {
        public enum UseEnvironment
        {
            Air, Water, Both, None
        };

        private float useState;

        [Serialize(UseEnvironment.Both, IsPropertySaveable.No, description: "Can the item be used in air, underwater or both.")]
        public UseEnvironment UsableIn { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: "The force to apply to the user's body, in the direction the item is pointing towards."), Editable(MinValueFloat = -1000.0f, MaxValueFloat = 1000.0f)]
        public float Force { get; set; }

        [Serialize("0,0", IsPropertySaveable.No, description: "The force to apply to the user's body, in a fixed direction (e.g. \"0,10\" would always apply an upwards force)."), Editable]
        public Vector2 FixedForce { get; set; }

        [Serialize(64.0f, IsPropertySaveable.No, description: "Maximum velocity that can be applied to the user (if the user is already going this fast, no more force is applied)"), Editable(MinValueFloat = 0.0f, MaxValueFloat = 64.0f)]
        public float MaxVelocity { get; set; }

        [Serialize(true, IsPropertySaveable.No, description: "If the item is held in RightHand or LeftHand, apply extra force there")]
        public bool ApplyToHands { get; set; }
#if CLIENT
        private string particles;
        [Serialize("", IsPropertySaveable.No, description: "The name of the particle prefab the item emits when used.")]
        public string Particles
        {
            get { return particles; }
            set { particles = value; }
        }
#endif

        public Propulsion(Item item, ContentXElement element)
            : base(item,element)
        {
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null || character.Removed) { return false; }
            if (!character.IsKeyDown(InputType.Aim) || character.Stun > 0.0f) { return false; }
            if (UsableIn == UseEnvironment.None) { return false; }

            IsActive = true;
            useState = 0.1f;

            if (character.AnimController.InWater)
            {
                if (UsableIn == UseEnvironment.Air) { return true; }
            }
            else
            {
                if (UsableIn == UseEnvironment.Water) { return true; }
            }

            Vector2 dir = character.CursorPosition - character.Position;
            if (!MathUtils.IsValid(dir)) { return true; }
            float length = 200;
            dir = dir.ClampLength(length) / length;
            Vector2 propulsion = dir * Force * character.PropulsionSpeedMultiplier * (1.0f + character.GetStatValue(StatTypes.PropulsionSpeed));
            propulsion += FixedForce;

            if (character.AnimController.InWater && Force > 0.0f) { character.AnimController.TargetMovement = dir; }

            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb.WearingItems.Find(w => w.WearableComponent.Item == item) == null) { continue; }
                limb.body.ApplyForce(propulsion, maxVelocity: MaxVelocity);
            }

            character.AnimController.Collider.ApplyForce(propulsion, maxVelocity: MaxVelocity);

            if (ApplyToHands) 
            { 
                if (character.Inventory.IsInLimbSlot(item, InvSlotType.RightHand))
                {
                    character.AnimController.GetLimb(LimbType.RightHand)?.body.ApplyForce(propulsion, maxVelocity: MaxVelocity);
                }
                if (character.Inventory.IsInLimbSlot(item, InvSlotType.LeftHand))
                {
                    character.AnimController.GetLimb(LimbType.LeftHand)?.body.ApplyForce(propulsion, maxVelocity: MaxVelocity);
                }
            }

#if CLIENT
            if (!string.IsNullOrWhiteSpace(particles))
            {
                GameMain.ParticleManager.CreateParticle(particles, item.WorldPosition,
                    item.body.Rotation + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi), 0.0f, item.CurrentHull);
            }
#endif
            return true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            useState -= deltaTime;

            if (useState <= 0.0f)
            {
                IsActive = false;
            }

            if (item.AiTarget != null && IsActive)
            {
                item.AiTarget.SoundRange = item.AiTarget.MaxSoundRange;
            }
        }
    }
}
