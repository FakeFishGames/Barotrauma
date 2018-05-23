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
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), GuiFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform),
                TextManager.Get("OptimizableLabel"), textAlignment: Alignment.TopCenter, font: GUI.LargeFont);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                Description, font: GUI.SmallFont, wrap: true);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform),
                TextManager.Get("OptimizableRequiredSkills"));
            for (int i = 0; i < requiredSkills.Count; i++)
            {
                var skillText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform),
                    "   - " + requiredSkills[i].Name + ": " + ((int)requiredSkills[i].Level), font: GUI.SmallFont)
                {
                    UserData = requiredSkills[i]
                };
            }

            progressBar = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform), 
                color: Color.Green, barSize: 0.0f);

            optimizeButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.15f), paddedFrame.RectTransform, Anchor.TopCenter),
                TextManager.Get("OptimizableOptimize"))
            {
                OnClicked = (btn, obj) =>
                {
                    currentOptimizer = Character.Controlled;
                    item.CreateClientEvent(this);
                    return true;
                }
            };
        }

        public override void AddToGUIUpdateList()
        {
            if (!currentlyOptimizable.Contains(this) || Character.Controlled == null || DegreeOfSuccess(Character.Controlled) < 0.5f) return;
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character, float deltaTime)
        {
            if (!currentlyOptimizable.Contains(this) || character == null || DegreeOfSuccess(character) < 0.5f) return;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (!currentlyOptimizable.Contains(this) || character == null || DegreeOfSuccess(character) < 0.5f) return;
            IsActive = true;

            progressBar.BarSize = optimizationProgress;

            optimizeButton.Enabled = true;
            foreach (GUIComponent c in GuiFrame.Children)
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
