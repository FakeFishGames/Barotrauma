using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class JobPrefab : PrefabWithUintIdentifier
    {
        public GUIButton CreateInfoFrame(bool isPvP, out GUIComponent buttonContainer)
        {
            int width = 500, height = 400;

            GUIButton frameHolder = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null);
            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, frameHolder.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

            GUIFrame frame = new GUIFrame(new RectTransform(new Point(width, height), frameHolder.RectTransform, Anchor.Center));
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), frame.RectTransform, Anchor.Center), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), Name, font: GUIStyle.LargeFont);

            var descriptionBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.15f) },
                Description, font: GUIStyle.SmallFont, wrap: true);

            var skillContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 0.5f), paddedFrame.RectTransform)
                { RelativeOffset = new Vector2(0.0f, 0.2f + descriptionBlock.RectTransform.RelativeSize.Y) });
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillContainer.RectTransform),
                TextManager.Get("Skills"), font: GUIStyle.LargeFont);
            foreach (SkillPrefab skill in Skills)
            {
                var levelRange = skill.GetLevelRange(isPvP);

                string levelStr = 
                    levelRange.End > levelRange.Start ? 
                    (int)levelRange.Start + " - " + (int)levelRange.End : 
                    ((int)levelRange.Start).ToString();
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillContainer.RectTransform),
                    "   - " + TextManager.AddPunctuation(':', TextManager.Get("SkillName." + skill.Identifier), levelStr), 
                    font: GUIStyle.SmallFont);
            }

            buttonContainer = paddedFrame;
            
            return frameHolder;
        }
        
        public IEnumerable<Sprite> GetJobOutfitSprites(CharacterTeamType team, bool isPvPMode)
        {
            var equipIdentifiers = JobItems
                .SelectMany(kvp => kvp.Value)
                .Where(j => j.Outfit)
                .Select(j => j.GetItemIdentifier(team, isPvPMode));

            List<ItemPrefab> outfitPrefabs = new List<ItemPrefab>();
            foreach (var equipIdentifier in equipIdentifiers)
            {
                var itemPrefab = ItemPrefab.Prefabs.Find(ip => ip.Identifier == equipIdentifier);
                if (itemPrefab != null) { outfitPrefabs.Add(itemPrefab); }
            }

            if (!outfitPrefabs.Any()) { return Enumerable.Empty<Sprite>(); }

            return outfitPrefabs.Select(p => p.InventoryIcon ?? p.Sprite);
        }
    }
}
