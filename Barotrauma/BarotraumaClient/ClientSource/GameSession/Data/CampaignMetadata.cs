using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    internal partial class CampaignMetadata
    {
        private const int MaxDrawnElements = 12;

        public void DebugDraw(SpriteBatch spriteBatch, Vector2 pos, CampaignMode campaign, GUI.DebugDrawMetaData debugDrawMetaData)
        {
            var campaignData = data;
            if (!debugDrawMetaData.FactionMetadata) { removeData("reputation.faction"); }
            if (!debugDrawMetaData.UpgradeLevels) { removeData("upgrade."); }
            if (!debugDrawMetaData.UpgradePrices) { removeData("upgradeprice."); }

            void removeData(string keyStartsWith)
            {
                campaignData = campaignData.Where(pair => !pair.Key.StartsWith(keyStartsWith)).ToDictionary(i => i.Key, i => i.Value);
            }
            
            int offset = 0;;
            if (campaignData.Count > 0)
            {
                offset = debugDrawMetaData.Offset % campaignData.Count;
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
            if (campaign.Factions != null)
            {
                const string factionHeader = "Reputations";
                Vector2 factionHeaderSize = GUIStyle.SubHeadingFont.MeasureString(factionHeader);
                Vector2 factionPos = new Vector2(GameMain.GraphicsWidth - (264 / 2) - factionHeaderSize.X / 2, y);

                GUI.DrawString(spriteBatch, factionPos, factionHeader, Color.White, font: GUIStyle.SubHeadingFont);
                y += factionHeaderSize.Y + 8;

                foreach (Faction faction in campaign.Factions)
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
        }
    }
}