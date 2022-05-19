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
using System.Collections.Immutable;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        private static Sprite infoAreaPortraitBG;

        public bool LastControlled;
        public int CrewListIndex { get; set; } = -1;

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
            infoAreaPortraitBG = GUIStyle.GetComponentStyle("InfoAreaPortraitBG")?.GetDefaultSprite();
            new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(833, 298, 142, 98), null, 0);
        }

        partial void LoadHeadSpriteProjectSpecific(ContentXElement limbElement)
        {
            ContentXElement maskElement = limbElement.GetChildElement("tintmask");
            if (maskElement != null)
            {
                ContentPath tintMaskPath = maskElement.GetAttributeContentPath("texture");
                if (!tintMaskPath.IsNullOrEmpty())
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

            GUIFont font = paddedFrame.Rect.Width < 280 ? GUIStyle.SmallFont : GUIStyle.Font;

            var headerTextArea = new GUILayoutGroup(new RectTransform(new Vector2(0.575f, 1.0f), headerArea.RectTransform))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            Color? nameColor = null;
            if (Job != null) { nameColor = Job.Prefab.UIColor; }

            GUITextBlock characterNameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform), ToolBox.LimitString(Name, GUIStyle.Font, headerTextArea.Rect.Width), textColor: nameColor, font: GUIStyle.Font)
            {
                ForceUpperCase = ForceUpperCase.Yes,
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

            if (PersonalityTrait != null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform),
                    TextManager.AddPunctuation(':', TextManager.Get("PersonalityTrait"), TextManager.Get("personalitytrait." + PersonalityTrait.Name.Replace(" ".ToIdentifier(), "".ToIdentifier()))),
                    font: font)
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

                var skills = Job.GetSkills().ToList();
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

                LocalizedString deadDescription = 
                    TextManager.Get("deceased") + "\n" + 
                   (Character.CauseOfDeath.Affliction?.CauseOfDeathDescription ?? TextManager.Get("CauseOfDeath." + Character.CauseOfDeath.Type.ToString()));

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), deadArea.RectTransform), deadDescription, textColor: GUIStyle.Red, font: font, textAlignment: Alignment.TopLeft) { Padding = Vector4.Zero };
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

            GUITextBlock textBlock = new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(40, 0) }, text, textColor: textColor, font: GUIStyle.SmallFont);
            new GUICustomComponent(new RectTransform(new Point(frame.Rect.Height, frame.Rect.Height), frame.RectTransform, Anchor.CenterLeft) { IsFixedSize = false }, 
                onDraw: (sb, component) => DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()));
            return frame;
        }

        partial void OnSkillChanged(Identifier skillIdentifier, float prevLevel, float newLevel)
        {
            if (TeamID == CharacterTeamType.FriendlyNPC) { return; }
            if (Character.Controlled != null && Character.Controlled.TeamID != TeamID) { return; }

            // if we increased by more than 1 in one increase, then display special color (for talents)
            bool specialIncrease = Math.Abs(newLevel - prevLevel) >= 1.0f;

            if ((int)newLevel > (int)prevLevel)
            {
                int increase = Math.Max((int)newLevel - (int)prevLevel, 1);

                Character?.AddMessage(
                    "+[value] "+ TextManager.Get("SkillName." + skillIdentifier).Value, 
                    specialIncrease ? GUIStyle.Orange : GUIStyle.Green, 
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
                    "+[value] " + TextManager.Get("experienceshort").Value,
                    GUIStyle.Blue, playSound: Character == Character.Controlled, "exp".ToIdentifier(), increase);
            }
        }

        private void GetDisguisedSprites(IdCard idCard)
        {
            if (idCard.Item.Tags == string.Empty) return;

            if (idCard.StoredOwnerAppearance.JobPrefab == null || idCard.StoredOwnerAppearance.Portrait == null)
            {
                var readTags = idCard.Item.Tags.Split(',')
                    .Where(s => s.Contains(':'))
                    .Select(s => s.Split(':'))
                    .Select(s => (s[0].ToIdentifier(),s[1]))
                    .ToImmutableDictionary();

                if (readTags.None()) { return; }

                if (idCard.StoredOwnerAppearance.JobPrefab == null)
                {
                    idCard.StoredOwnerAppearance.ExtractJobPrefab(readTags);
                }

                if (idCard.StoredOwnerAppearance.Portrait == null)
                {
                    idCard.StoredOwnerAppearance.ExtractAppearance(this, idCard);
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
            Head.FaceAttachment?.GetChildElements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.FaceAttachment)));
            Head.BeardElement?.GetChildElements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.Beard)));
            Head.MoustacheElement?.GetChildElements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.Moustache)));
            Head.HairElement?.GetChildElements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.Hair)));
            if (omitJob)
            {
                JobPrefab.NoJobElement?.GetChildElement("PortraitClothing")?.GetChildElements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.JobIndicator)));
            }
            else
            {
                Job?.Prefab.ClothingElement?.GetChildElements("sprite").ForEach(s => attachmentSprites.Add(new WearableSprite(s, WearableType.JobIndicator)));
            }
        }

        // Doesn't work if the head's source rect does not start at 0,0.
        public static Point CalculateOffset(Sprite sprite, Point offset) => sprite.SourceRect.Size * offset;

        public void CalculateHeadPosition(Sprite sprite)
        {
            if (sprite == null) { return; }
            if (Head.SheetIndex == null) { return; }
            Point location = CalculateOffset(sprite, Head.SheetIndex.ToPoint());
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

        public void DrawForeground(SpriteBatch spriteBatch)
        {
            if (Character is null || !(GameMain.GameSession?.Campaign is MultiPlayerCampaign)) { return; }
            const int million = 1000000;
            int xfraction = (int)(HUDLayoutSettings.BottomRightInfoArea.Width * 0.2f);
            int yoffset = GUI.IntScale(6);

            int walletAmount = Character.Wallet.Balance;

            LocalizedString str = walletAmount >= million ? TextManager.Get("crewwallet.balance.toomuchtoshow") : TextManager.FormatCurrency(walletAmount);
            Vector2 size = GUIStyle.Font.MeasureString(str);
            int barHeight = GUI.IntScale(18);

            Rectangle barRect = new Rectangle((int)(HUDLayoutSettings.BottomRightInfoArea.X + xfraction / 2.5f), HUDLayoutSettings.BottomRightInfoArea.Bottom - barHeight - yoffset, HUDLayoutSettings.BottomRightInfoArea.Width - xfraction, barHeight);
            float textScale = Math.Max(0.1f, Math.Min(barRect.Width / size.X, barRect.Height / size.Y)) - 0.01f;

            GUIStyle.WalletPortraitBG.Draw(spriteBatch, barRect, Color.White);

            int iconSize = GUI.IntScale(28);
            int iconXOffset = iconSize / 2;
            Rectangle iconRect = new Rectangle(barRect.Right - iconXOffset, barRect.Top - iconSize / 4, iconSize, iconSize);
            GUIStyle.CrewWalletIconSmall.Draw(spriteBatch, iconRect, Color.White);
            var (scaledTextSizeX, scaledTextSizeY) = size * textScale;
            GUIStyle.Font.DrawString(spriteBatch, str, new Vector2(barRect.Right - iconXOffset - scaledTextSizeX - GUI.IntScale(4), barRect.Center.Y - scaledTextSizeY / 2), GUIStyle.TextColorNormal, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
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
                headSprite.SourceRect = new Rectangle(CalculateOffset(headSprite, Head.SheetIndex.ToPoint()), headSprite.SourceRect.Size);
                SetHeadEffect(spriteBatch);
                headSprite.Draw(spriteBatch, screenPos, scale: scale, color: Head.SkinColor);
                if (AttachmentSprites != null)
                {
                    float depthStep = 0.000001f;
                    foreach (var attachment in AttachmentSprites)
                    {
                        SetAttachmentEffect(spriteBatch, attachment);
                        DrawAttachmentSprite(spriteBatch, attachment, headSprite, Head.SheetIndex, screenPos, scale, depthStep, GetAttachmentColor(attachment, Head.HairColor, Head.FacialHairColor));
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

        public static CharacterInfo ClientRead(Identifier speciesName, IReadMessage inc)
        {
            ushort infoID = inc.ReadUInt16();
            string newName = inc.ReadString();
            string originalName = inc.ReadString();
            int tagCount = inc.ReadByte();
            HashSet<Identifier> tagSet = new HashSet<Identifier>();
            for (int i = 0; i < tagCount; i++)
            {
                tagSet.Add(inc.ReadIdentifier());
            }
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
            Dictionary<Identifier, float> skillLevels = new Dictionary<Identifier, float>();
            if (!string.IsNullOrEmpty(jobIdentifier))
            {
                jobPrefab = JobPrefab.Get(jobIdentifier);
                byte skillCount = inc.ReadByte();
                for (int i = 0; i < skillCount; i++)
                {
                    Identifier skillIdentifier = inc.ReadIdentifier();
                    float skillLevel = inc.ReadSingle();
                    skillLevels.Add(skillIdentifier, skillLevel);
                }
            }

            // TODO: animations
            CharacterInfo ch = new CharacterInfo(speciesName, newName, originalName, jobPrefab, ragdollFile, variant)
            {
                ID = infoID,
            };
            ch.RecreateHead(tagSet.ToImmutableHashSet(), hairIndex, beardIndex, moustacheIndex, faceAttachmentIndex);
            ch.Head.SkinColor = skinColor;
            ch.Head.HairColor = hairColor;
            ch.Head.FacialHairColor = facialHairColor;
            ch.SetPersonalityTrait();
            if (ch.Job != null)
            {
                ch.Job.OverrideSkills(skillLevels);
            }

            ch.ExperiencePoints = inc.ReadUInt16();
            ch.AdditionalTalentPoints = inc.ReadRangedInteger(0, MaxAdditionalTalentPoints);
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

                RectTransform createItemRectTransform(Identifier labelTag, float width = 0.6f)
                {
                    var layoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.166f), content.RectTransform));
                    
                    var label = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), layoutGroup.RectTransform),
                        TextManager.Get(labelTag), font: GUIStyle.SubHeadingFont);

                    var bottomItem = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), layoutGroup.RectTransform),
                        style: null);
                    
                    return new RectTransform(new Vector2(width, 1.0f), bottomItem.RectTransform, Anchor.Center);
                }

                RectTransform menuCategoryRT = createItemRectTransform(info.Prefab.MenuCategoryVar, 1.0f);
                
                GUILayoutGroup menuCategoryContainer =
                    new GUILayoutGroup(menuCategoryRT, isHorizontal: true)
                    {
                        Stretch = true,
                        RelativeSpacing = 0.05f
                    };

                void createMenuCategoryButton(Identifier tag)
                {
                    new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), menuCategoryContainer.RectTransform),
                        TextManager.Get(tag), style: "ListBoxElement")
                    {
                        UserData = tag,
                        OnClicked = OpenHeadSelection,
                        Selected = info.Head.Preset.TagSet.Contains(tag)
                    };
                }

                foreach (var tag in info.Prefab.VarTags[info.Prefab.MenuCategoryVar].OrderBy(t => t.Value).Reverse())
                {
                    createMenuCategoryButton(tag);
                }

                List<GUIScrollBar> attachmentSliders = new List<GUIScrollBar>();
                void createAttachmentSlider(int initialValue, WearableType wearableType)
                {
                    int attachmentCount = info.CountValidAttachmentsOfType(wearableType);
                    if (attachmentCount > 0)
                    {
                        var labelTag = wearableType == WearableType.FaceAttachment
                            ? "FaceAttachment.Accessories".ToIdentifier()
                            : $"FaceAttachment.{wearableType}".ToIdentifier();
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

                createAttachmentSlider(info.Head.HairIndex, WearableType.Hair);
                createAttachmentSlider(info.Head.BeardIndex, WearableType.Beard);
                createAttachmentSlider(info.Head.MoustacheIndex, WearableType.Moustache);
                createAttachmentSlider(info.Head.FaceAttachmentIndex, WearableType.FaceAttachment);

                void createColorSelector(Identifier labelTag, IEnumerable<(Color Color, float Commonness)> options, Func<Color> getter,
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
                    Color? previewingColor = null;
                    dropdown.OnSelected = (component, color) =>
                    {
                        previewingColor = null;
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
                    new GUICustomComponent(new RectTransform(Vector2.One, buttonFrame.RectTransform),
                        onUpdate: (deltaTime, component) =>
                        {
                            if (GUI.MouseOn is GUIFrame { Parent: { } p } hoveredFrame && dropdown.ListBox.Content.IsParentOf(hoveredFrame))
                            {
                                previewingColor ??= getter();
                                Color color = (Color)(dropdown.ListBox.Content.FindChild(c =>
                                    c == hoveredFrame || c.IsParentOf(hoveredFrame))?.UserData ?? dropdown.SelectedData ?? getter());
                                setter(color);
                                buttonFrame.Color = getter();
                                buttonFrame.HoverColor = getter();
                            }
                            else if (previewingColor.HasValue)
                            {
                                setter(previewingColor.Value);
                                buttonFrame.Color = getter();
                                buttonFrame.HoverColor = getter();
                                previewingColor = null;
                            }
                        }, onDraw: null)
                    {
                        CanBeFocused = false,
                        Visible = true
                    };
                }

                if (info.CountValidAttachmentsOfType(WearableType.Hair) > 0)
                {
                    createColorSelector($"Customization.{nameof(info.Head.HairColor)}".ToIdentifier(), info.HairColors,
                        () => info.Head.HairColor, (color) => info.Head.HairColor = color);
                }

                if (info.CountValidAttachmentsOfType(WearableType.Moustache) > 0 ||
                    info.CountValidAttachmentsOfType(WearableType.Beard) > 0)
                {
                    createColorSelector($"Customization.{nameof(info.Head.FacialHairColor)}".ToIdentifier(), info.FacialHairColors,
                        () => info.Head.FacialHairColor, (color) => info.Head.FacialHairColor = color);
                }
                
                createColorSelector($"Customization.{nameof(info.Head.SkinColor)}".ToIdentifier(), info.SkinColors, () => info.Head.SkinColor,
                    (color) => info.Head.SkinColor = color);

                RandomizeButton = new GUIButton(new RectTransform(Vector2.One * 0.12f,
                        parentComponent.RectTransform,
                        anchor: Anchor.BottomRight, scaleBasis: ScaleBasis.Smallest)
                    { RelativeOffset = new Vector2(0.01f, 0.005f) }, style: "RandomizeButton")
                {
                    OnClicked = (button, o) =>
                    {
                        var headPreset = info.Prefab.Heads.GetRandom(Rand.RandSync.Unsynced);
                        info.Head = new HeadInfo(info, headPreset);
                        info.SetAttachments(Rand.RandSync.Unsynced);
                        info.SetColors(Rand.RandSync.Unsynced);

                        RecreateFrameContents();
                        info.RefreshHead();
                        OnHeadSwitch?.Invoke(this);
                        attachmentSliders.ForEach(s => OnSliderMoved?.Invoke(s, s.BarScroll));

                        return false;
                    }
                };
                listBox.ForceLayoutRecalculation();
                foreach (var childLayoutGroup in listBox.Content.GetAllChildren<GUILayoutGroup>())
                {
                    childLayoutGroup.Recalculate();
                }
            }

            private bool OpenHeadSelection(GUIButton button, object userData)
            {
                Identifier selectedCategory = (Identifier)userData;

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
                    new RectTransform(new Vector2(1.25f, 1.25f), HeadSelectionList.ContentBackground.RectTransform, Anchor.Center),
                    style: "OuterGlow", color: Color.Black)
                {
                    UserData = "outerglow",
                    CanBeFocused = false
                };

                GUILayoutGroup row = null;
                int itemsInRow = 0;

                ContentXElement headElement = info.Ragdoll.MainElement.Elements().FirstOrDefault(e =>
                    e.GetAttributeString("type", "").Equals("head", StringComparison.OrdinalIgnoreCase));
                ContentXElement headSpriteElement = headElement.GetChildElement("sprite");
                ContentPath spritePathWithTags = headSpriteElement.GetAttributeContentPath("texture");

                var characterConfigElement = info.CharacterConfigElement;

                var heads = info.Prefab.Heads;
                if (heads != null)
                {
                    row = null;
                    itemsInRow = 0;
                    foreach (var head in heads.Where(h => h.TagSet.Contains(selectedCategory)))
                    {
                        string spritePath = info.Prefab.ReplaceVars(spritePathWithTags.Value, head);

                        if (!File.Exists(spritePath)) { continue; }

                        Sprite headSprite = new Sprite(headSpriteElement, "", spritePath);
                        headSprite.SourceRect =
                            new Rectangle(CharacterInfo.CalculateOffset(headSprite, head.SheetIndex.ToPoint()),
                                headSprite.SourceRect.Size);
                        characterSprites.Add(headSprite);

                        if (itemsInRow >= 4 || row == null)
                        {
                            row = new GUILayoutGroup(
                                new RectTransform(new Vector2(1.0f, 0.333f), HeadSelectionList.Content.RectTransform),
                                true)
                            {
                                UserData = head.MenuCategory,
                                Visible = true
                            };
                            itemsInRow = 0;
                        }

                        var btn = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), row.RectTransform),
                            style: "ListBoxElementSquare")
                        {
                            OutlineColor = Color.White * 0.5f,
                            PressedColor = Color.White * 0.5f,
                            UserData = head,
                            OnClicked = SwitchHead,
                            Selected = info.Head.Preset == head,
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
                var headPreset = obj as HeadPreset;
                if (info.Head.Preset != headPreset)
                {
                    info.Head = new HeadInfo(info, headPreset)
                    {
                        SkinColor = info.Head.SkinColor,
                        HairColor = info.Head.HairColor,
                        FacialHairColor = info.Head.FacialHairColor
                    };
                    info.ReloadHeadAttachments();
                }

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
                        info.Head.BeardIndex = index;
                        break;
                    case WearableType.FaceAttachment:
                        info.Head.FaceAttachmentIndex = index;
                        break;
                    case WearableType.Hair:
                        info.Head.HairIndex = index;
                        break;
                    case WearableType.Moustache:
                        info.Head.MoustacheIndex = index;
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
                if (HeadSelectionList != null)
                {
                    HeadSelectionList.RectTransform.Parent = null;
                    HeadSelectionList = null;
                }
            }
        }
    }
}
