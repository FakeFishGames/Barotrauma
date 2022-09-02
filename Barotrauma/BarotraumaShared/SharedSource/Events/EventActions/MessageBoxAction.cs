using System;
using System.Linq;

namespace Barotrauma
{
    class MessageBoxAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Header { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Text { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string IconStyle { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool HideCloseButton { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string CloseOnInput { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CloseOnInteractTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CloseOnPickUpTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CloseOnEquipTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CloseOnExitRoomName { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool IsTutorialObjective { get; set; }

        private bool isFinished = false;

        public MessageBoxAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
#if CLIENT
            CreateMessageBox();
            if (IsTutorialObjective && GameMain.GameSession?.GameMode is TutorialMode tutorialMode)
            {
                tutorialMode.Tutorial?.TriggerTutorialSegment(new Tutorials.Tutorial.Segment(Text, CreateMessageBox));
            }
#endif
            isFinished = true;
        }

#if CLIENT
        public void CreateMessageBox()
        {
            new GUIMessageBox(
                headerText: TextManager.Get(Header),
                text: RichString.Rich(TextManager.ParseInputTypes(TextManager.Get(Text).Fallback(Text.ToString()), useColorHighlight: true)),
                buttons: Array.Empty<LocalizedString>(),
                type: GUIMessageBox.Type.Tutorial,
                iconStyle: IconStyle,
                autoCloseCondition: GetAutoCloseCondition(),
                hideCloseButton: HideCloseButton);
        }
#endif

        private Func<bool> GetAutoCloseCondition()
        {
            var character = ParentEvent.GetTargets(TargetTag).FirstOrDefault() as Character;
            Func<bool> autoCloseCondition = null;
            if (!string.IsNullOrEmpty(CloseOnInput) && Enum.TryParse(CloseOnInput, true, out InputType closeOnInput))
            {
#if CLIENT
                autoCloseCondition = () => PlayerInput.KeyDown(closeOnInput);
#endif
            }
            else if (!CloseOnInteractTag.IsEmpty)
            {
                autoCloseCondition = () => character?.SelectedItem != null && character.SelectedItem.HasTag(CloseOnInteractTag);
            }
            else if (!CloseOnPickUpTag.IsEmpty)
            {
                autoCloseCondition = () => character?.Inventory != null && character.Inventory.FindItemByTag(CloseOnPickUpTag, recursive: true) != null;
            }
            else if (!CloseOnEquipTag.IsEmpty)
            {
                autoCloseCondition = () => character != null && character.HasEquippedItem(CloseOnEquipTag);
            }
            else if (!CloseOnExitRoomName.IsEmpty)
            {
                autoCloseCondition = () => character?.CurrentHull != null && character.CurrentHull.RoomName.ToIdentifier() != CloseOnExitRoomName;
            }
            return autoCloseCondition;
        }

        public override bool IsFinished(ref string goToLabel)
        {
            return isFinished;
        }

        public override void Reset()
        {
            isFinished = false;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(MessageBoxAction)}";
        }
    }
}