using Barotrauma.Extensions;
using System.Collections.Generic;

namespace Barotrauma
{
    class CheckSelectedItemAction : BinaryOptionAction
    {
        public enum SelectedItemType { Primary, Secondary, Any };

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CharacterTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize(SelectedItemType.Any, IsPropertySaveable.Yes)]
        public SelectedItemType ItemType { get; set; }

        public CheckSelectedItemAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        protected override bool? DetermineSuccess()
        {
            Character character = null;
            if (!CharacterTag.IsEmpty)
            {
                foreach (var t in ParentEvent.GetTargets(CharacterTag))
                {
                    if (t is Character c)
                    {
                        character = c;
                        break;
                    }
                }
            }
            if (character == null)
            {
                DebugConsole.LogError($"CheckSelectedItemAction error: {GetEventName()} uses a CheckSelectedItemAction but no valid character was found for tag \"{CharacterTag}\"! This will cause the check to automatically fail.");
                return false;
            }
            if (!TargetTag.IsEmpty)
            {
                IEnumerable<Entity> targets = ParentEvent.GetTargets(TargetTag);
                if (targets.None())
                {
                    DebugConsole.LogError($"CheckSelectedItemAction error: {GetEventName()} uses a CheckSelectedItemAction but no valid targets were found for tag \"{TargetTag}\"! This will cause the check to automatically fail.");
                    return false;
                }
                foreach (var target in targets)
                {
                    if (target is not Item targetItem)
                    {
                        continue;
                    }
                    if (IsSelected(targetItem))
                    {
                        return true;
                    }
                }
                return false;

                bool IsSelected(Item item)
                {
                    return ItemType switch
                    {
                        SelectedItemType.Any => character.IsAnySelectedItem(item),
                        SelectedItemType.Primary => character.SelectedItem == item,
                        SelectedItemType.Secondary => character.SelectedSecondaryItem == item,
                        _ => false
                    };
                }
            }
            else
            {
                return ItemType switch
                {
                    SelectedItemType.Any => !character.HasSelectedAnyItem,
                    SelectedItemType.Primary => character.SelectedItem == null,
                    SelectedItemType.Secondary => character.SelectedSecondaryItem == null,
                    _ => false
                };
            }
        }

        private string GetEventName()
        {
            return ParentEvent?.Prefab?.Identifier is { IsEmpty: false } identifier ? $"the event \"{identifier}\"" : "an unknown event";
        }
    }
}