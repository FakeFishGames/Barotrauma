﻿using Barotrauma.Extensions;
using Barotrauma.Lights;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Structure : MapEntity, IDamageable, IServerSerializable
    {
        public static bool ShowWalls = true, ShowStructures = true;

        private List<ConvexHull> convexHulls;

        private readonly Dictionary<DecorativeSprite, DecorativeSprite.State> spriteAnimState = new Dictionary<DecorativeSprite, DecorativeSprite.State>();

        public readonly List<LightSource> Lights = new List<LightSource>();

        public override bool SelectableInEditor
        {
            get
            {
                if (GameMain.SubEditorScreen.IsSubcategoryHidden(Prefab.Subcategory))
                {
                    return false;
                }

                if (!SubEditorScreen.IsLayerVisible(this)) { return false; }

                return HasBody ? ShowWalls : ShowStructures;
            }
        }

        partial void InitProjSpecific()
        {
            Prefab.Sprite?.EnsureLazyLoaded();
            Prefab.BackgroundSprite?.EnsureLazyLoaded();

            foreach (var decorativeSprite in Prefab.DecorativeSprites)
            {
                decorativeSprite.Sprite.EnsureLazyLoaded();
                spriteAnimState.Add(decorativeSprite, new DecorativeSprite.State());
            }

            UpdateSpriteStates(0.0f);
        }

        public static Vector2 UpgradeTextureOffset(
            Vector2 targetSize,
            Vector2 originalTextureOffset,
            SubmarineInfo submarineInfo,
            Rectangle sourceRect,
            Vector2 scale,
            bool flippedX,
            bool flippedY)
        {
            if (submarineInfo.GameVersion <= Sprite.LastBrokenTiledSpriteGameVersion)
            {
                // Tiled sprite rendering was significantly changed after v1.2.3.0:
                // Rendering flipped, scaled and offset textures was completely broken,
                // but some existing community submarines depend on that old behavior,
                // so let's redo some of the broken logic here if the sub is old enough

                Vector2 flipper = (flippedX ? -1f : 1f, flippedY ? -1f : 1f);

                var textureOffset = originalTextureOffset * flipper;

                textureOffset = new Vector2(
                    MathUtils.PositiveModulo((int)-textureOffset.X, sourceRect.Width),
                    MathUtils.PositiveModulo((int)-textureOffset.Y, sourceRect.Height));

                textureOffset.X = (textureOffset.X / scale.X) % sourceRect.Width;
                textureOffset.Y = (textureOffset.Y / scale.Y) % sourceRect.Height;

                Vector2 flippedDrawOffset = Vector2.Zero;
                if (flippedX)
                {
                    float diff = targetSize.X % (sourceRect.Width * scale.X);
                    flippedDrawOffset.X = (sourceRect.Width * scale.X - diff) / scale.X;
                    flippedDrawOffset.X =
                        MathUtils.NearlyEqual(flippedDrawOffset.X, MathF.Round(flippedDrawOffset.X)) ?
                            MathF.Round(flippedDrawOffset.X) : flippedDrawOffset.X;
                }
                if (flippedY)
                {
                    float diff = targetSize.Y % (sourceRect.Height * scale.Y);
                    flippedDrawOffset.Y = (sourceRect.Height * scale.Y - diff) / scale.Y;
                    flippedDrawOffset.Y =
                        MathUtils.NearlyEqual(flippedDrawOffset.Y, MathF.Round(flippedDrawOffset.Y)) ?
                            MathF.Round(flippedDrawOffset.Y) : flippedDrawOffset.Y;
                }

                var textureOffsetPlusFlipBs = textureOffset + flippedDrawOffset;

                if (textureOffsetPlusFlipBs.X > sourceRect.Width)
                {
                    var diff = textureOffsetPlusFlipBs.X - sourceRect.Width;
                    textureOffset.X = (textureOffset.X + diff * (scale.X - 1f)) % sourceRect.Width;
                }
                if (textureOffsetPlusFlipBs.Y > sourceRect.Height)
                {
                    var diff = textureOffsetPlusFlipBs.Y - sourceRect.Height;
                    textureOffset.Y = (textureOffset.Y + diff * (scale.Y - 1f)) % sourceRect.Height;
                }

                textureOffset *= scale * flipper;

                return -textureOffset;
            }

            return originalTextureOffset;
        }

        partial void CreateConvexHull(Vector2 position, Vector2 size, float rotation)
        {
            if (!CastShadow) { return; }

            convexHulls ??= new List<ConvexHull>();

            //if the convex hull is longer than this, we need to split it to multiple parts
            //very large convex hulls don't play nicely with the lighting or LOS, because the shadow cast
            //by the convex hull would need to be extruded very far to cover the whole screen
            const float MaxConvexHullLength = 1024.0f;
            float length = IsHorizontal ? size.X : size.Y;
            int convexHullCount = (int)Math.Max(1, Math.Ceiling(length / MaxConvexHullLength));

            Vector2 sectionSize = size;
            if (convexHullCount > 1)
            {
                if (IsHorizontal)
                {
                    sectionSize.X = length / convexHullCount;
                }
                else
                {
                    sectionSize.Y = length / convexHullCount;
                }
            }

            for (int i = 0; i < convexHullCount; i++)
            {
                Vector2 offset =
                    (IsHorizontal ? Vector2.UnitX : Vector2.UnitY) *
                    (i * length / convexHullCount);

                var h = new ConvexHull(
                    new Rectangle((position - size / 2 + offset).ToPoint(), sectionSize.ToPoint()),
                    IsHorizontal,
                    this);
                if (Math.Abs(rotation) > 0.001f)
                {
                    h.Rotate(position, rotation);
                }
                convexHulls.Add(h);
            }
        }

        public override void UpdateEditing(Camera cam, float deltaTime)
        {
            if (editingHUD == null || editingHUD.UserData as Structure != this)
            {
                editingHUD = CreateEditingHUD(Screen.Selected != GameMain.SubEditorScreen);
            }
        }

        private void SetLightTextureOffset()
        {
            Vector2 textOffset = textureOffset;
            if (FlippedX) { textOffset.X = -textOffset.X; }
            if (FlippedY) { textOffset.Y = -textOffset.Y; }

            foreach (LightSource light in Lights)
            {
                Vector2 bgOffset = new Vector2(
                    MathUtils.PositiveModulo(-textOffset.X, light.texture.Width),
                    MathUtils.PositiveModulo(-textOffset.Y, light.texture.Height));

                light.LightTextureOffset = bgOffset;
            }
        }

        public GUIComponent CreateEditingHUD(bool inGame = false)
        {
            int heightScaled = (int)(20 * GUI.Scale);
            editingHUD = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GUI.Canvas, Anchor.CenterRight) { MinSize = new Point(400, 0) }) { UserData = this };
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(0.95f, 0.8f), editingHUD.RectTransform, Anchor.Center), style: null)
            {
                CanTakeKeyBoardFocus = false
            };
            var editor = new SerializableEntityEditor(listBox.Content.RectTransform, this, inGame, showName: true, titleFont: GUIStyle.LargeFont) { UserData = this };
          
            if (editor.Fields.TryGetValue(nameof(Scale).ToIdentifier(), out GUIComponent[] scaleFields) &&
                scaleFields.FirstOrDefault() is GUINumberInput scaleInput)
            {
                //texture offset needs to be adjusted when scaling the entity to keep the look of the entity unchanged
                scaleInput.OnValueChanged += (GUINumberInput numberInput) =>
                {
                    TextureOffset *= (Scale / ScaleWhenTextureOffsetSet);
                };
            }

            if (Submarine.MainSub?.Info?.Type == SubmarineType.OutpostModule)
            {
                GUITickBox tickBox = new GUITickBox(new RectTransform(new Point(listBox.Content.Rect.Width, 10)), TextManager.Get("sp.structure.removeiflinkedoutpostdoorinuse.name"))
                {
                    Font = GUIStyle.SmallFont,
                    Selected = RemoveIfLinkedOutpostDoorInUse,
                    ToolTip = TextManager.Get("sp.structure.removeiflinkedoutpostdoorinuse.description"),
                    OnSelected = (tickBox) =>
                    {
                        RemoveIfLinkedOutpostDoorInUse = tickBox.Selected;
                        return true;
                    }
                };
                editor.AddCustomContent(tickBox, 1);
            }

            if (!Layer.IsNullOrEmpty())
            {
                var layerText = new GUITextBlock(new RectTransform(new Point(listBox.Content.Rect.Width, heightScaled)) { MinSize = new Point(0, heightScaled) }, TextManager.AddPunctuation(':', TextManager.Get("editor.layer"), Layer));
                editor.AddCustomContent(layerText, 1);
            }

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Point(listBox.Content.Rect.Width, heightScaled)), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            var mirrorX = new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("MirrorEntityX"), style: "GUIButtonSmall")
            {
                ToolTip = TextManager.Get("MirrorEntityXToolTip"),
                OnClicked = (button, data) =>
                {
                    foreach (MapEntity me in SelectedList)
                    {
                        me.FlipX(relativeToSub: false);
                    }
                    if (!SelectedList.Contains(this)) { FlipX(relativeToSub: false); }
                    ColorFlipButton(button, FlippedX);
                    return true;
                }
            };
            ColorFlipButton(mirrorX, FlippedX);
            var mirrorY = new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("MirrorEntityY"), style: "GUIButtonSmall")
            {
                ToolTip = TextManager.Get("MirrorEntityYToolTip"),
                OnClicked = (button, data) =>
                {
                    foreach (MapEntity me in SelectedList)
                    {
                        me.FlipY(relativeToSub: false);
                    }
                    if (!SelectedList.Contains(this)) { FlipY(relativeToSub: false); }
                    ColorFlipButton(button, FlippedY);
                    return true;
                }
            };
            ColorFlipButton(mirrorY, FlippedY);
            new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("ReloadSprite"), style: "GUIButtonSmall")
            {
                OnClicked = (button, data) =>
                {
                    Sprite.ReloadXML();
                    Sprite.ReloadTexture();
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("ResetToPrefab"), style: "GUIButtonSmall")
            {
                OnClicked = (button, data) =>
                {
                    foreach (MapEntity me in SelectedList)
                    {
                        (me as Item)?.Reset();
                        (me as Structure)?.Reset();
                    }
                    if (!SelectedList.Contains(this)) { Reset(); }
                    CreateEditingHUD();
                    return true;
                }
            };
            buttonContainer.RectTransform.Resize(new Point(buttonContainer.Rect.Width, buttonContainer.RectTransform.Children.Max(c => c.MinSize.Y)));
            buttonContainer.RectTransform.IsFixedSize = true;
            GUITextBlock.AutoScaleAndNormalize(buttonContainer.Children.Where(c => c is GUIButton).Select(b => ((GUIButton)b).TextBlock));
            editor.AddCustomContent(buttonContainer, editor.ContentCount);

            PositionEditingHUD();

            return editingHUD;
        }

        partial void OnImpactProjSpecific(Fixture f1, Fixture f2, Contact contact)
        {
            if (!Prefab.Platform && Prefab.StairDirection == Direction.None)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(f2.Body.Position);

                int section = FindSectionIndex(pos);
                if (section > -1)
                {
                    Vector2 normal = contact.Manifold.LocalNormal;

                    float impact = Vector2.Dot(f2.Body.LinearVelocity, -normal) * f2.Body.Mass * 0.1f;
                    if (impact > 10.0f)
                    {
                        SoundPlayer.PlayDamageSound("StructureBlunt", impact, SectionPosition(section, true), tags: Tags);
                    }
                }
            }
        }

        public override bool IsVisible(Rectangle worldView)
        {
            RectangleF worldRect = Quad2D.FromSubmarineRectangle(WorldRect).Rotated(
                FlippedX != FlippedY
                    ? RotationRad
                    : -RotationRad).BoundingAxisAlignedRectangle;
            Vector2 worldPos = WorldPosition;

            Vector2 min = new Vector2(worldRect.X, worldRect.Y);
            Vector2 max = new Vector2(worldRect.Right, worldRect.Y + worldRect.Height);
            foreach (DecorativeSprite decorativeSprite in Prefab.DecorativeSprites)
            {
                Vector2 scale = decorativeSprite.GetScale(ref spriteAnimState[decorativeSprite].ScaleState, spriteAnimState[decorativeSprite].RandomScaleFactor) * Scale;
                min.X = Math.Min(worldPos.X - decorativeSprite.Sprite.size.X * decorativeSprite.Sprite.RelativeOrigin.X * scale.X, min.X);
                max.X = Math.Max(worldPos.X + decorativeSprite.Sprite.size.X * (1.0f - decorativeSprite.Sprite.RelativeOrigin.X) * scale.X, max.X);
                min.Y = Math.Min(worldPos.Y - decorativeSprite.Sprite.size.Y * (1.0f - decorativeSprite.Sprite.RelativeOrigin.Y) * scale.Y, min.Y);
                max.Y = Math.Max(worldPos.Y + decorativeSprite.Sprite.size.Y * decorativeSprite.Sprite.RelativeOrigin.Y * scale.Y, max.Y);
            }
            Vector2 offset = GetCollapseEffectOffset();
            min += offset;
            max += offset;

            if (min.X > worldView.Right || max.X < worldView.X) { return false; }
            if (min.Y > worldView.Y || max.Y < worldView.Y - worldView.Height) { return false; }

            Vector2 extents = max - min;
            if (extents.X * Screen.Selected.Cam.Zoom < 1.0f) { return false; }
            if (extents.Y * Screen.Selected.Cam.Zoom < 1.0f) { return false; }
            return true;
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (Prefab.Sprite == null) { return; }

            if (editing)
            {
                if (!SubEditorScreen.IsLayerVisible(this)) { return; }
                if (!HasBody && !ShowStructures) { return; }
                if (HasBody && !ShowWalls) { return; }
            }

            Draw(spriteBatch, editing, back, null);
        }

        public void DrawDamage(SpriteBatch spriteBatch, Effect damageEffect, bool editing)
        {
            Draw(spriteBatch, editing, false, damageEffect);
        }

        private float GetRealDepth()
        {
            return SpriteDepthOverrideIsSet ? SpriteOverrideDepth : Prefab.Sprite.Depth;
        }

        public override float GetDrawDepth()
        {
            return GetDrawDepth(GetRealDepth(), Prefab.Sprite);
        }

        private void Draw(SpriteBatch spriteBatch, bool editing, bool back = true, Effect damageEffect = null)
        {
            if (Prefab.Sprite == null) { return; }
            if (editing)
            {
                if (!SubEditorScreen.IsLayerVisible(this)) { return; }
                if (!HasBody && !ShowStructures) { return; }
                if (HasBody && !ShowWalls) { return; }
            }
            else if (IsHidden) 
            {
                return; 
            }

            Color color = IsIncludedInSelection && editing ? GUIStyle.Blue : IsHighlighted ? GUIStyle.Orange * Math.Max(spriteColor.A / (float) byte.MaxValue, 0.1f) : spriteColor;

            if (IsSelected && editing)
            {
                //color = Color.Lerp(color, Color.Gold, 0.5f);
                color = spriteColor;

                Vector2 rectSize = rect.Size.ToVector2();
                if (BodyWidth > 0.0f) { rectSize.X = BodyWidth; }
                if (BodyHeight > 0.0f) { rectSize.Y = BodyHeight; }

                Vector2 bodyPos = WorldPosition + BodyOffset * Scale;

                GUI.DrawRectangle(sb: spriteBatch,
                    center: new Vector2(bodyPos.X, -bodyPos.Y),
                    width: rectSize.X,
                    height: rectSize.Y,
                    rotation: BodyRotation,
                    clr: Color.White,
                    thickness: Math.Max(1, (int)(2 / Screen.Selected.Cam.Zoom)));
            }

            bool isWiringMode = editing && SubEditorScreen.TransparentWiringMode && SubEditorScreen.IsWiringMode();

            if (isWiringMode) { color *= 0.15f; }

            Vector2 drawOffset = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;
            drawOffset += GetCollapseEffectOffset();

            float depth = GetDrawDepth();

            Vector2 textureOffset = this.textureOffset;

            if (back && damageEffect == null && !isWiringMode)
            {
                if (Prefab.BackgroundSprite != null)
                {
                    Vector2 dropShadowOffset = Vector2.Zero;
                    if (UseDropShadow)
                    {
                        dropShadowOffset = DropShadowOffset;
                        if (dropShadowOffset == Vector2.Zero)
                        {
                            if (Submarine == null)
                            {
                                dropShadowOffset = Vector2.UnitY * 10.0f;
                            }
                            else
                            {
                                dropShadowOffset = IsHorizontal ?
                                    new Vector2(0.0f, Math.Sign(Submarine.HiddenSubPosition.Y - Position.Y) * 10.0f) :
                                    new Vector2(Math.Sign(Submarine.HiddenSubPosition.X - Position.X) * 10.0f, 0.0f);
                            }
                        }
                        dropShadowOffset.Y = -dropShadowOffset.Y;
                    }

                    Vector2 backGroundOffset = new Vector2(
                        MathUtils.PositiveModulo(-textureOffset.X, Prefab.BackgroundSprite.SourceRect.Width * TextureScale.X * Scale),
                        MathUtils.PositiveModulo(-textureOffset.Y, Prefab.BackgroundSprite.SourceRect.Height * TextureScale.Y * Scale));

                    float rotationRad = GetRotationForSprite(RotationRad, Prefab.BackgroundSprite);

                    Prefab.BackgroundSprite.DrawTiled(
                        spriteBatch,
                        new Vector2(rect.X + rect.Width / 2 + drawOffset.X, -(rect.Y - rect.Height / 2 + drawOffset.Y)),
                        new Vector2(rect.Width, rect.Height),
                        rotation: rotationRad,
                        origin: rect.Size.ToVector2() * new Vector2(0.5f, 0.5f),
                        color: Prefab.BackgroundSpriteColor,
                        textureScale: TextureScale * Scale,
                        startOffset: backGroundOffset,
                        depth: Math.Max(GetDrawDepth(Prefab.BackgroundSprite.Depth, Prefab.BackgroundSprite), depth + 0.000001f),
                        spriteEffects: Prefab.BackgroundSprite.effects ^ SpriteEffects);

                    if (UseDropShadow)
                    {
                        Prefab.BackgroundSprite.DrawTiled(
                            spriteBatch,
                            new Vector2(rect.X + rect.Width / 2 + drawOffset.X, -(rect.Y - rect.Height / 2 + drawOffset.Y)) + dropShadowOffset,
                            new Vector2(rect.Width, rect.Height),
                            rotation: rotationRad,
                            origin: rect.Size.ToVector2() * new Vector2(0.5f, 0.5f),
                            color: Color.Black * 0.5f,
                            textureScale: TextureScale * Scale,
                            startOffset: backGroundOffset,
                            depth: (depth + Prefab.BackgroundSprite.Depth) / 2.0f,
                            spriteEffects: Prefab.BackgroundSprite.effects ^ SpriteEffects);
                    }
                }
            }

            if (back == GetRealDepth() > 0.5f)
            {
                Vector2 advanceX = MathUtils.RotatedUnitXRadians(RotationRad).FlipY();
                Vector2 advanceY = advanceX.YX().FlipX();
                if (FlippedX != FlippedY)
                {
                    advanceX = advanceX.FlipY();
                    advanceY = advanceY.FlipX();
                }

                float sectionSpriteRotationRad = GetRotationForSprite(RotationRad, Prefab.Sprite);

                for (int i = 0; i < Sections.Length; i++)
                {
                    Rectangle drawSection = Sections[i].rect;
                    if (damageEffect != null)
                    {
                        float newCutoff = MathHelper.Lerp(0.0f, 0.65f, Sections[i].damage / MaxHealth);

                        if (Math.Abs(newCutoff - Submarine.DamageEffectCutoff) > 0.05f)
                        {
                            spriteBatch.End();
                            spriteBatch.Begin(SpriteSortMode.BackToFront,
                                BlendState.NonPremultiplied, SamplerState.LinearWrap,
                                null, null,
                                damageEffect,
                                Screen.Selected.Cam.Transform);

                            damageEffect.Parameters["aCutoff"].SetValue(newCutoff);
                            damageEffect.Parameters["cCutoff"].SetValue(newCutoff * 1.2f);
                            damageEffect.CurrentTechnique.Passes[0].Apply();
                            Submarine.DamageEffectCutoff = newCutoff;
                        }
                    }
                    if (!HasDamage && i == 0)
                    {
                        drawSection = new Rectangle(
                            drawSection.X,
                            drawSection.Y,
                            Sections[Sections.Length - 1].rect.Right - drawSection.X,
                            drawSection.Y - (Sections[Sections.Length - 1].rect.Y - Sections[Sections.Length - 1].rect.Height));
                        i = Sections.Length;
                    }

                    Vector2 sectionOffset = new Vector2(
                        Math.Abs(rect.Location.X - drawSection.Location.X),
                        Math.Abs(rect.Location.Y - drawSection.Location.Y));

                    if (FlippedX && IsHorizontal) { sectionOffset.X = rect.Right - drawSection.Right; }
                    if (FlippedY && !IsHorizontal) { sectionOffset.Y = (drawSection.Y - drawSection.Height) - (rect.Y - rect.Height); }

                    sectionOffset.X += MathUtils.PositiveModulo(-textureOffset.X, Prefab.Sprite.SourceRect.Width * TextureScale.X * Scale);
                    sectionOffset.Y += MathUtils.PositiveModulo(-textureOffset.Y, Prefab.Sprite.SourceRect.Height * TextureScale.Y * Scale);

                    Vector2 pos = new Vector2(drawSection.X, drawSection.Y);
                    pos -= rect.Location.ToVector2();
                    pos = advanceX * pos.X + advanceY * pos.Y;
                    pos += rect.Location.ToVector2();
                    pos = new Vector2(pos.X + rect.Width / 2 + drawOffset.X, -(pos.Y - rect.Height / 2 + drawOffset.Y));

                    Prefab.Sprite.DrawTiled(
                        spriteBatch,
                        pos,
                        new Vector2(drawSection.Width, drawSection.Height),
                        rotation: sectionSpriteRotationRad,
                        origin: rect.Size.ToVector2() * new Vector2(0.5f, 0.5f),
                        color: color,
                        startOffset: sectionOffset,
                        depth: depth,
                        textureScale: TextureScale * Scale,
                        spriteEffects: Prefab.Sprite.effects ^ SpriteEffects);
                }

                foreach (var decorativeSprite in Prefab.DecorativeSprites)
                {
                    if (!spriteAnimState[decorativeSprite].IsActive) { continue; }
                    float rotation = decorativeSprite.GetRotation(ref spriteAnimState[decorativeSprite].RotationState, spriteAnimState[decorativeSprite].RandomRotationFactor) + RotationRad;
                    Vector2 offset = decorativeSprite.GetOffset(ref spriteAnimState[decorativeSprite].OffsetState, spriteAnimState[decorativeSprite].RandomOffsetMultiplier) * Scale;
                    if (FlippedX && Prefab.CanSpriteFlipX) { offset.X = -offset.X; }
                    if (FlippedY && Prefab.CanSpriteFlipY) { offset.Y = -offset.Y; }
                    Vector2 drawPos = DrawPosition + MathUtils.RotatePoint(offset, -this.RotationRad);
                    decorativeSprite.Sprite.Draw(
                        spriteBatch: spriteBatch,
                        pos: drawPos.FlipY(),
                        color: color,
                        rotate: rotation,
                        origin: decorativeSprite.Sprite.Origin,
                        scale: decorativeSprite.GetScale(ref spriteAnimState[decorativeSprite].ScaleState, spriteAnimState[decorativeSprite].RandomScaleFactor) * Scale,
                        spriteEffect: Prefab.Sprite.effects ^ SpriteEffects,
                        depth: Math.Min(depth + (decorativeSprite.Sprite.Depth - Prefab.Sprite.Depth), 0.999f));
                }
            }

            static float GetRotationForSprite(float rotationRad, Sprite sprite)
            {
                // Use bitwise operations instead of HasFlag to avoid boxing, as this is performance-sensitive code.
                bool flipHorizontally = (sprite.effects & SpriteEffects.FlipHorizontally) == SpriteEffects.FlipHorizontally;
                bool flipVertically = (sprite.effects & SpriteEffects.FlipVertically) == SpriteEffects.FlipVertically;
                
                if (flipHorizontally != flipVertically)
                {
                    rotationRad = -rotationRad;
                }
                return rotationRad;
            }

            if (GameMain.DebugDraw && Screen.Selected.Cam.Zoom > 0.5f)
            {
                if (Bodies != null)
                {
                    foreach (var body in Bodies)
                    {
                        Vector2 pos = ConvertUnits.ToDisplayUnits(body.Position);
                        if (Submarine != null) { pos += Submarine.DrawPosition; }
                        pos.Y = -pos.Y;
                        var dimensions = bodyDimensions[body];
                        GUI.DrawRectangle(spriteBatch,
                            pos,
                            ConvertUnits.ToDisplayUnits(dimensions.X),
                            ConvertUnits.ToDisplayUnits(dimensions.Y),
                            -body.Rotation, Color.White);
                    }
                }

                if (SectionCount > 0 && HasBody)
                {
                    for (int i = 0; i < SectionCount; i++)
                    {
                        if (GetSection(i).damage > 0)
                        {
                            var textPos = SectionPosition(i, true);
                            if (Submarine != null)
                            { 
                                textPos += (Submarine.DrawPosition - Submarine.Position);
                            }
                            textPos.Y = -textPos.Y;
                            GUI.DrawString(spriteBatch, textPos, "Damage: " + (int)((GetSection(i).damage / MaxHealth) * 100f) + "%", Color.Yellow);
                        }
                    }
                }
            }
        }

        public void UpdateSpriteStates(float deltaTime)
        {
            if (Prefab.DecorativeSpriteGroups.Count == 0) { return; }            
            DecorativeSprite.UpdateSpriteStates(Prefab.DecorativeSpriteGroups, spriteAnimState, ID, deltaTime, ConditionalMatches);            
            foreach (int spriteGroup in Prefab.DecorativeSpriteGroups.Keys)
            {
                for (int i = 0; i < Prefab.DecorativeSpriteGroups[spriteGroup].Length; i++)
                {
                    var decorativeSprite = Prefab.DecorativeSpriteGroups[spriteGroup][i];
                    if (decorativeSprite == null) { continue; }
                    if (spriteGroup > 0)
                    {
                        int activeSpriteIndex = ID % Prefab.DecorativeSpriteGroups[spriteGroup].Length;
                        if (i != activeSpriteIndex)
                        {
                            spriteAnimState[decorativeSprite].IsActive = false;
                            continue;
                        }
                    }

                    //check if the sprite is active (whether it should be drawn or not)
                    var spriteState = spriteAnimState[decorativeSprite];
                    spriteState.IsActive = true;
                    foreach (PropertyConditional conditional in decorativeSprite.IsActiveConditionals)
                    {
                        if (!ConditionalMatches(conditional))
                        {
                            spriteState.IsActive = false;
                            break;
                        }
                    }
                    if (!spriteState.IsActive) { continue; }

                    //check if the sprite should be animated
                    bool animate = true;
                    foreach (PropertyConditional conditional in decorativeSprite.AnimationConditionals)
                    {
                        if (!ConditionalMatches(conditional)) { animate = false; break; }
                    }
                    if (!animate) { continue; }
                    spriteState.OffsetState += deltaTime;
                    spriteState.RotationState += deltaTime;
                }
            }
        }

        private bool ConditionalMatches(PropertyConditional conditional)
        {
            if (!string.IsNullOrEmpty(conditional.TargetItemComponent))
            {
                return false;
            }
            else
            {
                if (!conditional.Matches(this)) { return false; }
            }
            return true;
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            byte sectionCount = msg.ReadByte();

            bool invalidMessage = false;
            if (sectionCount != Sections.Length)
            {
                invalidMessage = true;
                string errorMsg = $"Error while reading a network event for the structure \"{Name} ({ID})\". Section count does not match (server: {sectionCount} client: {Sections.Length})";
                GameAnalyticsManager.AddErrorEventOnce("Structure.ClientRead:SectionCountMismatch", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                throw new Exception(errorMsg);
            }

            for (int i = 0; i < sectionCount; i++)
            {
                float damage = msg.ReadRangedSingle(0.0f, 1.0f, 8) * MaxHealth;
                if (!invalidMessage && i < Sections.Length)
                {
                    SetDamage(i, damage, isNetworkEvent: true);
                }
            }
        }
    }
}
