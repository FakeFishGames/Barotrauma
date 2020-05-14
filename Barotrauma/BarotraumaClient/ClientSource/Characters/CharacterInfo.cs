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
        private static Sprite infoAreaPortraitBG;

        public static void Init()
        {
            infoAreaPortraitBG = GUI.Style.GetComponentStyle("InfoAreaPortraitBG")?.Sprites[GUIComponent.ComponentState.None][0].Sprite;
            new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(833, 298, 142, 98), null, 0);
        }


        public GUIComponent CreateInfoFrame(GUIFrame frame, bool returnParent, Sprite permissionIcon = null)
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.874f, 0.58f), frame.RectTransform, Anchor.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.05f) })
            {
                RelativeSpacing = 0.05f
               //Stretch = true
            };

            var headerArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.322f), paddedFrame.RectTransform), isHorizontal: true);

            new GUICustomComponent(new RectTransform(new Vector2(0.425f, 1.0f), headerArea.RectTransform), 
                onDraw: (sb, component) => DrawInfoFrameCharacterIcon(sb, component.Rect));

            ScalableFont font = paddedFrame.Rect.Width < 280 ? GUI.SmallFont : GUI.Font;

            var headerTextArea = new GUILayoutGroup(new RectTransform(new Vector2(0.575f, 1.0f), headerArea.RectTransform))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            Color? nameColor = null;
            if (Job != null) { nameColor = Job.Prefab.UIColor; }

            GUITextBlock characterNameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform), ToolBox.LimitString(Name, GUI.Font, headerTextArea.Rect.Width), textColor: nameColor, font: GUI.Font)
            {
                ForceUpperCase = true,
                Padding = Vector4.Zero
            };

            if (permissionIcon != null)
            {
                Point iconSize = permissionIcon.SourceRect.Size;
                int iconWidth = (int)((float)characterNameBlock.Rect.Height / iconSize.Y * iconSize.X);
                new GUIImage(new RectTransform(new Point(iconWidth, characterNameBlock.Rect.Height), characterNameBlock.RectTransform) { AbsoluteOffset = new Point(-iconWidth - 2, 0) }, permissionIcon) { IgnoreLayoutGroups = true };
            }

            if (Job != null)
            {   
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform), Job.Name, textColor: Job.Prefab.UIColor, font: font)
                {
                    Padding = Vector4.Zero
                };
            }

            if (personalityTrait != null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform), TextManager.AddPunctuation(':', TextManager.Get("PersonalityTrait"), TextManager.Get("personalitytrait." + personalityTrait.Name.Replace(" ", ""))), font: font)
                {
                    Padding = Vector4.Zero
                };
            }

            if (Job != null && (Character == null || !Character.IsDead))
            {
                var skillsArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.63f), paddedFrame.RectTransform, Anchor.BottomCenter, Pivot.BottomCenter))
                {
                    Stretch = true
                };

                var skills = Job.Skills;
                skills.Sort((s1, s2) => -s1.Level.CompareTo(s2.Level));

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillsArea.RectTransform), TextManager.AddPunctuation(':', TextManager.Get("skills"), string.Empty), font: font) { Padding = Vector4.Zero };
                
                foreach (Skill skill in skills)
                {
                    Color textColor = Color.White * (0.5f + skill.Level / 200.0f);

                    var skillName = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillsArea.RectTransform), TextManager.Get("SkillName." + skill.Identifier), textColor: textColor, font: font) { Padding = Vector4.Zero };
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), skillName.RectTransform), ((int)skill.Level).ToString(), textColor: textColor, font: font, textAlignment: Alignment.CenterRight);
                }
            }
            else if (Character != null && Character.IsDead)
            {
                var deadArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.63f), paddedFrame.RectTransform, Anchor.BottomCenter, Pivot.BottomCenter))
                {
                    Stretch = true
                };

                string deadDescription = TextManager.AddPunctuation(':', TextManager.Get("deceased") + "\n" + Character.CauseOfDeath.Affliction?.CauseOfDeathDescription ?? 
                    TextManager.AddPunctuation(':', TextManager.Get("CauseOfDeath"), TextManager.Get("CauseOfDeath." + Character.CauseOfDeath.Type.ToString())));

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), deadArea.RectTransform), deadDescription, textColor: GUI.Style.Red, font: font, textAlignment: Alignment.TopLeft) { Padding = Vector4.Zero };
            }

            if (returnParent)
            {
                return frame;
            }
            else
            {
                return paddedFrame;
            }
        }

        private void DrawInfoFrameCharacterIcon(SpriteBatch sb, Rectangle componentRect)
        {
            Vector2 targetAreaSize = componentRect.Size.ToVector2();
            float scale = Math.Min(targetAreaSize.X / headSprite.size.X, targetAreaSize.Y / headSprite.size.Y);
            DrawIcon(sb, componentRect.Location.ToVector2() + headSprite.size / 2 * scale, targetAreaSize);
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
                    GUI.Style.Green,
                    textPopupPos,
                    Vector2.UnitY * 10.0f);
            }
            else if (prevLevel % 0.1f > 0.05f && newLevel % 0.1f < 0.05f)
            {
                GUI.AddMessage(
                    "+10 XP",
                    GUI.Style.Green,
                    textPopupPos,
                    Vector2.UnitY * 10.0f);
            }

            if ((int)newLevel > (int)prevLevel)
            {
                GUI.AddMessage(
                    TextManager.GetWithVariables("SkillIncreased", new string[3] { "[name]", "[skillname]", "[newlevel]" },
                    new string[3] { Name, TextManager.Get("SkillName." + skillIdentifier), ((int)newLevel).ToString() },
                    new bool[3] { false, true, false }), GUI.Style.Green);
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

        // Doesn't work if the head's source rect does not start at 0,0.
        public static Point CalculateOffset(Sprite sprite, Point offset) => sprite.SourceRect.Size * offset;

        public void CalculateHeadPosition(Sprite sprite)
        {
            if (sprite == null) { return; }
            if (Head.SheetIndex == null) { return; }
            Point location = CalculateOffset(sprite, Head.SheetIndex.Value.ToPoint());
            sprite.SourceRect = new Rectangle(location, sprite.SourceRect.Size);
        }

        public void DrawBackground(SpriteBatch spriteBatch)
        {
            if (infoAreaPortraitBG == null) { return; }
            infoAreaPortraitBG.Draw(spriteBatch, HUDLayoutSettings.BottomRightInfoArea.Location.ToVector2(), Color.White, Vector2.Zero, 0.0f,
                scale: new Vector2(
                    HUDLayoutSettings.BottomRightInfoArea.Width / (float)infoAreaPortraitBG.SourceRect.Width,
                    HUDLayoutSettings.BottomRightInfoArea.Height / (float)infoAreaPortraitBG.SourceRect.Height));
        }

        public void DrawPortrait(SpriteBatch spriteBatch, Vector2 screenPos, Vector2 offset, float targetWidth, bool flip = false)
        {
            if (Portrait != null)
            {
                // Scale down the head sprite 10%
                float scale = targetWidth * 0.9f / Portrait.size.X;
                if (Head.SheetIndex.HasValue)
                {
                    Portrait.SourceRect = new Rectangle(CalculateOffset(Portrait, Head.SheetIndex.Value.ToPoint()), Portrait.SourceRect.Size);
                }
                Portrait.Draw(spriteBatch, screenPos + offset, Color.White, Portrait.Origin, scale: scale, spriteEffect: flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
                if (AttachmentSprites != null)
                {
                    float depthStep = 0.000001f;
                    foreach (var attachment in AttachmentSprites)
                    {
                        DrawAttachmentSprite(spriteBatch, attachment, Portrait, screenPos + offset, scale, depthStep, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
                        depthStep += depthStep;
                    }
                }
            }
        }
        
        public void DrawIcon(SpriteBatch spriteBatch, Vector2 screenPos, Vector2 targetAreaSize)
        {
            var headSprite = HeadSprite;
            if (headSprite != null)
            {
                float scale = Math.Min(targetAreaSize.X / headSprite.size.X, targetAreaSize.Y / headSprite.size.Y);
                if (Head.SheetIndex.HasValue)
                {
                    headSprite.SourceRect = new Rectangle(CalculateOffset(headSprite, Head.SheetIndex.Value.ToPoint()), headSprite.SourceRect.Size);
                }
                headSprite.Draw(spriteBatch, screenPos, scale: scale);
                if (AttachmentSprites != null)
                {
                    float depthStep = 0.000001f;
                    foreach (var attachment in AttachmentSprites)
                    {
                        DrawAttachmentSprite(spriteBatch, attachment, headSprite, screenPos, scale, depthStep);
                        depthStep += depthStep;
                    }
                }
            }
        }

        public void DrawJobIcon(SpriteBatch spriteBatch, Vector2 pos, float scale = 1.0f)
        {
            var icon = Job?.Prefab?.Icon;
            if (icon == null) { return; }
            icon.Draw(spriteBatch, pos, Job.Prefab.UIColor, scale: scale);
        }
        public void DrawJobIcon(SpriteBatch spriteBatch, Rectangle area)
        {
            var icon = Job?.Prefab?.Icon;
            if (icon == null) { return; }
            icon.Draw(spriteBatch,
                area.Center.ToVector2(),
                Job.Prefab.UIColor,
                scale: Math.Min(area.Width / (float)icon.SourceRect.Width, area.Height / (float)icon.SourceRect.Height));
        }

        private void DrawAttachmentSprite(SpriteBatch spriteBatch, WearableSprite attachment, Sprite head, Vector2 drawPos, float scale, float depthStep, SpriteEffects spriteEffects = SpriteEffects.None)
        {
            if (attachment.InheritSourceRect)
            {
                if (attachment.SheetIndex.HasValue)
                {
                    attachment.Sprite.SourceRect = new Rectangle(CalculateOffset(head, attachment.SheetIndex.Value), head.SourceRect.Size);
                }
                else if (Head.SheetIndex.HasValue)
                {
                    attachment.Sprite.SourceRect = new Rectangle(CalculateOffset(head, Head.SheetIndex.Value.ToPoint()), head.SourceRect.Size);
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
