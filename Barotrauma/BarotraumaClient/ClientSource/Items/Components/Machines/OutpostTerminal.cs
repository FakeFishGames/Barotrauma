﻿
namespace Barotrauma.Items.Components
{
    partial class OutpostTerminal : ItemComponent
    {
        private SubmarineSelection selectionUI;

        public override bool Select(Character character)
        {
            if (GameMain.GameSession?.Campaign == null || !Level.IsLoadedFriendlyOutpost)
            {
                return false;
            }

            if (selectionUI == null)
            {
                selectionUI = new SubmarineSelection(true, null, GUI.Canvas);
            }

            GuiFrame = selectionUI.GuiFrame;
            selectionUI.RefreshSubmarineDisplay(true, setTransferOptionToTrue: true);
            IsActive = true;
            return base.Select(character);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (Character.Controlled?.SelectedItem != item)
            {
                IsActive = false;
                return;
            }

            base.Update(deltaTime, cam);

            selectionUI?.Update();
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            if (selectionUI != null)
            {
                selectionUI.GuiFrame.RectTransform.Parent = null;
                selectionUI = null;
            }
        }
    }
}