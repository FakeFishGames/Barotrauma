using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    internal partial class CampaignMetadata
    {
        private const int MaxDrawnElements = 12;

        public void DebugDraw(SpriteBatch spriteBatch, Vector2 pos, int debugDrawMetadataOffset, string[] ignoredMetadataInfo)
        {
            var campaignData = data;
            foreach (string ignored in ignoredMetadataInfo)
            {
                if (!string.IsNullOrWhiteSpace(ignored))
                {
                    campaignData = campaignData.Where(pair => !pair.Key.StartsWith(ignored)).ToDictionary(i => i.Key, i => i.Value);
                }
            }
            
            int offset = 0;;
            if (campaignData.Count > 0)
            {
                offset = debugDrawMetadataOffset % campaignData.Count;
                if (offset < 0) { offset += campaignData.Count; }
            }

            var text = "Campaign metadata:\n";

            int max = 0;
            for (int i = offset; i < campaignData.Count + offset; i++)
            {
                int index = i;
                if (index >= campaignData.Count) { index -= campaignData.Count; }

                var (key, value) = campaignData.ElementAt(index);

                if (max < MaxDrawnElements)
                {
                    text += $"{key.ColorizeObject()}: {value.ColorizeObject()}\n";
                    max++;
                }
                else
                {
                    text += "Use arrow keys to scroll";
                    break;
                }
            }

            text = text.TrimEnd('\n');

            ImmutableArray<RichTextData>? richTextDatas = RichTextData.GetRichTextData(text, out text);

            Vector2 size = GUIStyle.SmallFont.MeasureString(text);
            Vector2 infoPos = new Vector2(GameMain.GraphicsWidth - size.X - 16, pos.Y + 8);
            Rectangle infoRect = new Rectangle(infoPos.ToPoint(), size.ToPoint());
            infoRect.Inflate(8, 8);
            GUI.DrawRectangle(spriteBatch, infoRect, Color.Black * 0.8f, isFilled: true);
            GUI.DrawRectangle(spriteBatch, infoRect, Color.White * 0.8f);

            if (richTextDatas != null && richTextDatas.Value.Any())
            {
                GUI.DrawStringWithColors(spriteBatch, infoPos, text, Color.White, richTextDatas.Value, font: GUIStyle.SmallFont);
            }
            else
            {
                GUI.DrawString(spriteBatch, infoPos, text, Color.White, font: GUIStyle.SmallFont);
            }

            float y = infoRect.Bottom + 16;
            if (Campaign.Factions != null)
            {
                const string factionHeader = "Reputations";
                Vector2 factionHeaderSize = GUIStyle.SubHeadingFont.MeasureString(factionHeader);
                Vector2 factionPos = new Vector2(GameMain.GraphicsWidth - (264 / 2) - factionHeaderSize.X / 2, y);

                GUI.DrawString(spriteBatch, factionPos, factionHeader, Color.White, font: GUIStyle.SubHeadingFont);
                y += factionHeaderSize.Y + 8;

                foreach (Faction faction in Campaign.Factions)
                {
                    LocalizedString name = faction.Prefab.Name;
                    Vector2 nameSize = GUIStyle.SmallFont.MeasureString(name);
                    GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 264, y), name, Color.White, font: GUIStyle.SmallFont);
                    y += nameSize.Y + 5;

                    Color color = ToolBox.GradientLerp(faction.Reputation.NormalizedValue, Color.Red, Color.Yellow, Color.LightGreen);
                    GUI.DrawRectangle(spriteBatch, new Rectangle(GameMain.GraphicsWidth - 264, (int) y, (int)(faction.Reputation.NormalizedValue * 255), 10), color, isFilled: true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle(GameMain.GraphicsWidth - 264, (int) y, 256, 10), Color.White);
                    y += 15;
                }
            }

            Location location = Campaign.Map?.CurrentLocation;
            if (location?.Reputation != null)
            {
                string name = Campaign.Map?.CurrentLocation.Name;
                Vector2 nameSize = GUIStyle.SmallFont.MeasureString(name);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 264, y), name, Color.White, font: GUIStyle.SmallFont);
                y += nameSize.Y + 5;

                float normalizedReputation = MathUtils.InverseLerp(location.Reputation.MinReputation, location.Reputation.MaxReputation, location.Reputation.Value);
                Color color = ToolBox.GradientLerp(normalizedReputation, Color.Red, Color.Yellow, Color.LightGreen);
                GUI.DrawRectangle(spriteBatch, new Rectangle(GameMain.GraphicsWidth - 264, (int) y, (int)(normalizedReputation * 255), 10), color, isFilled: true);
                GUI.DrawRectangle(spriteBatch, new Rectangle(GameMain.GraphicsWidth - 264, (int) y, 256, 10), Color.White);
            }
        }
    }
}