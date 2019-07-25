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

namespace Barotrauma
{
    partial class Structure : MapEntity, IDamageable, IServerSerializable
    {
        public static bool ShowWalls = true, ShowStructures = true;        

        private List<ConvexHull> convexHulls;

        public override bool SelectableInEditor
        {
            get
            {
                return HasBody ? ShowWalls : ShowStructures;;
            }
        }
        
        private string specialTag;
        [Editable, Serialize("", true)]
        public string SpecialTag
        {
            get { return specialTag; }
            set { specialTag = value; }
        }        

        partial void InitProjSpecific()
        {
            Prefab.sprite?.EnsureLazyLoaded();
            Prefab.BackgroundSprite?.EnsureLazyLoaded();
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

        public override void UpdateEditing(Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData as Structure != this)
            {
                editingHUD = CreateEditingHUD(Screen.Selected != GameMain.SubEditorScreen);
            }            
        }

        public GUIComponent CreateEditingHUD(bool inGame = false)
        {
            int heightScaled = (int)(20 * GUI.Scale);
            editingHUD = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GUI.Canvas, Anchor.CenterRight) { MinSize = new Point(400, 0) }) { UserData = this };
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(0.95f, 0.8f), editingHUD.RectTransform, Anchor.Center), style: null);
            var editor = new SerializableEntityEditor(listBox.Content.RectTransform, this, inGame, showName: true);
            
            var buttonContainer = new GUILayoutGroup(new RectTransform(new Point(listBox.Content.Rect.Width, heightScaled)), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("MirrorEntityX"))
            {
                ToolTip = TextManager.Get("MirrorEntityXToolTip"),
                OnClicked = (button, data) =>
                {
                    FlipX(relativeToSub: false);
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("MirrorEntityY"))
            {
                ToolTip = TextManager.Get("MirrorEntityYToolTip"),
                OnClicked = (button, data) =>
                {
                    FlipY(relativeToSub: false);
                    return true;
                }
            };
            var reloadTextureButton = new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("ReloadSprite"))
            {
                OnClicked = (button, data) =>
                {
                    Sprite.ReloadXML();
                    Sprite.ReloadTexture();
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("ResetToPrefab"))
            {
                OnClicked = (button, data) =>
                {
                    Reset();
                    CreateEditingHUD();
                    return true;
                }
            };
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

            if (worldRect.X > worldView.Right || worldRect.Right < worldView.X) return false;
            if (worldRect.Y < worldView.Y - worldView.Height || worldRect.Y - worldRect.Height > worldView.Y) return false;

            return true;
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (prefab.sprite == null) return;
            if (editing)
            {
                if (!HasBody && !ShowStructures) return;
                if (HasBody && !ShowWalls) return;
            }

            Draw(spriteBatch, editing, back, null);
        }

        public override void DrawDamage(SpriteBatch spriteBatch, Effect damageEffect)
        {
            Draw(spriteBatch, false, false, damageEffect);
        }

        private void Draw(SpriteBatch spriteBatch, bool editing, bool back = true, Effect damageEffect = null)
        {
            if (prefab.sprite == null) return;
            if (editing)
            {
                if (!HasBody && !ShowStructures) return;
                if (HasBody && !ShowWalls) return;
            }

            Color color = IsHighlighted ? Color.Orange : spriteColor;
            if (IsSelected && editing)
            {
                //color = Color.Lerp(color, Color.Gold, 0.5f);
                color = spriteColor;

                Vector2 rectSize = rect.Size.ToVector2();
                if (BodyWidth > 0.0f) { rectSize.X = BodyWidth; }
                if (BodyHeight > 0.0f) { rectSize.Y = BodyHeight; }

                Vector2 bodyPos = WorldPosition + BodyOffset;

                GUI.DrawRectangle(spriteBatch, new Vector2(bodyPos.X, -bodyPos.Y), rectSize.X, rectSize.Y, BodyRotation, Color.White, 
                    thickness: Math.Max(1, (int)(2 / Screen.Selected.Cam.Zoom)));
            }

            Vector2 drawOffset = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;

            float depth = SpriteDepthOverrideIsSet ? SpriteOverrideDepth : prefab.sprite.Depth;
            depth -= (ID % 255) * 0.000001f;

            Vector2 textureOffset = this.textureOffset;
            if (FlippedX) textureOffset.X = -textureOffset.X;
            if (FlippedY) textureOffset.Y = -textureOffset.Y;

            if (back && damageEffect == null)
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

                    Point backGroundOffset = new Point(
                        MathUtils.PositiveModulo((int)-textureOffset.X, Prefab.BackgroundSprite.SourceRect.Width),
                        MathUtils.PositiveModulo((int)-textureOffset.Y, Prefab.BackgroundSprite.SourceRect.Height));

                    Prefab.BackgroundSprite.DrawTiled(
                        spriteBatch,
                        new Vector2(rect.X + drawOffset.X, -(rect.Y + drawOffset.Y)),
                        new Vector2(rect.Width, rect.Height),
                        color: color,
                        textureScale: TextureScale * Scale,
                        startOffset: backGroundOffset,
                        depth: Math.Max(Prefab.BackgroundSprite.Depth, depth + 0.000001f));

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

            if (back == depth > 0.5f || editing)
            {
                SpriteEffects oldEffects = prefab.sprite.effects;
                prefab.sprite.effects ^= SpriteEffects;

                for (int i = 0; i < Sections.Length; i++)
                {
                    if (damageEffect != null)
                    {
                        float newCutoff = MathHelper.Lerp(0.0f, 0.65f, Sections[i].damage / Prefab.Health);

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
                    
                    Point sectionOffset = new Point(
                        Math.Abs(rect.Location.X - Sections[i].rect.Location.X),
                        Math.Abs(rect.Location.Y - Sections[i].rect.Location.Y));

                    if (FlippedX && IsHorizontal) sectionOffset.X = Sections[i].rect.Right - rect.Right;
                    if (FlippedY && !IsHorizontal) sectionOffset.Y = (rect.Y - rect.Height) - (Sections[i].rect.Y - Sections[i].rect.Height);

                    sectionOffset.X += MathUtils.PositiveModulo((int)-textureOffset.X, prefab.sprite.SourceRect.Width);
                    sectionOffset.Y += MathUtils.PositiveModulo((int)-textureOffset.Y, prefab.sprite.SourceRect.Height);

                    prefab.sprite.DrawTiled(
                        spriteBatch,
                        new Vector2(Sections[i].rect.X + drawOffset.X, -(Sections[i].rect.Y + drawOffset.Y)),
                        new Vector2(Sections[i].rect.Width, Sections[i].rect.Height),
                        color: color,
                        startOffset: sectionOffset,
                        depth: depth,
                        textureScale: TextureScale * Scale);
                }
                prefab.sprite.effects = oldEffects;
            }

            if (GameMain.DebugDraw)
            {
                if (Bodies != null)
                {
                    for (int i = 0; i < Bodies.Count; i++)
                    {
                        Vector2 pos = FarseerPhysics.ConvertUnits.ToDisplayUnits(Bodies[i].Position);
                        if (Submarine != null) pos += Submarine.Position;
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
                            GUI.DrawString(spriteBatch, textPos, "Damage: " + (int)((GetSection(i).damage / Health) * 100f) + "%", Color.Yellow);
                        }
                    }
                }

                AiTarget?.Draw(spriteBatch);
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            for (int i = 0; i < Sections.Length; i++)
            {
                float damage = msg.ReadRangedSingle(0.0f, 1.0f, 8) * Health;
                SetDamage(i, damage);
            }
        }
    }
}
