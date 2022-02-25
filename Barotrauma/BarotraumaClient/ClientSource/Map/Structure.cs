using Barotrauma.Extensions;
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

#if DEBUG
        [Editable, Serialize("", IsPropertySaveable.Yes)]
#else
        [Serialize("", IsPropertySaveable.Yes)]
#endif
        public string SpecialTag
        {
            get;
            set;
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

        partial void CreateConvexHull(Vector2 position, Vector2 size, float rotation)
        {
            if (!CastShadow) { return; }

            if (convexHulls == null)
            {
                convexHulls = new List<ConvexHull>();
            }

            Vector2 halfSize = size / 2;
            Vector2[] verts = new Vector2[]
            {
                position + new Vector2(-halfSize.X, halfSize.Y),
                position + new Vector2(halfSize.X, halfSize.Y),
                position + new Vector2(halfSize.X, -halfSize.Y),
                position + new Vector2(-halfSize.X, -halfSize.Y),
            };

            var h = new ConvexHull(verts, Color.Black, this);
            if (Math.Abs(rotation) > 0.001f)
            {
                h.Rotate(position, rotation);
            }
            convexHulls.Add(h);
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
                    MathUtils.PositiveModulo((int)-textOffset.X, light.texture.Width),
                    MathUtils.PositiveModulo((int)-textOffset.Y, light.texture.Height));

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

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Point(listBox.Content.Rect.Width, heightScaled)), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("MirrorEntityX"))
            {
                ToolTip = TextManager.Get("MirrorEntityXToolTip"),
                OnClicked = (button, data) =>
                {
                    foreach (MapEntity me in SelectedList)
                    {
                        me.FlipX(relativeToSub: false);
                    }
                    if (!SelectedList.Contains(this)) { FlipX(relativeToSub: false); }
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("MirrorEntityY"))
            {
                ToolTip = TextManager.Get("MirrorEntityYToolTip"),
                OnClicked = (button, data) =>
                {
                    foreach (MapEntity me in SelectedList)
                    {
                        me.FlipY(relativeToSub: false);
                    }
                    if (!SelectedList.Contains(this)) { FlipY(relativeToSub: false); }
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("ReloadSprite"))
            {
                OnClicked = (button, data) =>
                {
                    Sprite.ReloadXML();
                    Sprite.ReloadTexture(updateAllSprites: true);
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("ResetToPrefab"))
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
            Rectangle worldRect = WorldRect;
            Vector2 worldPos = WorldPosition;

            Vector2 min = new Vector2(worldRect.X, worldRect.Y - worldRect.Height);
            Vector2 max = new Vector2(worldRect.Right, worldRect.Y);
            foreach (DecorativeSprite decorativeSprite in Prefab.DecorativeSprites)
            {
                float scale = decorativeSprite.GetScale(spriteAnimState[decorativeSprite].RandomScaleFactor) * Scale;
                min.X = Math.Min(worldPos.X - decorativeSprite.Sprite.size.X * decorativeSprite.Sprite.RelativeOrigin.X * scale, min.X);
                max.X = Math.Max(worldPos.X + decorativeSprite.Sprite.size.X * (1.0f - decorativeSprite.Sprite.RelativeOrigin.X) * scale, max.X);
                min.Y = Math.Min(worldPos.Y - decorativeSprite.Sprite.size.Y * (1.0f - decorativeSprite.Sprite.RelativeOrigin.Y) * scale, min.Y);
                max.Y = Math.Max(worldPos.Y + decorativeSprite.Sprite.size.Y * decorativeSprite.Sprite.RelativeOrigin.Y * scale, max.Y);
            }

            if (min.X > worldView.Right || max.X < worldView.X) { return false; }
            if ( min.Y > worldView.Y || max.Y < worldView.Y - worldView.Height) { return false; }

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

        public float GetDrawDepth()
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
            else if (HiddenInGame) { return; }

            Color color = IsIncludedInSelection && editing ? GUIStyle.Blue : IsHighlighted ? GUIStyle.Orange * Math.Max(spriteColor.A / (float) byte.MaxValue, 0.1f) : spriteColor;

            if (IsSelected && editing)
            {
                //color = Color.Lerp(color, Color.Gold, 0.5f);
                color = spriteColor;

                Vector2 rectSize = rect.Size.ToVector2();
                if (BodyWidth > 0.0f) { rectSize.X = BodyWidth; }
                if (BodyHeight > 0.0f) { rectSize.Y = BodyHeight; }

                Vector2 bodyPos = WorldPosition + BodyOffset * Scale;

                GUI.DrawRectangle(spriteBatch, new Vector2(bodyPos.X, -bodyPos.Y), rectSize.X, rectSize.Y, BodyRotation, Color.White,
                    thickness: Math.Max(1, (int)(2 / Screen.Selected.Cam.Zoom)));
            }

            bool isWiringMode = editing && SubEditorScreen.TransparentWiringMode && SubEditorScreen.IsWiringMode();

            if (isWiringMode) { color *= 0.15f; }

            Vector2 drawOffset = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;

            float depth = GetDrawDepth();

            Vector2 textureOffset = this.textureOffset;
            if (FlippedX) { textureOffset.X = -textureOffset.X; }
            if (FlippedY) { textureOffset.Y = -textureOffset.Y; }

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

                    SpriteEffects oldEffects = Prefab.BackgroundSprite.effects;
                    Prefab.BackgroundSprite.effects ^= SpriteEffects;

                    Vector2 backGroundOffset = new Vector2(
                        MathUtils.PositiveModulo((int)-textureOffset.X, Prefab.BackgroundSprite.SourceRect.Width),
                        MathUtils.PositiveModulo((int)-textureOffset.Y, Prefab.BackgroundSprite.SourceRect.Height));

                    Prefab.BackgroundSprite.DrawTiled(
                        spriteBatch,
                        new Vector2(rect.X + drawOffset.X, -(rect.Y + drawOffset.Y)),
                        new Vector2(rect.Width, rect.Height),
                        color: Prefab.BackgroundSpriteColor,
                        textureScale: TextureScale * Scale,
                        startOffset: backGroundOffset,
                        depth: Math.Max(GetDrawDepth(Prefab.BackgroundSprite.Depth, Prefab.BackgroundSprite), depth + 0.000001f));

                    if (UseDropShadow)
                    {
                        Prefab.BackgroundSprite.DrawTiled(
                            spriteBatch,
                            new Vector2(rect.X + drawOffset.X, -(rect.Y + drawOffset.Y)) + dropShadowOffset,
                            new Vector2(rect.Width, rect.Height),
                            color: Color.Black * 0.5f,
                            textureScale: TextureScale * Scale,
                            startOffset: backGroundOffset,
                            depth: (depth + Prefab.BackgroundSprite.Depth) / 2.0f);
                    }

                    Prefab.BackgroundSprite.effects = oldEffects;
                }
            }

            if (back == GetRealDepth() > 0.5f)
            {
                SpriteEffects oldEffects = Prefab.Sprite.effects;
                Prefab.Sprite.effects ^= SpriteEffects;

                for (int i = 0; i < Sections.Length; i++)
                {
                    Rectangle drawSection = Sections[i].rect;
                    if (damageEffect != null)
                    {
                        float newCutoff = MathHelper.Lerp(0.0f, 0.65f, Sections[i].damage / MaxHealth);

                        if (Math.Abs(newCutoff - Submarine.DamageEffectCutoff) > 0.01f || color != Submarine.DamageEffectColor)
                        {
                            damageEffect.Parameters["aCutoff"].SetValue(newCutoff);
                            damageEffect.Parameters["cCutoff"].SetValue(newCutoff * 1.2f);
                            damageEffect.Parameters["inColor"].SetValue(color.ToVector4());

                            damageEffect.CurrentTechnique.Passes[0].Apply();

                            Submarine.DamageEffectCutoff = newCutoff;
                            Submarine.DamageEffectColor = color;
                        }
                    }
                    if (!HasDamage && i == 0)
                    {
                        drawSection = new Rectangle(
                            drawSection.X,
                            drawSection.Y,
                            Sections[Sections.Length -1 ].rect.Right - drawSection.X,
                            drawSection.Y - (Sections[Sections.Length - 1].rect.Y - Sections[Sections.Length - 1].rect.Height));
                        i = Sections.Length;
                    }

                    Vector2 sectionOffset = new Vector2(
                        Math.Abs(rect.Location.X - drawSection.Location.X),
                        Math.Abs(rect.Location.Y - drawSection.Location.Y));

                    if (FlippedX && IsHorizontal) { sectionOffset.X = drawSection.Right - rect.Right; }
                    if (FlippedY && !IsHorizontal) { sectionOffset.Y = (rect.Y - rect.Height) - (drawSection.Y - drawSection.Height); }

                    sectionOffset.X += MathUtils.PositiveModulo((int)-textureOffset.X, Prefab.Sprite.SourceRect.Width);
                    sectionOffset.Y += MathUtils.PositiveModulo((int)-textureOffset.Y, Prefab.Sprite.SourceRect.Height);

                    Prefab.Sprite.DrawTiled(
                        spriteBatch,
                        new Vector2(drawSection.X + drawOffset.X, -(drawSection.Y + drawOffset.Y)),
                        new Vector2(drawSection.Width, drawSection.Height),
                        color: color,
                        startOffset: sectionOffset,
                        depth: depth,
                        textureScale: TextureScale * Scale);
                }

                foreach (var decorativeSprite in Prefab.DecorativeSprites)
                {
                    if (!spriteAnimState[decorativeSprite].IsActive) { continue; }
                    float rotation = decorativeSprite.GetRotation(ref spriteAnimState[decorativeSprite].RotationState, spriteAnimState[decorativeSprite].RandomRotationFactor);
                    Vector2 offset = decorativeSprite.GetOffset(ref spriteAnimState[decorativeSprite].OffsetState, spriteAnimState[decorativeSprite].RandomOffsetMultiplier) * Scale;
                    decorativeSprite.Sprite.Draw(spriteBatch, new Vector2(DrawPosition.X + offset.X, -(DrawPosition.Y + offset.Y)), color,
                        rotation, decorativeSprite.GetScale(spriteAnimState[decorativeSprite].RandomScaleFactor) * Scale, Prefab.Sprite.effects,
                        depth: Math.Min(depth + (decorativeSprite.Sprite.Depth - Prefab.Sprite.Depth), 0.999f));
                }
                Prefab.Sprite.effects = oldEffects;
            }

            if (GameMain.DebugDraw && Screen.Selected.Cam.Zoom > 0.5f)
            {
                if (Bodies != null)
                {
                    for (int i = 0; i < Bodies.Count; i++)
                    {
                        Vector2 pos = FarseerPhysics.ConvertUnits.ToDisplayUnits(Bodies[i].Position);
                        if (Submarine != null) { pos += Submarine.DrawPosition; }
                        pos.Y = -pos.Y;
                        GUI.DrawRectangle(spriteBatch,
                            pos,
                            FarseerPhysics.ConvertUnits.ToDisplayUnits(bodyDebugDimensions[i].X),
                            FarseerPhysics.ConvertUnits.ToDisplayUnits(bodyDebugDimensions[i].Y),
                            -Bodies[i].Rotation, Color.White);
                    }
                }

                if (SectionCount > 0 && HasBody)
                {
                    for (int i = 0; i < SectionCount; i++)
                    {
                        if (GetSection(i).damage > 0)
                        {
                            var textPos = SectionPosition(i, true);
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
            if (!string.IsNullOrEmpty(conditional.TargetItemComponentName))
            {
                return false;
            }
            else
            {
                if (!conditional.Matches(this)) { return false; }
            }
            return true;
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            byte sectionCount = msg.ReadByte();

            bool invalidMessage = false;
            if (type != ServerNetObject.ENTITY_EVENT && type != ServerNetObject.ENTITY_EVENT_INITIAL)
            {
                DebugConsole.NewMessage($"Error while reading a network event for the structure \"{Name} ({ID})\". Invalid event type ({type}).", Color.Red);
                return;
            }
            else if (sectionCount != Sections.Length)
            {
                invalidMessage = true;
                string errorMsg = $"Error while reading a network event for the structure \"{Name} ({ID})\". Section count does not match (server: {sectionCount} client: {Sections.Length})";
                DebugConsole.NewMessage(errorMsg, Color.Red);
                GameAnalyticsManager.AddErrorEventOnce("Structure.ClientRead:SectionCountMismatch", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
            }

            for (int i = 0; i < sectionCount; i++)
            {
                float damage = msg.ReadRangedSingle(0.0f, 1.0f, 8) * MaxHealth;
                if (!invalidMessage && i < Sections.Length)
                {
                    SetDamage(i, damage);
                }
            }
        }
    }
}
