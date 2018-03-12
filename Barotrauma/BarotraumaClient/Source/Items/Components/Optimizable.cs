using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Optimizable : ItemComponent
    {
        private GUIButton optimizeButton;
        private GUIProgressBar progressBar;

        partial void InitProjSpecific(XElement element)
        {
            new GUITextBlock(new Rectangle(0, 0, 0, 20), "Device can be optimized", "", Alignment.TopCenter, Alignment.TopCenter, GuiFrame, false, GUI.LargeFont);

            new GUITextBlock(new Rectangle(0, 30, 100, 20), "Required skills:", "", GuiFrame);
            for (int i = 0; i < requiredSkills.Count; i++)
            {
                var skillText = new GUITextBlock(new Rectangle(0, 50 + i * 20, 100, 20), "   - " + requiredSkills[i].Name + ": " + ((int)requiredSkills[i].Level), "", GuiFrame);
                skillText.UserData = requiredSkills[i];
            }

            progressBar = new GUIProgressBar(new Rectangle(0, -60, 200, 30), Color.Green, 0.0f, Alignment.BottomCenter, GuiFrame);

            optimizeButton = new GUIButton(new Rectangle(0, 0, 120, 30), "Optimize", Alignment.BottomCenter, "", GuiFrame);
            optimizeButton.OnClicked = (btn, obj) =>
            {
                currentOptimizer = Character.Controlled;
                item.CreateClientEvent(this);
                return true;
            };
        }

        public override void AddToGUIUpdateList()
        {
            if (!currentlyOptimizable.Contains(this)) return;
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            if (!currentlyOptimizable.Contains(this)) return;
            GuiFrame.Update(1.0f / 60.0f);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (!currentlyOptimizable.Contains(this) || character == null) return;
            IsActive = true;

            progressBar.BarSize = optimizationProgress;

            optimizeButton.Enabled = true;
            foreach (GUIComponent c in GuiFrame.children)
            {
                Skill skill = c.UserData as Skill;
                if (skill == null) continue;

                GUITextBlock textBlock = (GUITextBlock)c;
                if (character.GetSkillLevel(skill.Name) < skill.Level)
                {
                    textBlock.TextColor = Color.Red;
                    optimizeButton.Enabled = false;
                }
                else
                {
                    textBlock.TextColor = Color.White;
                }
            }
            
            GuiFrame.Draw(spriteBatch);
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            msg.Write(
                currentOptimizer == Character.Controlled && 
                Character.Controlled != null &&
                Character.Controlled.SelectedConstruction == item);
        }
    }
}
