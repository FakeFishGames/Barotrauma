using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Extensions;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        public GUIFrame CreateInfoFrame(GUIFrame frame)
        {
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), frame.RectTransform, Anchor.Center), null);

            new GUICustomComponent(new RectTransform(new Point(30, 30), paddedFrame.RectTransform), 
                onDraw: (sb, component) => DrawIcon(sb, component.Rect.Center.ToVector2(), targetWidth: component.Rect.Width));

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
            new GUICustomComponent(new RectTransform(new Point(frame.Rect.Height, frame.Rect.Height), frame.RectTransform, Anchor.CenterLeft) { IsFixedSize = false }, 
                onDraw: (sb, component) => DrawIcon(sb, component.Rect.Center.ToVector2(), targetWidth: component.Rect.Width));
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

        partial void LoadAttachmentSprites()
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
            // TODO load class specific wearables
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
                if (AttachmentsSprites != null)
                {
                    float depthStep = 0.000001f;
                    foreach (var attachment in AttachmentsSprites)
                    {
                        DrawAttachmentSprite(spriteBatch, attachment, Portrait, screenPos + offset, scale, depthStep, SpriteEffects.FlipHorizontally);
                        depthStep += 0.000001f;
                    }
                }
            }
        }

        public void DrawIcon(SpriteBatch spriteBatch, Vector2 screenPos, float targetWidth)
        {
            if (HeadSprite != null)
            {
                float scale = targetWidth / HeadSprite.size.X;
                HeadSprite.Draw(spriteBatch, screenPos, scale: scale);
                if (AttachmentsSprites != null)
                {
                    float depthStep = 0.000001f;
                    foreach (var attachment in AttachmentsSprites)
                    {
                        DrawAttachmentSprite(spriteBatch, attachment, HeadSprite, screenPos, scale, depthStep);
                        depthStep += 0.000001f;
                    }
                }
            }
        }

        private void DrawAttachmentSprite(SpriteBatch spriteBatch, WearableSprite attachment, Sprite head, Vector2 drawPos, float scale, float depthStep, SpriteEffects spriteEffects = SpriteEffects.None)
        {
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
    }
}
