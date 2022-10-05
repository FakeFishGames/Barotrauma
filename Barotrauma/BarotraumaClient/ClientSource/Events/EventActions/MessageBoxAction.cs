using Barotrauma.Tutorials;
using System;
using System.Linq;

namespace Barotrauma;

partial class MessageBoxAction : EventAction
{
    partial void UpdateProjSpecific()
    {
        if (Type == ActionType.Create || Type == ActionType.ConnectObjective)
        {
            CreateMessageBox();
            if (!ObjectiveTag.IsEmpty && GameMain.GameSession?.GameMode is TutorialMode tutorialMode)
            {
                Identifier id = Identifier.IfEmpty(Text);
                var segment = Tutorial.Segment.CreateMessageBoxSegment(id, ObjectiveTag, CreateMessageBox);
                tutorialMode.Tutorial?.TriggerTutorialSegment(segment, connectObjective: Type == ActionType.ConnectObjective);
            }
        }
        else if (Type == ActionType.Close)
        {
            GUIMessageBox.Close(Tag);
        }
        else if (Type == ActionType.Clear)
        {
            GUIMessageBox.CloseAll();
        }
    }

    public void CreateMessageBox()
    {
        new GUIMessageBox(
            headerText: TextManager.Get(Header),
            text: RichString.Rich(TextManager.ParseInputTypes(TextManager.Get(Text).Fallback(Text.ToString()), useColorHighlight: true)),
            buttons: Array.Empty<LocalizedString>(),
            type: GUIMessageBox.Type.Tutorial,
            tag: Tag,
            iconStyle: IconStyle,
            autoCloseCondition: GetAutoCloseCondition(),
            hideCloseButton: HideCloseButton)
        {
            FlashOnAutoCloseCondition = true
        };
    }

    private Func<bool> GetAutoCloseCondition()
    {
        var character = ParentEvent.GetTargets(TargetTag).FirstOrDefault() as Character;
        Func<bool> autoCloseCondition = null;
        if (!string.IsNullOrEmpty(CloseOnInput) && Enum.TryParse(CloseOnInput, true, out InputType closeOnInput))
        {
            autoCloseCondition = () => PlayerInput.KeyDown(closeOnInput);
        }
        else if (!CloseOnSelectTag.IsEmpty)
        {
            autoCloseCondition = () => character?.SelectedItem != null && character.SelectedItem.HasTag(CloseOnSelectTag);
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
            autoCloseCondition = () => character?.CurrentHull == null || character.CurrentHull.RoomName.ToIdentifier() != CloseOnExitRoomName;
        }
        else if (!CloseOnInRoomName.IsEmpty)
        {
            autoCloseCondition = () => character?.CurrentHull != null && character.CurrentHull.RoomName.ToIdentifier() == CloseOnInRoomName;
        }
        return autoCloseCondition;
    }
}