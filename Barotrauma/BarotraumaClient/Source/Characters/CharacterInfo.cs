using Barotrauma.Extensions;
using Barotrauma.Networking;
﻿using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        public GUIFrame CreateInfoFrame(GUIFrame frame)
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.7f), frame.RectTransform, Anchor.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.1f) })
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            var headerArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.4f), paddedFrame.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };

            new GUICustomComponent(new RectTransform(new Vector2(0.25f, 1.0f), headerArea.RectTransform), 
                onDraw: (sb, component) => DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()));

            ScalableFont font = paddedFrame.Rect.Width < 280 ? GUI.SmallFont : GUI.Font;

            var headerTextArea = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1.0f), headerArea.RectTransform))
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };

            Color? nameColor = null;
            if (Job != null) { nameColor = Job.Prefab.UIColor; }
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform),
                Name, textColor: nameColor, font: GUI.LargeFont)
            {
                Padding = Vector4.Zero,
                AutoScale = true
            };

            if (Job != null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform),
                    Job.Name, textColor: Job.Prefab.UIColor, font: font);
            }

            if (personalityTrait != null && TextManager.Language == "English")
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform),
                   TextManager.AddPunctuation(':', TextManager.Get("PersonalityTrait"), TextManager.Get("personalitytrait." + personalityTrait.Name.Replace(" ", ""))), font: font);
            }

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform), style: null);

            if (Job != null)
            {
                var skills = Job.Skills;
                skills.Sort((s1, s2) => -s1.Level.CompareTo(s2.Level));

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                    TextManager.Get("Skills") + ":", font: font);
                
                foreach (Skill skill in skills)
                {
                    Color textColor = Color.White * (0.5f + skill.Level / 200.0f);

                    var skillName = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                        TextManager.Get("SkillName." + skill.Identifier), textColor: textColor, font: font);
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), skillName.RectTransform),
                        ((int)skill.Level).ToString(), textColor: textColor, font: font, textAlignment: Alignment.CenterRight);
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

            Color? textColor = null;
            if (Job != null) { textColor = Job.Prefab.UIColor; }

            GUITextBlock textBlock = new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(40, 0) }, text, textColor: textColor, font: GUI.SmallFont);
            new GUICustomComponent(new RectTransform(new Point(frame.Rect.Height, frame.Rect.Height), frame.RectTransform, Anchor.CenterLeft) { IsFixedSize = false }, 
                onDraw: (sb, component) => DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()));
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
                    TextManager.GetWithVariables("SkillIncreased", new string[3] { "[name]", "[skillname]", "[newlevel]" },
                    new string[3] { Name, TextManager.Get("SkillName." + skillIdentifier), ((int)newLevel).ToString() },
                    new bool[3] { false, true, false }), Color.Green);
            }
        }

        partial void LoadAttachmentSprites(bool omitJob)
        {
            if (attachmentSprites == null)
            {
                attachmentSprites = new List<WearableSprite>();
            }
            if (!IsAttachmentsLoaded)
            {
                LoadHeadAttachments();
            }
            FaceAttachment?.Elements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.FaceAttachment)));
            BeardElement?.Elements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.Beard)));
            MoustacheElement?.Elements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.Moustache)));
            HairElement?.Elements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.Hair)));
            if (omitJob)
            {
                JobPrefab.NoJobElement?.Element("PortraitClothing")?.Elements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.JobIndicator)));
            }
            else
            {
                Job?.Prefab.ClothingElement?.Elements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.JobIndicator)));
            }
        }

        public void DrawPortrait(SpriteBatch spriteBatch, Vector2 screenPos, float targetWidth)
        {
            float backgroundScale = 1;
            if (PortraitBackground != null)
            {
                backgroundScale = targetWidth / PortraitBackground.size.X;
                PortraitBackground.Draw(spriteBatch, screenPos, scale: backgroundScale);
            }
            if (Portrait != null)
            {
                // Scale down the head sprite 10%
                float scale = targetWidth * 0.9f / Portrait.size.X;
                Vector2 offset = Portrait.size * backgroundScale / 4;
                Portrait.Draw(spriteBatch, screenPos + offset, scale: scale, spriteEffect: SpriteEffects.FlipHorizontally);
                if (AttachmentSprites != null)
                {
                    float depthStep = 0.000001f;
                    foreach (var attachment in AttachmentSprites)
                    {
                        DrawAttachmentSprite(spriteBatch, attachment, Portrait, screenPos + offset, scale, depthStep, SpriteEffects.FlipHorizontally);
                        depthStep += depthStep;
                    }
                }
            }
        }
        
        public void DrawIcon(SpriteBatch spriteBatch, Vector2 screenPos, Vector2 targetAreaSize)
        {
            if (HeadSprite != null)
            {
                float scale = Math.Min(targetAreaSize.X / HeadSprite.size.X, targetAreaSize.Y / HeadSprite.size.Y);
                HeadSprite.Draw(spriteBatch, screenPos, scale: scale);
                if (AttachmentSprites != null)
                {
                    float depthStep = 0.000001f;
                    foreach (var attachment in AttachmentSprites)
                    {
                        DrawAttachmentSprite(spriteBatch, attachment, HeadSprite, screenPos, scale, depthStep);
                        depthStep += depthStep;
                    }
                }
            }
        }

        private void DrawAttachmentSprite(SpriteBatch spriteBatch, WearableSprite attachment, Sprite head, Vector2 drawPos, float scale, float depthStep, SpriteEffects spriteEffects = SpriteEffects.None)
        {
            var list = AttachmentSprites.ToList();
            if (attachment.InheritSourceRect)
            {
                if (attachment.SheetIndex.HasValue)
                {
                    Point location = (head.SourceRect.Location + head.SourceRect.Size) * attachment.SheetIndex.Value;
                    attachment.Sprite.SourceRect = new Rectangle(location, head.SourceRect.Size);
                }
                else
                {
                    attachment.Sprite.SourceRect = head.SourceRect;
                }
            }
            Vector2 origin = attachment.Sprite.Origin;
            if (attachment.InheritOrigin)
            {
                origin = head.Origin;
                attachment.Sprite.Origin = origin;
            }
            else
            {
                origin = attachment.Sprite.Origin;
            }
            float depth = attachment.Sprite.Depth;
            if (attachment.InheritLimbDepth)
            {
                depth = head.Depth - depthStep;
            }
            attachment.Sprite.Draw(spriteBatch, drawPos, Color.White, origin, rotate: 0, scale: scale, depth: depth, spriteEffect: spriteEffects);
        }


        public static CharacterInfo ClientRead(string speciesName, IReadMessage inc)
        {
            ushort infoID = inc.ReadUInt16();
            string newName = inc.ReadString();
            int gender = inc.ReadByte();
            int race = inc.ReadByte();
            int headSpriteID = inc.ReadByte();
            int hairIndex = inc.ReadByte();
            int beardIndex = inc.ReadByte();
            int moustacheIndex = inc.ReadByte();
            int faceAttachmentIndex = inc.ReadByte();
            string ragdollFile = inc.ReadString();

            string jobIdentifier = inc.ReadString();
            int variant = inc.ReadByte();

            JobPrefab jobPrefab = null;
            Dictionary<string, float> skillLevels = new Dictionary<string, float>();
            if (!string.IsNullOrEmpty(jobIdentifier))
            {
                jobPrefab = JobPrefab.Get(jobIdentifier);
                byte skillCount = inc.ReadByte();
                for (int i = 0; i < skillCount; i++)
                {
                    string skillIdentifier = inc.ReadString();
                    float skillLevel = inc.ReadSingle();
                    skillLevels.Add(skillIdentifier, skillLevel);
                }
            }

            // TODO: animations
            CharacterInfo ch = new CharacterInfo(speciesName, newName, jobPrefab, ragdollFile, variant)
            {
                ID = infoID,
            };
            ch.RecreateHead(headSpriteID,(Race)race, (Gender)gender, hairIndex, beardIndex, moustacheIndex, faceAttachmentIndex);
            if (ch.Job != null)
            {
                foreach (KeyValuePair<string, float> skill in skillLevels)
                {
                    Skill matchingSkill = ch.Job.Skills.Find(s => s.Identifier == skill.Key);
                    if (matchingSkill == null)
                    {
                        ch.Job.Skills.Add(new Skill(skill.Key, skill.Value));
                        continue;
                    }
                    matchingSkill.Level = skill.Value;
                }
                ch.Job.Skills.RemoveAll(s => !skillLevels.ContainsKey(s.Identifier));
            }
            return ch;
        }
    }
}
