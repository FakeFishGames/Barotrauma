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


        [Serialize("", false)]
        public string Description
        {
            get;
            set;
        }


        partial void InitProjSpecific(XElement element)
        {
            new GUITextBlock(new Rectangle(0, -30, 0, 20), "Device can be optimized", "", Alignment.TopCenter, Alignment.TopCenter, GuiFrame, false, GUI.LargeFont);

            new GUITextBlock(new Rectangle(0, 0, 0, 50), Description, "", Alignment.TopLeft, Alignment.TopLeft, GuiFrame, true, GUI.SmallFont);

            new GUITextBlock(new Rectangle(0, 50, 100, 20), "Required skills:", "", GuiFrame);
            for (int i = 0; i < requiredSkills.Count; i++)
            {
                var skillText = new GUITextBlock(new Rectangle(0, 65 + i * 15, 100, 20), "   - " + requiredSkills[i].Name + ": " + ((int)requiredSkills[i].Level), "", GuiFrame, GUI.SmallFont);
                skillText.UserData = requiredSkills[i];
            }

            progressBar = new GUIProgressBar(new Rectangle(0, 15, (int)((GuiFrame.Rect.Width - GuiFrame.Padding.X - GuiFrame.Padding.Z) * 0.6f), 20), Color.Green, 0.0f, Alignment.BottomRight, GuiFrame);

            optimizeButton = new GUIButton(new Rectangle(0, 15, (int)((GuiFrame.Rect.Width - GuiFrame.Padding.X - GuiFrame.Padding.Z) * 0.3f), 20), "Optimize", Alignment.BottomLeft, "", GuiFrame);
            optimizeButton.OnClicked = (btn, obj) =>
            {
                currentOptimizer = Character.Controlled;
                item.CreateClientEvent(this);
                return true;
            };
        }

        public override void AddToGUIUpdateList()
        {
            if (!currentlyOptimizable.Contains(this) || Character.Controlled == null || DegreeOfSuccess(Character.Controlled) < 0.5f) return;
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            if (!currentlyOptimizable.Contains(this) || character == null || DegreeOfSuccess(character) < 0.5f) return;
            GuiFrame.Update(1.0f / 60.0f);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (!currentlyOptimizable.Contains(this) || character == null || DegreeOfSuccess(character) < 0.5f) return;
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


        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            isOptimized = msg.ReadBoolean();
            if (isOptimized)
            {
                optimizedTimer = msg.ReadRangedSingle(0.0f, OptimizationDuration, 16);
                currentlyOptimizable.Remove(this);
            }
            else
            {
                bool isCurrentlyOptimizable = msg.ReadBoolean();
                if (isCurrentlyOptimizable)
                {
                    currentlyOptimizable.Add(this);
                    optimizableTimer = msg.ReadRangedSingle(0.0f, OptimizableDuration, 16);
                    optimizationProgress = msg.ReadRangedSingle(0.0f, 1.0f, 8);
                }
                else
                {
                    currentlyOptimizable.Remove(this);
                }
            }
        }
    }
}
