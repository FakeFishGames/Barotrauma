using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class RemoteController : ItemComponent
    {
        [Serialize("", false, description: "Tag or identifier of the item that should be controlled.")]
        public string Target
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool OnlyInOwnSub
        {
            get;
            private set;
        }

        [Serialize(10000.0f, false)]
        public float Range
        {
            get;
            private set;
        }

        public Item TargetItem { get => currentTarget; }

        private Item currentTarget;
        private Character currentUser;
        private Submarine currentSub;

        public RemoteController(Item item, XElement element)
            : base(item, element)
        {
            DrawHudWhenEquipped = false;
        }

        public override bool Select(Character character)
        {
            if (base.Select(character))
            {
                FindTarget(character);
                return true;
            }
            return false;
        }

        public override void Equip(Character character)
        {
            FindTarget(character);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);
            if (currentTarget.Removed || 
                item.Submarine != currentSub || 
                Vector2.DistanceSquared(currentTarget.WorldPosition, item.WorldPosition) > Range * Range)
            {
                FindTarget(currentUser);
            }
        }

        private void FindTarget(Character user)
        {
            currentTarget = null;
            if (user == null || (item.Submarine == null && OnlyInOwnSub))
            {
                IsActive = false;
                return;
            }

            float closestDist = float.PositiveInfinity;
            foreach (Item targetItem in Item.ItemList)
            {
                if (OnlyInOwnSub)
                {
                    if (targetItem.Submarine != item.Submarine) { continue; }
                    if (targetItem.Submarine.TeamID != user.TeamID) { continue; }
                }
                if (!targetItem.HasTag(Target) && targetItem.prefab.Identifier != Target) { continue; }

                float distSqr = Vector2.DistanceSquared(item.WorldPosition, targetItem.WorldPosition);
                if (distSqr > Range * Range || distSqr > closestDist) { continue; }

                currentTarget = targetItem;
                currentSub = item.Submarine;
                closestDist = distSqr;
                currentUser = user;
            }
            IsActive = currentTarget != null;
        }
    }
}
