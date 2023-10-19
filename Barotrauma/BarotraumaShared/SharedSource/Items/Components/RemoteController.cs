﻿using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class RemoteController : ItemComponent
    {
        [Serialize("", IsPropertySaveable.No, description: "Tag or identifier of the item that should be controlled.")]
        public Identifier Target
        {
            get;
            private set;
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool OnlyInOwnSub
        {
            get;
            private set;
        }

        [Serialize(10000.0f, IsPropertySaveable.No)]
        public float Range
        {
            get;
            private set;
        }

        public Item TargetItem { get => currentTarget; }

        private Item currentTarget;
        private Character currentUser;
        private Submarine currentSub;

        public RemoteController(Item item, ContentXElement element)
            : base(item, element)
        {
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
                if (targetItem.NonInteractable || targetItem.NonPlayerTeamInteractable || targetItem.HiddenInGame) { continue; }
                if (OnlyInOwnSub)
                {
                    if (targetItem.Submarine != item.Submarine) { continue; }
                    if (targetItem.Submarine.TeamID != user.TeamID) { continue; }
                }
                if (!targetItem.HasTag(Target) && ((MapEntity)targetItem).Prefab.Identifier != Target) { continue; }

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
