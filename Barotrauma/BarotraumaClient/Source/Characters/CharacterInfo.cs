using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        public GUIFrame CreateInfoFrame(GUIFrame frame)
        {
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), frame.RectTransform, Anchor.Center), null);

            new GUIImage(new RectTransform(new Point(30, 30), paddedFrame.RectTransform), HeadSprite);
            
            ScalableFont font = frame.Rect.Width < 280 ? GUI.SmallFont : GUI.Font;

            int x = 0, y = 0;
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x + 60, y) }, 
                Name, font: font);            
            y += 20;

            if (Job != null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x + 60, y) }, 
                    Job.Name, font: font);
                y += 25;
            }

            if (personalityTrait != null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x + 60, y) },
                    "Trait: " + personalityTrait.Name, font: font);
                y += 25;
            }

            if (Job != null)
            {
                var skills = Job.Skills;
                skills.Sort((s1, s2) => -s1.Level.CompareTo(s2.Level));

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x, y) },
                    TextManager.Get("Skills") + ":", font: font);
                
                y += 20;
                foreach (Skill skill in skills)
                {
                    Color textColor = Color.White * (0.5f + skill.Level / 200.0f);

                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x, y) },
                        skill.Name, textColor: textColor, font: font);
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(x, y) },
                        ((int)skill.Level).ToString(), textColor: textColor, font: font, textAlignment: Alignment.TopRight);
                    y += 20;
                }
            }


            return frame;
        }

        public GUIFrame CreateCharacterFrame(GUIComponent parent, string text, object userData)
        {
            GUIFrame frame = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, 40), parent.RectTransform), "ListBoxElement")
            {
                UserData = userData
            };

            GUITextBlock textBlock = new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform) { AbsoluteOffset = new Point(40, 0) }, text, font: GUI.SmallFont);
            new GUIImage(new RectTransform(new Point(frame.Rect.Height, frame.Rect.Height), frame.RectTransform, Anchor.CenterLeft), HeadSprite);            

            return frame;
        }

    }
}
