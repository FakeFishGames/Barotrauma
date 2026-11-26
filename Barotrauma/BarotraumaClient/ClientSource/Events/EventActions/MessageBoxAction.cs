using System;
using System.Linq;

namespace Barotrauma;

partial class MessageBoxAction : EventAction
{
    partial void UpdateProjSpecific()
    {
        Show(ToMessageBoxData());
    }

    public static void Show(MessageBoxData data)
    {
        if (data.Type is ActionType.Create or ActionType.ConnectObjective)
        {
            CreateMessageBox(data);

            if (!data.ObjectiveTag.IsEmpty)
            {
                Identifier id = data.Identifier.IfEmpty(data.Text);

                var segment = ObjectiveManager.Segment.CreateMessageBoxSegment(id, data.ObjectiveTag, () => CreateMessageBox(data));

                segment.CanBeCompleted = data.ObjectiveCanBeCompleted;
                segment.ParentId = data.ParentObjectiveId;

                ObjectiveManager.TriggerSegment(segment, connectObjective: data.Type == ActionType.ConnectObjective);
            }
        }
        else if (data.Type == ActionType.Close)
        {
            GUIMessageBox.Close(data.Tag);
        }
        else if (data.Type == ActionType.Clear)
        {
            GUIMessageBox.CloseAll();
        }
    }

    private static void CreateMessageBox(MessageBoxData data)
    {
        bool isDefaultStyle = data.Style == BoxStyle.Default;
        var messageBox = new GUIMessageBox(
            headerText: TextManager.Get(data.Header).Fallback(data.Header.ToString()),
            text: RichString.Rich(TextManager.ParseInputTypes(TextManager.Get(data.Text).Fallback(data.Text.ToString()), useColorHighlight: true)),
            buttons: isDefaultStyle ? new LocalizedString[] { TextManager.Get("OK") } : Array.Empty<LocalizedString>(),
            type: isDefaultStyle ? GUIMessageBox.Type.Default : GUIMessageBox.Type.Tutorial,
            tag: data.Tag,
            iconStyle: data.IconStyle,
            autoCloseCondition: GetAutoCloseCondition(data, Character.Controlled),
            hideCloseButton: data.HideCloseButton)
        {
            FlashOnAutoCloseCondition = true
        };
        if (isDefaultStyle)
        {
            messageBox.Buttons[0].OnClicked = messageBox.Close;
        }
    }

    private static Func<bool> GetAutoCloseCondition(MessageBoxData data, Character character)
    {
        Func<bool> autoCloseCondition = null;
        if (!string.IsNullOrEmpty(data.CloseOnInput) && Enum.TryParse(data.CloseOnInput, true, out InputType closeOnInput))
        {
            autoCloseCondition = () => PlayerInput.KeyDown(closeOnInput);
        }
        else if (!data.CloseOnSelectTag.IsEmpty)
        {
            autoCloseCondition = () => character?.SelectedItem != null && character.SelectedItem.HasTag(data.CloseOnSelectTag);
        }
        else if (!data.CloseOnPickUpTag.IsEmpty)
        {
            autoCloseCondition = () => character?.Inventory != null && character.Inventory.FindItemByTag(data.CloseOnPickUpTag, recursive: true) != null;
        }
        else if (!data.CloseOnEquipTag.IsEmpty)
        {
            autoCloseCondition = () => character != null && character.HasEquippedItem(data.CloseOnEquipTag);
        }
        else if (!data.CloseOnExitRoomName.IsEmpty)
        {
            autoCloseCondition = () => character?.CurrentHull == null || character.CurrentHull.RoomName.ToIdentifier() != data.CloseOnExitRoomName;
        }
        else if (!data.CloseOnInRoomName.IsEmpty)
        {
            autoCloseCondition = () => character?.CurrentHull != null && character.CurrentHull.RoomName.ToIdentifier() == data.CloseOnInRoomName;
        }
        return autoCloseCondition;
    }
}