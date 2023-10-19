﻿using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class RemoteController : ItemComponent
    {
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            currentTarget?.DrawHUD(spriteBatch, Screen.Selected.Cam, character);
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            currentTarget?.UpdateHUD(cam, character,deltaTime);
        }

        public override void AddToGUIUpdateList(int order = 0)
        {
            currentTarget?.AddToGUIUpdateList(order: -1);
        }
    }
}
