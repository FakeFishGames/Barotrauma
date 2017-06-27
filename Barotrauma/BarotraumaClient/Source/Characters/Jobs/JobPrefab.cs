using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    partial class JobPrefab
    {
        public GUIFrame CreateInfoFrame()
        {
            int width = 500, height = 400;

            GUIFrame backFrame = new GUIFrame(Rectangle.Empty, Color.Black * 0.5f);
            backFrame.Padding = Vector4.Zero;

            GUIFrame frame = new GUIFrame(new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), "", backFrame);
            frame.Padding = new Vector4(30.0f, 30.0f, 30.0f, 30.0f);

            new GUITextBlock(new Rectangle(0, 0, 100, 20), Name, "", Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.LargeFont);

            var descriptionBlock = new GUITextBlock(new Rectangle(0, 40, 0, 0), Description, "", Alignment.TopLeft, Alignment.TopLeft, frame, true, GUI.SmallFont);

            new GUITextBlock(new Rectangle(0, 40 + descriptionBlock.Rect.Height + 20, 100, 20), "Skills: ", "", Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.LargeFont);

            int y = 40 + descriptionBlock.Rect.Height + 50;
            foreach (SkillPrefab skill in Skills)
            {
                string skillDescription = Skill.GetLevelName((int)skill.LevelRange.X);
                string skillDescription2 = Skill.GetLevelName((int)skill.LevelRange.Y);

                if (skillDescription2 != skillDescription)
                {
                    skillDescription += "/" + skillDescription2;
                }
                new GUITextBlock(new Rectangle(0, y, 100, 20),
                    "   - " + skill.Name + ": " + skillDescription, "", Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.SmallFont);

                y += 20;
            }

            new GUITextBlock(new Rectangle(250, 40 + descriptionBlock.Rect.Height + 20, 0, 20), "Items: ", "", Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.LargeFont);

            y = 40 + descriptionBlock.Rect.Height + 50;
            foreach (string itemName in ItemNames)
            {
                new GUITextBlock(new Rectangle(250, y, 100, 20),
                "   - " + itemName, "", Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.SmallFont);

                y += 20;
            }

            return backFrame;
        }
    }
}
