using Barotrauma.Extensions;
using System.Collections.Generic;

namespace Barotrauma
{
    class CheckSelectedAction : BinaryOptionAction
    {
        public enum SelectedItemType { Primary, Secondary, Any };

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CharacterTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize(SelectedItemType.Any, IsPropertySaveable.Yes)]
        public SelectedItemType ItemType { get; set; }

        public CheckSelectedAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

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
                Error($"{nameof(CheckSelectedAction)} error: {GetEventName()} uses a {nameof(CheckSelectedAction)} but no valid character was found for tag \"{CharacterTag}\"! This will cause the check to automatically fail.");
                return false;
            }
            if (!TargetTag.IsEmpty)
            {
                IEnumerable<Entity> targets = ParentEvent.GetTargets(TargetTag);
                if (targets.None())
                {
                    Error($"{nameof(CheckSelectedAction)} error: {GetEventName()} uses a {nameof(CheckSelectedAction)} but no valid targets were found for tag \"{TargetTag}\"! This will cause the check to automatically fail.");
                    return false;
                }
                foreach (var target in targets)
                {
                    if (target is Character targetCharacter)
                    {
                        if (ItemType == SelectedItemType.Any && character.SelectedCharacter == targetCharacter) { return true; }
                        continue;
                    }
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
#if DEBUG
            void Error(string errorMsg)
            {
                DebugConsole.ThrowError(errorMsg);
            }
#else

            void Error(string errorMsg)
            {
                DebugConsole.LogError(errorMsg);
            }
#endif
        }

        private string GetEventName()
        {
            return ParentEvent?.Prefab?.Identifier is { IsEmpty: false } identifier ? $"the event \"{identifier}\"" : "an unknown event";
        }
    }
}