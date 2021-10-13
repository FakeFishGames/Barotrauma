using Barotrauma.Extensions;
using Barotrauma.Networking;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;
using Barotrauma.IO;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        private static Sprite infoAreaPortraitBG;

        public bool LastControlled;

        #warning TODO: Refactor
        private Sprite disguisedPortrait;
        private List<WearableSprite> disguisedAttachmentSprites;
        private Vector2? disguisedSheetIndex;
        private Sprite disguisedJobIcon;
        private Color disguisedJobColor;
        private Color disguisedHairColor;
        private Color disguisedFacialHairColor;
        private Color disguisedSkinColor;

        private Sprite tintMask;
        private float tintHighlightThreshold;
        private float tintHighlightMultiplier;

        public static void Init()
        {
            infoAreaPortraitBG = GUI.Style.GetComponentStyle("InfoAreaPortraitBG")?.GetDefaultSprite();
            new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(833, 298, 142, 98), null, 0);
        }

        partial void LoadHeadSpriteProjectSpecific(XElement limbElement)
        {
            XElement maskElement = limbElement.Element("tintmask");
            if (maskElement != null)
            {
                string tintMaskPath = maskElement.GetAttributeString("texture", "");
                if (!string.IsNullOrWhiteSpace(tintMaskPath))
                {
                    tintMask = new Sprite(maskElement, file: Limb.GetSpritePath(tintMaskPath, this));
                    tintHighlightThreshold = maskElement.GetAttributeFloat("highlightthreshold", 0.6f);
                    tintHighlightMultiplier = maskElement.GetAttributeFloat("highlightmultiplier", 0.8f);
                }
            }
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

                    float modifiedSkillLevel = skill.Level;
                    if (Character != null)
                    {
                        modifiedSkillLevel = Character.GetSkillLevel(skill.Identifier);
                    }
                    if (!MathUtils.NearlyEqual(MathF.Round(modifiedSkillLevel), MathF.Round(skill.Level)))
                    {
                        int skillChange = (int)MathF.Round(modifiedSkillLevel - skill.Level);
                        string changeText = $"{(skillChange > 0 ? "+" : "") + skillChange}";
                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), skillName.RectTransform), $"{(int)skill.Level} ({changeText})", textColor: textColor, font: font, textAlignment: Alignment.CenterRight);
                    }
                    else
                    {
                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), skillName.RectTransform), ((int)skill.Level).ToString(), textColor: textColor, font: font, textAlignment: Alignment.CenterRight);
                    }
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
            if (_headSprite == null) { return; }
            Vector2 targetAreaSize = componentRect.Size.ToVector2();
            float scale = Math.Min(targetAreaSize.X / _headSprite.size.X, targetAreaSize.Y / _headSprite.size.Y);
            DrawIcon(sb, componentRect.Location.ToVector2() + _headSprite.size / 2 * scale, targetAreaSize);
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

        partial void OnSkillChanged(string skillIdentifier, float prevLevel, float newLevel)
        {
            if (TeamID == CharacterTeamType.FriendlyNPC) { return; }
            if (Character.Controlled != null && Character.Controlled.TeamID != TeamID) { return; }

            // if we increased by more than 1 in one increase, then display special color (for talents)
            bool specialIncrease = Math.Abs(newLevel - prevLevel) >= 1.0f;

            if ((int)newLevel > (int)prevLevel)
            {
                int increase = Math.Max((int)newLevel - (int)prevLevel, 1);
                Character?.AddMessage(
                    "+[value] "+ TextManager.Get("SkillName." + skillIdentifier), 
                    specialIncrease ? GUI.Style.Orange : GUI.Style.Green, 
                    playSound: Character == Character.Controlled, skillIdentifier, increase);
            }
        }

        partial void OnExperienceChanged(int prevAmount, int newAmount)
        {
            if (Character.Controlled != null && Character.Controlled.TeamID != TeamID) { return; }

            GameSession.TabMenuInstance?.OnExperienceChanged(Character);

            if (newAmount > prevAmount)
            {
                int increase = newAmount - prevAmount;
                Character?.AddMessage(
                    "+[value] " + TextManager.Get("experienceshort"),
                    GUI.Style.Blue, playSound: Character == Character.Controlled, "exp", increase);
            }
        }

        private void GetDisguisedSprites(IdCard idCard)
        {
            if (idCard.Item.Tags == string.Empty) return;

            if (idCard.StoredOwnerAppearance.JobPrefab == null || idCard.StoredOwnerAppearance.Portrait == null)
            {
                string[] readTags = idCard.Item.Tags.Split(',');

                if (readTags.Length == 0) { return; }

                if (idCard.StoredOwnerAppearance.JobPrefab == null)
                {
                    idCard.StoredOwnerAppearance.ExtractJobPrefab(readTags);
                }

                if (idCard.StoredOwnerAppearance.Portrait == null)
                {
                    idCard.StoredOwnerAppearance.ExtractAppearance(this, readTags);
                }
            }

            if (idCard.StoredOwnerAppearance.JobPrefab != null)
            {
                disguisedJobIcon = idCard.StoredOwnerAppearance.JobPrefab.Icon;
                disguisedJobColor = idCard.StoredOwnerAppearance.JobPrefab.UIColor;
            }

            disguisedPortrait = idCard.StoredOwnerAppearance.Portrait;
            disguisedSheetIndex = idCard.StoredOwnerAppearance.SheetIndex;
            disguisedAttachmentSprites = idCard.StoredOwnerAppearance.Attachments;

            disguisedHairColor = idCard.StoredOwnerAppearance.HairColor;
            disguisedFacialHairColor = idCard.StoredOwnerAppearance.FacialHairColor;
            disguisedSkinColor = idCard.StoredOwnerAppearance.SkinColor;
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

        public void DrawPortrait(SpriteBatch spriteBatch, Vector2 screenPos, Vector2 offset, float targetWidth, bool flip = false, bool evaluateDisguise = false)
        {
            if (evaluateDisguise && IsDisguised) { return; }

            Vector2? sheetIndex;
            Sprite portraitToDraw;
            List<WearableSprite> attachmentsToDraw;

            Color hairColor;
            Color facialHairColor;
            Color skinColor;

            if (!IsDisguisedAsAnother || !evaluateDisguise)
            {
                sheetIndex = Head.SheetIndex;
                portraitToDraw = Portrait;
                attachmentsToDraw = AttachmentSprites;

                hairColor = Head.HairColor;
                facialHairColor = Head.FacialHairColor;
                skinColor = Head.SkinColor;
            }
            else
            {
                sheetIndex = disguisedSheetIndex;
                portraitToDraw = disguisedPortrait;
                attachmentsToDraw = disguisedAttachmentSprites;
                
                hairColor = disguisedHairColor;
                facialHairColor = disguisedFacialHairColor;
                skinColor = disguisedSkinColor;
            }

            if (portraitToDraw != null)
            {
                var currEffect = spriteBatch.GetCurrentEffect();
                // Scale down the head sprite 10%
                float scale = targetWidth * 0.9f / Portrait.size.X;
                if (sheetIndex.HasValue)
                {
                    SetHeadEffect(spriteBatch);
                    portraitToDraw.SourceRect = new Rectangle(CalculateOffset(portraitToDraw, sheetIndex.Value.ToPoint()), portraitToDraw.SourceRect.Size);
                }
                portraitToDraw.Draw(spriteBatch, screenPos + offset, skinColor, portraitToDraw.Origin, scale: scale, spriteEffect: flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
                if (attachmentsToDraw != null)
                {
                    float depthStep = 0.000001f;
                    foreach (var attachment in attachmentsToDraw)
                    {
                        SetAttachmentEffect(spriteBatch, attachment);
                        DrawAttachmentSprite(spriteBatch, attachment, portraitToDraw, sheetIndex, screenPos + offset, scale, depthStep, GetAttachmentColor(attachment, hairColor, facialHairColor), flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
                        depthStep += depthStep;
                    }
                }
                spriteBatch.SwapEffect(currEffect);
            }
        }

        //TODO: I hate this so much :(
        private SpriteBatch.EffectWithParams headEffectParameters;
        private Dictionary<WearableType, SpriteBatch.EffectWithParams> attachmentEffectParameters
            = new Dictionary<WearableType, SpriteBatch.EffectWithParams>();

        private void SetHeadEffect(SpriteBatch spriteBatch)
        {
            headEffectParameters.Effect ??= GameMain.GameScreen.ThresholdTintEffect;
            headEffectParameters.Params ??= new Dictionary<string, object>();
            headEffectParameters.Params["xBaseTexture"] = HeadSprite.Texture;
            headEffectParameters.Params["xTintMaskTexture"] = tintMask?.Texture ?? GUI.WhiteTexture;
            headEffectParameters.Params["xCutoffTexture"] = GUI.WhiteTexture;
            headEffectParameters.Params["baseToCutoffSizeRatio"] = 1.0f;
            headEffectParameters.Params["highlightThreshold"] = tintHighlightThreshold;
            headEffectParameters.Params["highlightMultiplier"] = tintHighlightMultiplier;
            spriteBatch.SwapEffect(headEffectParameters);
        }

        private void SetAttachmentEffect(SpriteBatch spriteBatch, WearableSprite attachment)
        {
            if (!attachmentEffectParameters.ContainsKey(attachment.Type))
            {
                attachmentEffectParameters.Add(attachment.Type, new SpriteBatch.EffectWithParams(GameMain.GameScreen.ThresholdTintEffect, new Dictionary<string, object>()));
            }
            var parameters = attachmentEffectParameters[attachment.Type].Params;
            parameters["xBaseTexture"] = attachment.Sprite.Texture;
            parameters["xTintMaskTexture"] = GUI.WhiteTexture;
            parameters["xCutoffTexture"] = GUI.WhiteTexture;
            parameters["baseToCutoffSizeRatio"] = 1.0f;
            parameters["highlightThreshold"] = tintHighlightThreshold;
            parameters["highlightMultiplier"] = tintHighlightMultiplier;
            spriteBatch.SwapEffect(attachmentEffectParameters[attachment.Type]);
        }

        private Color GetAttachmentColor(WearableSprite attachment, Color hairColor, Color facialHairColor)
        {
            switch (attachment.Type)
            {
                case WearableType.Hair:
                    return hairColor;
                case WearableType.Beard:
                case WearableType.Moustache:
                    return facialHairColor;
                default:
                    return Color.White;
            }
        }
        
        public void DrawIcon(SpriteBatch spriteBatch, Vector2 screenPos, Vector2 targetAreaSize)
        {
            var headSprite = HeadSprite;
            if (headSprite != null)
            {
                var currEffect = spriteBatch.GetCurrentEffect();
                float scale = Math.Min(targetAreaSize.X / headSprite.size.X, targetAreaSize.Y / headSprite.size.Y);
                if (Head.SheetIndex.HasValue)
                {
                    headSprite.SourceRect = new Rectangle(CalculateOffset(headSprite, Head.SheetIndex.Value.ToPoint()), headSprite.SourceRect.Size);
                }
                SetHeadEffect(spriteBatch);
                headSprite.Draw(spriteBatch, screenPos, scale: scale, color: SkinColor);
                if (AttachmentSprites != null)
                {
                    float depthStep = 0.000001f;
                    foreach (var attachment in AttachmentSprites)
                    {
                        SetAttachmentEffect(spriteBatch, attachment);
                        DrawAttachmentSprite(spriteBatch, attachment, headSprite, Head.SheetIndex, screenPos, scale, depthStep, GetAttachmentColor(attachment, HairColor, FacialHairColor));
                        depthStep += depthStep;
                    }
                }
                spriteBatch.SwapEffect(currEffect);
            }
        }

        public void DrawJobIcon(SpriteBatch spriteBatch, Rectangle area, bool evaluateDisguise = false)
        {
            if (evaluateDisguise && IsDisguised) return;
            var icon = !IsDisguisedAsAnother || !evaluateDisguise ? Job?.Prefab?.Icon : disguisedJobIcon;
            if (icon == null) { return; }
            Color iconColor = !IsDisguisedAsAnother || !evaluateDisguise ? Job.Prefab.UIColor : disguisedJobColor;

            icon.Draw(spriteBatch, area.Center.ToVector2(), iconColor, scale: Math.Min(area.Width / (float)icon.SourceRect.Width, area.Height / (float)icon.SourceRect.Height));
        }

        private void DrawAttachmentSprite(SpriteBatch spriteBatch, WearableSprite attachment, Sprite head, Vector2? sheetIndex, Vector2 drawPos, float scale, float depthStep, Color? color = null, SpriteEffects spriteEffects = SpriteEffects.None)
        {
            if (attachment.InheritSourceRect)
            {
                if (attachment.SheetIndex.HasValue)
                {
                    attachment.Sprite.SourceRect = new Rectangle(CalculateOffset(head, attachment.SheetIndex.Value), head.SourceRect.Size);
                }
                else if (sheetIndex.HasValue)
                {
                    attachment.Sprite.SourceRect = new Rectangle(CalculateOffset(head, sheetIndex.Value.ToPoint()), head.SourceRect.Size);
                }
                else
                {
                    attachment.Sprite.SourceRect = head.SourceRect;
                }
            }
            Vector2 origin;
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
            attachment.Sprite.Draw(spriteBatch, drawPos, color ?? Color.White, origin, rotate: 0, scale: scale, depth: depth, spriteEffect: spriteEffects);
        }

        public static CharacterInfo ClientRead(string speciesName, IReadMessage inc)
        {
            ushort infoID = inc.ReadUInt16();
            string newName = inc.ReadString();
            string originalName = inc.ReadString();
            int gender = inc.ReadByte();
            int race = inc.ReadByte();
            int headSpriteID = inc.ReadByte();
            int hairIndex = inc.ReadByte();
            int beardIndex = inc.ReadByte();
            int moustacheIndex = inc.ReadByte();
            int faceAttachmentIndex = inc.ReadByte();
            Color skinColor = inc.ReadColorR8G8B8();
            Color hairColor = inc.ReadColorR8G8B8();
            Color facialHairColor = inc.ReadColorR8G8B8();
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
            CharacterInfo ch = new CharacterInfo(speciesName, newName, originalName, jobPrefab, ragdollFile, variant)
            {
                ID = infoID,
            };
            ch.RecreateHead(headSpriteID,(Race)race, (Gender)gender, hairIndex, beardIndex, moustacheIndex, faceAttachmentIndex);
            ch.SkinColor = skinColor;
            ch.HairColor = hairColor;
            ch.FacialHairColor = facialHairColor;
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

            byte savedStatValueCount = inc.ReadByte();
            for (int i = 0; i < savedStatValueCount; i++)
            {
                int statType = inc.ReadByte();
                string statIdentifier = inc.ReadString();
                float statValue = inc.ReadSingle();
                bool removeOnDeath = inc.ReadBoolean();
                ch.ChangeSavedStatValue((StatTypes)statType, statValue, statIdentifier, removeOnDeath);
            }
            ch.ExperiencePoints = inc.ReadUInt16();
            ch.AdditionalTalentPoints = inc.ReadUInt16();
            return ch;
        }

        public void CreateIcon(RectTransform rectT)
        {
            LoadHeadAttachments();
            new GUICustomComponent(rectT,
                onDraw: (sb, component) => DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()));
        }

        public class AppearanceCustomizationMenu : IDisposable
        {
            public readonly CharacterInfo CharacterInfo;
            public GUIListBox HeadSelectionList = null;
            public bool HasIcon = true;

            public GUIScrollBar.OnMovedHandler OnSliderMoved = null;
            public GUIScrollBar.OnMovedHandler OnSliderReleased = null;
            public Action<AppearanceCustomizationMenu> OnHeadSwitch = null;

            private readonly GUIComponent parentComponent;
            private readonly List<Sprite> characterSprites = new List<Sprite>();
            public GUIButton RandomizeButton;

            public AppearanceCustomizationMenu(CharacterInfo info, GUIComponent parent, bool hasIcon = true)
            {
                CharacterInfo = info;
                parentComponent = parent;
                HasIcon = hasIcon;

                RecreateFrameContents();
            }

            public void RecreateFrameContents()
            {
                var info = CharacterInfo;

                HeadSelectionList = null;
                parentComponent.ClearChildren();
                ClearSprites();

                float contentWidth = HasIcon ? 0.75f : 1.0f;
                var listBox = new GUIListBox(
                        new RectTransform(new Vector2(contentWidth, 1.0f), parentComponent.RectTransform,
                            Anchor.CenterLeft))
                    { CanBeFocused = false, CanTakeKeyBoardFocus = false };
                var content = listBox.Content;
                
                info.LoadHeadAttachments();
                if (HasIcon)
                {
                    info.CreateIcon(
                        new RectTransform(new Vector2(0.25f, 1.0f), parentComponent.RectTransform, Anchor.CenterRight)
                            { RelativeOffset = new Vector2(-0.01f, 0.0f) });
                }

                RectTransform createItemRectTransform(string labelTag, float width = 0.6f)
                {
                    var layoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.166f), content.RectTransform));
                    
                    var label = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), layoutGroup.RectTransform),
                        TextManager.Get(labelTag), font: GUI.SubHeadingFont);

                    var bottomItem = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), layoutGroup.RectTransform),
                        style: null);
                    
                    return new RectTransform(new Vector2(width, 1.0f), bottomItem.RectTransform, Anchor.Center);
                }

                RectTransform genderItemRT = createItemRectTransform("Gender", 1.0f);
                
                GUILayoutGroup genderContainer =
                    new GUILayoutGroup(genderItemRT, isHorizontal: true)
                    {
                        Stretch = true,
                        RelativeSpacing = 0.05f
                    };

                void createGenderButton(Gender gender)
                {
                    new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), genderContainer.RectTransform),
                        TextManager.Get(gender.ToString()), style: "ListBoxElement")
                    {
                        UserData = gender,
                        OnClicked = OpenHeadSelection,
                        Selected = info.Gender == gender
                    };
                }

                createGenderButton(Gender.Male);
                createGenderButton(Gender.Female);

                int countAttachmentsOfType(WearableType wearableType)
                    => info.FilterByTypeAndHeadID(
                        info.FilterElementsByGenderAndRace(info.Wearables, info.Head.gender, info.Head.race),
                        wearableType, info.HeadSpriteId).Count();

                List<GUIScrollBar> attachmentSliders = new List<GUIScrollBar>();
                void createAttachmentSlider(int initialValue, WearableType wearableType)
                {
                    int attachmentCount = countAttachmentsOfType(wearableType);
                    if (attachmentCount > 0)
                    {
                        var labelTag = wearableType == WearableType.FaceAttachment
                            ? "FaceAttachment.Accessories"
                            : $"FaceAttachment.{wearableType}";
                        var sliderItemRT = createItemRectTransform(labelTag);
                        var slider =
                            new GUIScrollBar(sliderItemRT, style: "GUISlider")
                            {
                                Range = new Vector2(0, attachmentCount),
                                StepValue = 1,
                                OnMoved = (bar, scroll) => SwitchAttachment(bar, wearableType),
                                OnReleased = OnSliderReleased,
                                BarSize = 1.0f / (float)(attachmentCount + 1)
                            };
                        slider.BarScrollValue = initialValue;
                        attachmentSliders.Add(slider);
                    }
                }

                createAttachmentSlider(info.HairIndex, WearableType.Hair);
                createAttachmentSlider(info.BeardIndex, WearableType.Beard);
                createAttachmentSlider(info.MoustacheIndex, WearableType.Moustache);
                createAttachmentSlider(info.FaceAttachmentIndex, WearableType.FaceAttachment);

                void createColorSelector(string labelTag, IEnumerable<(Color Color, float Commonness)> options, Func<Color> getter,
                    Action<Color> setter)
                {
                    var selectorItemRT = createItemRectTransform(labelTag, 0.4f);
                    var dropdown =
                        new GUIDropDown(selectorItemRT)
                            { AllowNonText = true };

                    var listBoxSize = dropdown.ListBox.RectTransform.RelativeSize;
                    dropdown.ListBox.RectTransform.RelativeSize = new Vector2(listBoxSize.X * 1.75f, listBoxSize.Y);
                    var dropdownButton = dropdown.GetChild<GUIButton>();
                    var buttonFrame =
                        new GUIFrame(
                            new RectTransform(Vector2.One * 0.7f, dropdownButton.RectTransform, Anchor.CenterLeft)
                                { RelativeOffset = new Vector2(0.05f, 0.0f) }, style: null);
                    dropdown.OnSelected = (component, color) =>
                    {
                        setter((Color)color);
                        buttonFrame.Color = getter();
                        buttonFrame.HoverColor = getter();
                        return true;
                    };
                    buttonFrame.Color = getter();
                    buttonFrame.HoverColor = getter();

                    dropdown.ListBox.UseGridLayout = true;
                    foreach (var option in options)
                    {
                        var optionElement =
                            new GUIFrame(
                                new RectTransform(new Vector2(0.25f, 1.0f / 3.0f),
                                    dropdown.ListBox.Content.RectTransform),
                                style: "ListBoxElement")
                            {
                                UserData = option.Color,
                                CanBeFocused = true
                            };
                        var colorElement =
                            new GUIFrame(
                                new RectTransform(Vector2.One * 0.75f, optionElement.RectTransform, Anchor.Center,
                                    scaleBasis: ScaleBasis.Smallest),
                                style: null)
                            {
                                Color = option.Color,
                                HoverColor = option.Color,
                                OutlineColor = Color.Lerp(Color.Black, option.Color, 0.5f),
                                CanBeFocused = false
                            };
                    }

                    var childToSelect = dropdown.ListBox.Content.FindChild(c => (Color)c.UserData == getter());
                    dropdown.Select(dropdown.ListBox.Content.GetChildIndex(childToSelect));

                    //The following exists to track mouseover to preview colors before selecting them
                    bool previewingColor = false;
                    new GUICustomComponent(new RectTransform(Vector2.One, buttonFrame.RectTransform),
                        onUpdate: (deltaTime, component) =>
                        {
                            if (GUI.MouseOn is GUIFrame { Parent: { } p } hoveredFrame && dropdown.ListBox.Content.IsParentOf(hoveredFrame))
                            {
                                previewingColor = true;
                                Color color = (Color)(dropdown.ListBox.Content.FindChild(c =>
                                    c == hoveredFrame || c.IsParentOf(hoveredFrame))?.UserData ?? dropdown.SelectedData);
                                setter(color);
                                buttonFrame.Color = getter();
                                buttonFrame.HoverColor = getter();
                            }
                            else if (previewingColor)
                            {
                                setter((Color)dropdown.SelectedData);
                                buttonFrame.Color = getter();
                                buttonFrame.HoverColor = getter();
                                previewingColor = false;
                            }
                        }, onDraw: null)
                    {
                        CanBeFocused = false,
                        Visible = true
                    };
                }

                if (countAttachmentsOfType(WearableType.Hair) > 0)
                {
                    createColorSelector($"Customization.{nameof(info.HairColor)}", info.HairColors,
                        () => info.HairColor, (color) => info.HairColor = color);
                }

                if (countAttachmentsOfType(WearableType.Moustache) > 0 ||
                    countAttachmentsOfType(WearableType.Beard) > 0)
                {
                    createColorSelector($"Customization.{nameof(info.FacialHairColor)}", info.FacialHairColors,
                        () => info.FacialHairColor, (color) => info.FacialHairColor = color);
                }

                createColorSelector($"Customization.{nameof(info.SkinColor)}", info.SkinColors, () => info.SkinColor,
                    (color) => info.SkinColor = color);

                RandomizeButton = new GUIButton(new RectTransform(Vector2.One * 0.12f,
                        parentComponent.RectTransform,
                        anchor: Anchor.BottomRight, scaleBasis: ScaleBasis.Smallest)
                    { RelativeOffset = new Vector2(0.01f, 0.005f) }, style: "RandomizeButton")
                {
                    OnClicked = (button, o) =>
                    {
                        info.Head = new HeadInfo();
                        info.SetGenderAndRace(Rand.RandSync.Unsynced);
                        info.SetColors();
                        
                        RecreateFrameContents();
                        info.RefreshHead();
                        OnHeadSwitch?.Invoke(this);
                        attachmentSliders.ForEach(s => OnSliderMoved?.Invoke(s, s.BarScroll));

                        return false;
                    }
                };
                //force update twice because the listbox is insanely janky
                //TODO: fix all of the UI :)
                listBox.ForceUpdate();
                listBox.ForceUpdate();
                foreach (var childLayoutGroup in listBox.Content.GetAllChildren<GUILayoutGroup>())
                {
                    childLayoutGroup.Recalculate();
                }
            }

            private bool OpenHeadSelection(GUIButton button, object userData)
            {
                Gender selectedGender = (Gender)userData;

                var info = CharacterInfo;

                float characterHeightWidthRatio = info.HeadSprite.size.Y / info.HeadSprite.size.X;
                HeadSelectionList ??= new GUIListBox(
                    new RectTransform(
                        new Point(parentComponent.Rect.Width,
                            (int)(parentComponent.Rect.Width * characterHeightWidthRatio * 0.6f)), GUI.Canvas)
                    {
                        AbsoluteOffset = new Point(parentComponent.Rect.Right - parentComponent.Rect.Width,
                            button.Rect.Bottom)
                    });
                HeadSelectionList.Visible = true;
                HeadSelectionList.Content.ClearChildren();
                ClearSprites();

                parentComponent.RectTransform.SizeChanged += () =>
                {
                    if (parentComponent == null || HeadSelectionList?.RectTransform == null || button == null)
                    {
                        return;
                    }

                    HeadSelectionList.RectTransform.Resize(new Point(parentComponent.Rect.Width,
                        (int)(parentComponent.Rect.Width * characterHeightWidthRatio * 0.6f)));
                    HeadSelectionList.RectTransform.AbsoluteOffset =
                        new Point(parentComponent.Rect.Right - parentComponent.Rect.Width, button.Rect.Bottom);
                };

                new GUIFrame(
                    new RectTransform(new Vector2(1.25f, 1.25f), HeadSelectionList.RectTransform, Anchor.Center),
                    style: "OuterGlow", color: Color.Black)
                {
                    UserData = "outerglow",
                    CanBeFocused = false
                };

                GUILayoutGroup row = null;
                int itemsInRow = 0;

                XElement headElement = info.Ragdoll.MainElement.Elements().FirstOrDefault(e =>
                    e.GetAttributeString("type", "").Equals("head", StringComparison.OrdinalIgnoreCase));
                XElement headSpriteElement = headElement.Element("sprite");
                string spritePathWithTags = headSpriteElement.Attribute("texture").Value;

                var characterConfigElement = info.CharacterConfigElement;

                var heads = info.Heads;
                if (heads != null)
                {
                    row = null;
                    itemsInRow = 0;
                    foreach (var kvp in heads.Where(kv => kv.Key.Gender == selectedGender))
                    {
                        var headPreset = kvp.Key;
                        Race race = headPreset.Race;
                        int headIndex = headPreset.ID;

                        string spritePath = spritePathWithTags
                            .Replace("[GENDER]", selectedGender.ToString().ToLowerInvariant())
                            .Replace("[RACE]", race.ToString().ToLowerInvariant());

                        if (!File.Exists(spritePath))
                        {
                            continue;
                        }

                        Sprite headSprite = new Sprite(headSpriteElement, "", spritePath);
                        headSprite.SourceRect =
                            new Rectangle(CalculateOffset(headSprite, kvp.Value.ToPoint()),
                                headSprite.SourceRect.Size);
                        characterSprites.Add(headSprite);

                        if (itemsInRow >= 4 || row == null)
                        {
                            row = new GUILayoutGroup(
                                new RectTransform(new Vector2(1.0f, 0.333f), HeadSelectionList.Content.RectTransform),
                                true)
                            {
                                UserData = selectedGender,
                                Visible = true
                            };
                            itemsInRow = 0;
                        }

                        var btn = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), row.RectTransform),
                            style: "ListBoxElementSquare")
                        {
                            OutlineColor = Color.White * 0.5f,
                            PressedColor = Color.White * 0.5f,
                            UserData = new Tuple<Gender, Race, int>(selectedGender, race, headIndex),
                            OnClicked = SwitchHead,
                            Selected = selectedGender == info.Gender && race == info.Race && headIndex == info.HeadSpriteId,
                            Visible = true
                        };

                        new GUIImage(new RectTransform(Vector2.One, btn.RectTransform), headSprite, scaleToFit: true);
                        itemsInRow++;
                    }
                }

                return false;
            }

            private bool SwitchHead(GUIButton button, object obj)
            {
                var info = CharacterInfo;
                Gender gender = ((Tuple<Gender, Race, int>)obj).Item1;
                Race race = ((Tuple<Gender, Race, int>)obj).Item2;
                int id = ((Tuple<Gender, Race, int>)obj).Item3;
                info.Gender = gender;
                info.Race = race;
                info.Head.HeadSpriteId = id;
                RecreateFrameContents();
                OnHeadSwitch?.Invoke(this);
                return true;
            }

            private bool SwitchAttachment(GUIScrollBar scrollBar, WearableType type)
            {
                var info = CharacterInfo;
                int index = (int)scrollBar.BarScrollValue;
                switch (type)
                {
                    case WearableType.Beard:
                        info.BeardIndex = index;
                        break;
                    case WearableType.FaceAttachment:
                        info.FaceAttachmentIndex = index;
                        break;
                    case WearableType.Hair:
                        info.HairIndex = index;
                        break;
                    case WearableType.Moustache:
                        info.MoustacheIndex = index;
                        break;
                    default:
                        DebugConsole.ThrowError($"Wearable type not implemented: {type}");
                        return false;
                }

                info.RefreshHead();
                OnSliderMoved?.Invoke(scrollBar, scrollBar.BarScroll);
                return true;
            }

            public void Update()
            {
                if (HeadSelectionList != null && PlayerInput.PrimaryMouseButtonDown() &&
                    !GUI.IsMouseOn(HeadSelectionList))
                {
                    HeadSelectionList.Visible = false;
                }
            }

            public void AddToGUIUpdateList()
            {
                HeadSelectionList?.AddToGUIUpdateList();
            }

            private void ClearSprites()
            {
                foreach (Sprite sprite in characterSprites) { sprite.Remove(); }
                characterSprites.Clear();
            }
            
            public void Dispose()
            {
                ClearSprites();
            }

            ~AppearanceCustomizationMenu()
            {
                Dispose();
            }
        }
    }
}
