using Barotrauma.Networking;
using Barotrauma.Particles;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Sabotageable : ItemComponent, IDrawableComponent
    {
        public GUIButton SabotageButton
        {
            get { return sabotageButton; }
        }
        private GUIButton sabotageButton;
        private GUIProgressBar progressBar;

        private string sabotageButtonText, sabotagingText;

        [Serialize("", false)]
        public string Description
        {
            get;
            set;
        }

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        public override bool ShouldDrawHUD(Character character)
        {
            if (!HasRequiredItems(character, false) || character.SelectedConstruction != item) return false;
            return (item.Condition > 0.0f || (currentFixer == character && item.IsFullCondition));
        }

        partial void InitProjSpecific(XElement element)
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
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
                    "   - " + TextManager.AddPunctuation(':', TextManager.Get("SkillName." + requiredSkills[i].Identifier), ((int)requiredSkills[i].Level).ToString()), 
                    font: GUI.SmallFont)
                {
                    UserData = requiredSkills[i]
                };
            }

            progressBar = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform), 
                color: Color.Green, barSize: 0.0f);

            sabotageButtonText = TextManager.Get("SabotageButton");
            sabotagingText = TextManager.Get("Sabotaging");
            sabotageButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.15f), paddedFrame.RectTransform, Anchor.TopCenter),
                sabotageButtonText/*, Alignment.Center, "", Color.Red*/)
            {
                OnClicked = (btn, obj) =>
                {
                    CurrentFixer = Character.Controlled;
                    item.CreateClientEvent(this);
                    return true;
                }
            };
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
        }
        
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            IsActive = true;

            progressBar.BarSize = item.Condition / item.MaxCondition;
            progressBar.Color = ToolBox.GradientLerp(progressBar.BarSize, Color.Red, Color.Orange, Color.Green);

            sabotageButton.Enabled = currentFixer == null;
            sabotageButton.Text = currentFixer == null ? 
                sabotageButtonText : 
                sabotagingText + new string('.', ((int)(Timing.TotalTime * 2.0f) % 3) + 1);

            System.Diagnostics.Debug.Assert(GuiFrame.GetChild(0) is GUILayoutGroup, "Sabotage UI hierarchy has changed, could not find skill texts");
            foreach (GUIComponent c in GuiFrame.GetChild(0).Children)
            {
                if (!(c.UserData is Skill skill)) continue;

                GUITextBlock textBlock = (GUITextBlock)c;
                if (character.GetSkillLevel(skill.Identifier) < skill.Level)
                {
                    textBlock.TextColor = Color.Red;
                }
                else
                {
                    textBlock.TextColor = Color.White;
                }
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            // TODO(xxx): deteriorationTimer = msg.ReadSingle();
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            //no need to write anything, just letting the server know we started repairing
        }

        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (GameMain.DebugDraw && Character.Controlled?.FocusedItem == item)
            {
                /* TODO(xxx): 
                bool paused = !ShouldDeteriorate();
                if (deteriorationTimer > 0.0f)
                {
                    GUI.DrawString(spriteBatch,
                        new Vector2(item.WorldPosition.X, -item.WorldPosition.Y), "Deterioration delay " + ((int)deteriorationTimer) + (paused ? " [PAUSED]" : ""),
                        paused ? Color.Cyan : Color.Lime, Color.Black * 0.5f);
                }
                else
                {
                    GUI.DrawString(spriteBatch,
                        new Vector2(item.WorldPosition.X, -item.WorldPosition.Y), "Deteriorating at " + (int)(DeteriorationSpeed * 60.0f) + " units/min" + (paused ? " [PAUSED]" : ""),
                        paused ? Color.Cyan : Color.Red, Color.Black * 0.5f);
                }
                */
                GUI.DrawString(spriteBatch,
                    new Vector2(item.WorldPosition.X, -item.WorldPosition.Y + 20), "Condition: " + (int)item.Condition + "/" + (int)item.MaxCondition,
                    Color.Orange);
            }
        }
    }
}
