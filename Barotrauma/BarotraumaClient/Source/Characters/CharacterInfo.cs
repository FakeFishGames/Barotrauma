using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class CharacterInfo
    {

        public GUIFrame CreateInfoFrame(Rectangle rect)
        {
            GUIFrame frame = new GUIFrame(rect, Color.Transparent);
            frame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            return CreateInfoFrame(frame);
        }

        public GUIFrame CreateInfoFrame(GUIFrame frame)
        {
            new GUIImage(new Rectangle(0, 0, 30, 30), HeadSprite, Alignment.TopLeft, frame);

            ScalableFont font = frame.Rect.Width < 280 ? GUI.SmallFont : GUI.Font;

            int x = 0, y = 0;
            new GUITextBlock(new Rectangle(x + 60, y, 200, 20), Name, "", frame, font);
            y += 20;

            if (Job != null)
            {
                new GUITextBlock(new Rectangle(x + 60, y, 200, 20), Job.Name, "", frame, font);
                y += 25;
            }

            if (personalityTrait != null)
            {
                new GUITextBlock(new Rectangle(x, y, 200, 20), "Trait: " + personalityTrait.Name, "", frame, font);
                y += 25;
            }

            if (Job != null)
            {
                var skills = Job.Skills;
                skills.Sort((s1, s2) => -s1.Level.CompareTo(s2.Level));

                new GUITextBlock(new Rectangle(x, y, 200, 20), TextManager.Get("Skills") + ":", "", frame, font);
                y += 20;
                foreach (Skill skill in skills)
                {
                    Color textColor = Color.White * (0.5f + skill.Level / 200.0f);
                    new GUITextBlock(new Rectangle(x, y, 200, 20), skill.Name, Color.Transparent, textColor, Alignment.Left, "", frame).Font = font;
                    new GUITextBlock(new Rectangle(x, y, 200, 20), skill.Level.ToString(), Color.Transparent, textColor, Alignment.Right, "", frame).Font = font;
                    y += 20;
                }
            }


            return frame;
        }

        public GUIFrame CreateCharacterFrame(GUIComponent parent, string text, object userData)
        {
            GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 40), Color.Transparent, "ListBoxElement", parent);
            frame.UserData = userData;

            GUITextBlock textBlock = new GUITextBlock(
                new Rectangle(40, 0, 0, 25),
                text,
                null, null,
                Alignment.Left, Alignment.Left,
                "", frame, false);
            textBlock.Font = GUI.SmallFont;
            textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

            new GUIImage(new Rectangle(-5, -5, 0, 0), HeadSprite, Alignment.Left, frame);

            return frame;
        }

    }
}
