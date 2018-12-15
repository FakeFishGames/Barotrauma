using Barotrauma.Lights;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class WallSection
    {
        public ConvexHull hull;
    }

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

        protected Vector2 textureScale = Vector2.One;
        [Editable, Serialize("1.0, 1.0", true)]
        public Vector2 TextureScale
        {
            get { return textureScale; }
            set
            {
                textureScale = new Vector2(
                    MathHelper.Clamp(value.X, 0.01f, 10),
                    MathHelper.Clamp(value.Y, 0.01f, 10));
            }
        }
        
        protected Vector2 textureOffset = Vector2.Zero;
        [Editable, Serialize("0.0, 0.0", true)]
        public Vector2 TextureOffset
        {
            get { return textureOffset; }
            set { textureOffset = value; }
        }

        private void GenerateConvexHull()
        {
            // If not null and not empty , remove the hulls from the system
            if (convexHulls != null && convexHulls.Any())
                convexHulls.ForEach(x => x.Remove());

            // list all of hulls for this structure
            convexHulls = new List<ConvexHull>();

            var mergedSections = new List<WallSection>();
            foreach (var section in Sections)
            {
                if (mergedSections.Count > 5)
                {
                    int width = IsHorizontal ? section.rect.Width : (int)BodyWidth;
                    int height = IsHorizontal ? (int)BodyHeight : section.rect.Height;
                    mergedSections.Add(new WallSection(new Rectangle(
                        section.rect.Center.X - width / 2,
                        section.rect.Y - section.rect.Height / 2 + height / 2,
                        width, height)));

                    GenerateMergedHull(mergedSections);
                    continue;
                }

                // if there is a gap and we have sections to merge, do it.
                if (section.gap != null)
                {
                    GenerateMergedHull(mergedSections);
                }
                else
                {
                    int width = IsHorizontal ? section.rect.Width : (int)BodyWidth;
                    int height = IsHorizontal ? (int)BodyHeight : section.rect.Height;
                    mergedSections.Add(new WallSection(new Rectangle(
                        section.rect.Center.X - width / 2,
                        section.rect.Y - section.rect.Height / 2 + height / 2,
                        width, height)));
                }
            }

            // take care of any leftover pieces
            if (mergedSections.Count > 0)
            {
                GenerateMergedHull(mergedSections);
            }
        }

        private void GenerateMergedHull(List<WallSection> mergedSections)
        {
            if (!mergedSections.Any()) return;
            Rectangle mergedRect = GenerateMergedRect(mergedSections);

            var h = new ConvexHull(CalculateExtremes(mergedRect), Color.Black, this);

            if (prefab.BodyRotation != 0.0f)
            {
                float rotation = MathHelper.ToRadians(prefab.BodyRotation);
                if (FlippedX != FlippedY) rotation = -rotation;
                h.Rotate(Position, -rotation);
            }

            mergedSections.ForEach(x => x.hull = h);
            convexHulls.Add(h);
            mergedSections.Clear();
        }

        public override void UpdateEditing(Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData as Structure != this)
            {
                editingHUD = CreateEditingHUD(Screen.Selected != GameMain.SubEditorScreen);
            }            
        }

        private GUIComponent CreateEditingHUD(bool inGame = false)
        {
            editingHUD = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GUI.Canvas, Anchor.CenterRight) { MinSize = new Point(400, 0) }) { UserData = this };
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(0.95f, 0.8f), editingHUD.RectTransform, Anchor.Center), style: null);
            var editor = new SerializableEntityEditor(listBox.Content.RectTransform, this, inGame, showName: true, elementHeight: 20);
            var reloadTextureButton = new GUIButton(new RectTransform(new Point(editingHUD.Rect.Width / 2, 20)), "Reload Texture");
            reloadTextureButton.OnClicked += (button, data) =>
            {
                Sprite.ReloadTexture();
                return true;
            };
            editor.AddCustomContent(reloadTextureButton, 6);

            PositionEditingHUD();

            return editingHUD;
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

            Color color = (isHighlighted) ? Color.Orange : spriteColor;
            if (IsSelected && editing)
            {
                color = Color.Lerp(color, Color.Gold, 0.5f);

                Vector2 rectSize = rect.Size.ToVector2();
                if (BodyWidth > 0.0f) { rectSize.X = BodyWidth; }
                if (BodyHeight > 0.0f) { rectSize.Y = BodyHeight; }

                Vector2 bodyPos = WorldPosition + BodyOffset;

                GUI.DrawRectangle(spriteBatch, new Vector2(bodyPos.X, -bodyPos.Y), rectSize.X, rectSize.Y, BodyRotation, color);
            }

            Vector2 drawOffset = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;

            float depth = SpriteDepthOverrideIsSet ? SpriteOverrideDepth : prefab.sprite.Depth;
            depth -= (ID % 255) * 0.000001f;

            Vector2 textureOffset = this.textureOffset;
            if (FlippedX) textureOffset.X = -textureOffset.X;
            if (FlippedY) textureOffset.Y = -textureOffset.Y;
            
            if (back && damageEffect == null)
            {
                if (prefab.BackgroundSprite != null)
                {
                    SpriteEffects oldEffects = prefab.BackgroundSprite.effects;
                    prefab.BackgroundSprite.effects ^= SpriteEffects;

                    Point backGroundOffset = new Point(
                        MathUtils.PositiveModulo((int)-textureOffset.X, prefab.BackgroundSprite.SourceRect.Width),
                        MathUtils.PositiveModulo((int)-textureOffset.Y, prefab.BackgroundSprite.SourceRect.Height));

                    prefab.BackgroundSprite.DrawTiled(
                        spriteBatch,
                        new Vector2(rect.X + drawOffset.X, -(rect.Y + drawOffset.Y)),
                        new Vector2(rect.Width, rect.Height),
                        color: color,
                        textureScale: TextureScale,
                        startOffset: backGroundOffset,
                        scaleMultiplier: Scale);

                    prefab.BackgroundSprite.effects = oldEffects;
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
                        float newCutoff = Sections[i].damage > 0 ? 
                            MathHelper.Lerp(0.2f, 0.65f, Sections[i].damage / prefab.Health) : 0.0f;

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
                        textureScale: TextureScale,
                        scaleMultiplier: Scale);
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
            }
        }
    }
}
