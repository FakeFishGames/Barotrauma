#nullable enable

using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class WaitForItemUsedAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ItemTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier UserTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetItemComponent { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the target item when it's used.")]
        public Identifier ApplyTagToItem { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the user when the target item is used.")]
        public Identifier ApplyTagToUser{ get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the hull the target item is inside when the item is used.")]
        public Identifier ApplyTagToHull { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the hull the target item is inside, and all the hulls it's linked to, when the item is used.")]
        public Identifier ApplyTagToLinkedHulls { get; set; }

        [Serialize(1, IsPropertySaveable.Yes)]
        public int RequiredUseCount { get; set; }

        private bool isFinished;

        private readonly HashSet<Entity> targets = new HashSet<Entity>();
        private readonly HashSet<ItemComponent> targetComponents = new HashSet<ItemComponent>();

        private int useCount = 0;

        private Identifier onUseEventIdentifier;
        private Identifier OnUseEventIdentifier
        {
            get
            {
                if (onUseEventIdentifier.IsEmpty)
                {
                    onUseEventIdentifier = (ParentEvent.Prefab.Identifier + ParentEvent.Actions.IndexOf(this).ToString()).ToIdentifier();
                }
                return onUseEventIdentifier;
            }
        }

        public WaitForItemUsedAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        {
            if (ItemTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{ParentEvent.Prefab.Identifier}\". {nameof(ItemTag)} not set in {nameof(WaitForItemUsedAction)}.");
            }
        }

        private void OnItemUsed(Item item, Character user)
        {
            if (!UserTag.IsEmpty)
            {
                if (!ParentEvent.GetTargets(UserTag).Contains(user)) { return; }
            }

            useCount++;
            if (useCount < RequiredUseCount) { return; }

            if (!ApplyTagToItem.IsEmpty)
            {
                ParentEvent.AddTarget(ApplyTagToItem, item);
            }
            if (!ApplyTagToUser.IsEmpty && user != null)
            {
                ParentEvent.AddTarget(ApplyTagToUser, user);
            }
            ApplyTagsToHulls(item, ApplyTagToHull, ApplyTagToLinkedHulls);
            DeregisterTargets();
            isFinished = true;
        }

        public override void Update(float deltaTime)
        {
            TryRegisterTargets();
        }

        private void TryRegisterTargets()
        {
            foreach (Entity target in ParentEvent.GetTargets(ItemTag))
            {
                //already registered, ignore
                if (targets.Contains(target)) { continue; }
                if (target is not Item item) { continue; }
                if (TargetItemComponent.IsEmpty)
                {
                    item.GetComponents<ItemComponent>().ForEach(ic => Register(ic));
                }
                else if (item.Components.FirstOrDefault(ic => ic.Name == TargetItemComponent) is ItemComponent targetItemComponent)
                {
                    Register(targetItemComponent);
                }
                else
                {
#if DEBUG
                    DebugConsole.ThrowError($"Failed to find the component {TargetItemComponent} on item {item.Prefab.Identifier}");
#endif
                }
            }
            void Register(ItemComponent ic)
            {
                targets.Add(ic.Item);
                targetComponents.Add(ic);
                ic.OnUsed.RegisterOverwriteExisting(
                    OnUseEventIdentifier,
                    i => { OnItemUsed(i.Item, i.User); });
            }
        }

        private void DeregisterTargets()
        {
            foreach (ItemComponent ic in targetComponents)
            {
                ic.OnUsed.Deregister(OnUseEventIdentifier);
            }
            targetComponents.Clear();
            targets.Clear();
        }


        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset()
        {
            isFinished = false;
            useCount = 0;
            DeregisterTargets();
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(WaitForItemUsedAction)} -> ({ItemTag})";
        }
    }
}