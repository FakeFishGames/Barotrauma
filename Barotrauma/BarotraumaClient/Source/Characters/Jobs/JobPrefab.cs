using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma
{
    partial class JobPrefab
    {
        public GUIButton CreateInfoFrame()
        {
            int width = 500, height = 400;

            GUIButton backFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker");
            GUIFrame frame = new GUIFrame(new RectTransform(new Point(width, height), backFrame.RectTransform, Anchor.Center));
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), frame.RectTransform, Anchor.Center), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), Name, font: GUI.LargeFont);

            var descriptionBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.15f) },
                Description, font: GUI.SmallFont, wrap: true);

            var skillContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 0.5f), paddedFrame.RectTransform)
                { RelativeOffset = new Vector2(0.0f, 0.2f + descriptionBlock.RectTransform.RelativeSize.Y) });
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillContainer.RectTransform),
                TextManager.Get("Skills"), font: GUI.LargeFont);
            foreach (SkillPrefab skill in Skills)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillContainer.RectTransform),
                    "   - " + TextManager.AddPunctuation(':', TextManager.Get("SkillName." + skill.Identifier), (int)skill.LevelRange.X + " - " + (int)skill.LevelRange.Y), 
                    font: GUI.SmallFont);
            }

            var itemContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 0.5f), paddedFrame.RectTransform, Anchor.TopRight)
            { RelativeOffset = new Vector2(0.0f, 0.2f + descriptionBlock.RectTransform.RelativeSize.Y) })
            {
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), itemContainer.RectTransform),
                TextManager.Get("Items", fallBackTag: "mapentitycategory.equipment"), font: GUI.LargeFont);
            foreach (string itemName in ItemNames.Distinct())
            {
                int count = ItemNames.Count(i => i == itemName);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), itemContainer.RectTransform),
                    "   - " + (count == 1 ? itemName : itemName + " x" + count),
                    font: GUI.SmallFont);
            }

            return backFrame;
        }
    }
}
