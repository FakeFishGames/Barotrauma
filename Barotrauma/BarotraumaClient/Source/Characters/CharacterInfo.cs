using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        public XElement InventoryData;

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
                    Job.Name, textColor: Job.Prefab.UIColor, font: font);
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
                        TextManager.Get("SkillName." + skill.Identifier), textColor: textColor, font: font);
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(x, y) },
                        ((int)skill.Level).ToString(), textColor: textColor, font: font, textAlignment: Alignment.TopRight);
                    y += 20;
                }
            }


            return frame;
        }

        public GUIFrame CreateCharacterFrame(GUIComponent parent, string text, object userData)
        {
            GUIFrame frame = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, 40), parent.RectTransform) { IsFixedSize = false }, "ListBoxElement")
            {
                UserData = userData
            };

            GUITextBlock textBlock = new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(40, 0) }, text, font: GUI.SmallFont);
            new GUIImage(new RectTransform(new Point(frame.Rect.Height, frame.Rect.Height), frame.RectTransform, Anchor.CenterLeft) { IsFixedSize = false }, HeadSprite);            

            return frame;
        }

        partial void OnSkillChanged(string skillIdentifier, float prevLevel, float newLevel, Vector2 textPopupPos)
        {
            if (newLevel - prevLevel > 0.1f)
            {
                GUI.AddMessage(
                    "+" + ((int)((newLevel - prevLevel) * 100.0f)).ToString() + " XP",
                    Color.Green,
                    textPopupPos,
                    Vector2.UnitY * 10.0f);
            }
            else if (prevLevel % 0.1f > 0.05f && newLevel % 0.1f < 0.05f)
            {
                GUI.AddMessage(
                    "+10 XP",
                    Color.Green,
                    textPopupPos,
                    Vector2.UnitY * 10.0f);
            }

            if ((int)newLevel > (int)prevLevel)
            {
                GUI.AddMessage(
                    TextManager.Get("SkillIncreased")
                        .Replace("[name]", Name)
                        .Replace("[skillname]", TextManager.Get("SkillName." + skillIdentifier))
                        .Replace("[newlevel]", ((int)newLevel).ToString()),
                    Color.Green);
            }
        }

        public static CharacterInfo ClientRead(string configPath, NetBuffer inc)
        {
            ushort infoID = inc.ReadUInt16();
            string newName = inc.ReadString();
            bool isFemale = inc.ReadBoolean();
            int headSpriteID = inc.ReadByte();
            string jobIdentifier = inc.ReadString();

            JobPrefab jobPrefab = null;
            Dictionary<string, float> skillLevels = new Dictionary<string, float>();
            if (!string.IsNullOrEmpty(jobIdentifier))
            {
                jobPrefab = JobPrefab.List.Find(jp => jp.Identifier == jobIdentifier);
                int skillCount = inc.ReadByte();
                for (int i = 0; i < skillCount; i++)
                {
                    string skillIdentifier = inc.ReadString();
                    float skillLevel = inc.ReadSingle();
                    skillLevels.Add(skillIdentifier, skillLevel);
                }
            }

            CharacterInfo ch = new CharacterInfo(configPath, newName, isFemale ? Gender.Female : Gender.Male, jobPrefab)
            {
                ID = infoID,
                HeadSpriteId = headSpriteID
            };

            System.Diagnostics.Debug.Assert(skillLevels.Count == ch.Job.Skills.Count);
            if (ch.Job != null)
            {
                foreach (KeyValuePair<string, float> skill in skillLevels)
                {
                    Skill matchingSkill = ch.Job.Skills.Find(s => s.Identifier == skill.Key);
                    if (matchingSkill == null)
                    {
                        DebugConsole.ThrowError("Skill \"" + skill.Key + "\" not found in character \"" + newName + "\"");
                        continue;
                    }
                    matchingSkill.Level = skill.Value;
                }
            }
            return ch;
        }
    }
}
