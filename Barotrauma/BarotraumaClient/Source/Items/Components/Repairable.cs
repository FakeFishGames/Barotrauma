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
    partial class Repairable : ItemComponent
    {
        private GUIButton optimizeButton;
        private GUIProgressBar progressBar;
        
        [Serialize("", false)]
        public string Description
        {
            get;
            set;
        }
        
        public override bool ShouldDrawHUD(Character character)
        {
            return item.Condition < ShowRepairUIThreshold && HasRequiredItems(character, false);
        }

        partial void InitProjSpecific(XElement element)
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), GuiFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform),
                header, textAlignment: Alignment.TopCenter, font: GUI.LargeFont);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                Description, font: GUI.SmallFont, wrap: true);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform),
                TextManager.Get("RequiredRepairSkills"));
            for (int i = 0; i < requiredSkills.Count; i++)
            {
                var skillText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform),
                    "   - " + TextManager.Get("SkillName." + requiredSkills[i].Identifier) + ": " + ((int)requiredSkills[i].Level), font: GUI.SmallFont)
                {
                    UserData = requiredSkills[i]
                };
            }

            progressBar = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform), 
                color: Color.Green, barSize: 0.0f);

            optimizeButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.15f), paddedFrame.RectTransform, Anchor.TopCenter),
                TextManager.Get("RepairButton"))
            {
                OnClicked = (btn, obj) =>
                {
                    currentFixer = Character.Controlled;
                    item.CreateClientEvent(this);
                    return true;
                }
            };
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            IsActive = true;

            progressBar.BarSize = repairProgress;
            progressBar.Color = repairProgress < 0.5f ?
                Color.Lerp(Color.Red, Color.Orange, repairProgress * 2.0f) :
                Color.Lerp(Color.Orange, Color.Green, (repairProgress - 0.5f) * 2.0f);

            optimizeButton.Enabled = true;
            foreach (GUIComponent c in GuiFrame.Children)
            {
                Skill skill = c.UserData as Skill;
                if (skill == null) continue;

                GUITextBlock textBlock = (GUITextBlock)c;
                if (character.GetSkillLevel(skill.Identifier) < skill.Level)
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
    }
}
