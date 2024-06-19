using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.MapCreatures.Behavior;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    partial class Item : MapEntity, IDamageable, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        public static bool ShowItems = true, ShowWires = true;

        private readonly List<PosInfo> positionBuffer = new List<PosInfo>();

        private readonly List<ItemComponent> activeHUDs = new List<ItemComponent>();

        private readonly List<SerializableEntityEditor> activeEditors = new List<SerializableEntityEditor>();


        private GUIComponentStyle iconStyle;
        public GUIComponentStyle IconStyle 
        { 
            get { return iconStyle; }
            private set
            {
                if (IconStyle != value)
                {
                    iconStyle = value;
                    CheckIsHighlighted();
                }
            }
        }

        partial void AssignCampaignInteractionTypeProjSpecific(CampaignMode.InteractionType interactionType, IEnumerable<Client> targetClients)
        {
            if (interactionType == CampaignMode.InteractionType.None)
            {
                IconStyle = null;
            }
            else
            {
                IconStyle = GUIStyle.GetComponentStyle($"CampaignInteractionIcon.{interactionType}");
            }
        }

        public IEnumerable<ItemComponent> ActiveHUDs => activeHUDs;

        public float LastImpactSoundTime;
        public const float ImpactSoundInterval = 0.2f;

        private float editingHUDRefreshTimer;

        private ContainedItemSprite activeContainedSprite;

        private readonly Dictionary<DecorativeSprite, DecorativeSprite.State> spriteAnimState = new Dictionary<DecorativeSprite, DecorativeSprite.State>();

        public float DrawDepthOffset;

        private bool fakeBroken;
        public bool FakeBroken
        {
            get { return fakeBroken; }
            set
            {
                if (value != fakeBroken)
                {
                    fakeBroken = value;
                    SetActiveSprite();
                }
            }
        }

        private Sprite activeSprite;
        public override Sprite Sprite
        {
            get { return activeSprite; }
        }

        public override Rectangle Rect
        {
            get { return base.Rect; }
            set
            {
                cachedVisibleExtents = null;
                base.Rect = value;
            }
        }

        public override bool DrawBelowWater => (!(Screen.Selected is SubEditorScreen editor) || !editor.WiringMode || !isWire || !isLogic) && (base.DrawBelowWater || ParentInventory is CharacterInventory);

        public override bool DrawOverWater => base.DrawOverWater || (IsSelected || Screen.Selected is SubEditorScreen editor && editor.WiringMode) && (isWire || isLogic);

        private GUITextBlock itemInUseWarning;
        private GUITextBlock ItemInUseWarning
        {
            get
            {
                if (itemInUseWarning == null)
                {
                    itemInUseWarning = new GUITextBlock(new RectTransform(new Point(10), GUI.Canvas), "",
                        textColor: GUIStyle.Orange, color: Color.Black,
                        textAlignment: Alignment.Center, style: "OuterGlow");
                }
                return itemInUseWarning;
            }
        }

        public override bool SelectableInEditor
        {
            get
            {
                if (GameMain.SubEditorScreen.IsSubcategoryHidden(Prefab.Subcategory))
                {
                    return false;
                }

                if (!SubEditorScreen.IsLayerVisible(this)) { return false;}

                return parentInventory == null && (body == null || body.Enabled) && ShowItems;
            }
        }

        public float GetDrawDepth()
        {
            return GetDrawDepth(SpriteDepth + DrawDepthOffset, Sprite);
        }

        public Color GetSpriteColor(Color? defaultColor = null, bool withHighlight = false)
        {
            Color color = defaultColor ?? spriteColor;
            if (Prefab.UseContainedSpriteColor && ownInventory != null)
            {
                foreach (Item item in ContainedItems)
                {
                    color = item.ContainerColor;
                    break;
                }
            }
            if (withHighlight)
            {
                if (IsHighlighted && !GUI.DisableItemHighlights && Screen.Selected != GameMain.GameScreen)
                {
                    color = GUIStyle.Orange * Math.Max(GetSpriteColor().A / (float)byte.MaxValue, 0.1f);
                }
                else if (IsHighlighted && HighlightColor.HasValue)
                {
                    color = Color.Lerp(color, HighlightColor.Value, (MathF.Sin((float)Timing.TotalTime * 3.0f) + 1.0f) / 2.0f);
                }
            }
            return color;
        }

        protected override void CheckIsHighlighted()
        {
            if (IsHighlighted || ExternalHighlight || IconStyle != null)
            {
                highlightedEntities.Add(this);
            }
            else
            {
                highlightedEntities.Remove(this);
            }
        }

        public Color GetInventoryIconColor()
        {
            Color color = InventoryIconColor;
            if (Prefab.UseContainedInventoryIconColor && ownInventory != null)
            {
                foreach (Item item in ContainedItems)
                {
                    color = item.ContainerColor;
                    break;
                }
            }
            return color;
        }

        partial void SetActiveSpriteProjSpecific()
        {
            activeSprite = Prefab.Sprite;
            activeContainedSprite = null;
            Holdable holdable = GetComponent<Holdable>();
            if (holdable != null && holdable.Attached)
            {
                foreach (ContainedItemSprite containedSprite in Prefab.ContainedSprites)
                {
                    if (containedSprite.UseWhenAttached)
                    {
                        activeContainedSprite = containedSprite;
                        activeSprite = containedSprite.Sprite;
                        UpdateSpriteStates(0.0f);
                        return;
                    }
                }
            }

            if (Container != null)
            {
                foreach (ContainedItemSprite containedSprite in Prefab.ContainedSprites)
                {
                    if (containedSprite.MatchesContainer(Container))
                    {
                        activeContainedSprite = containedSprite;
                        activeSprite = containedSprite.Sprite;
                        UpdateSpriteStates(0.0f);
                        return;
                    }
                }
            }

            float displayCondition = FakeBroken ? 0.0f : ConditionPercentageRelativeToDefaultMaxCondition;
            for (int i = 0; i < Prefab.BrokenSprites.Length;i++)
            {
                if (Prefab.BrokenSprites[i].FadeIn) { continue; }
                float minCondition = i > 0 ? Prefab.BrokenSprites[i - i].MaxConditionPercentage : 0.0f;
                if (displayCondition <= minCondition || displayCondition <= Prefab.BrokenSprites[i].MaxConditionPercentage)
                {
                    activeSprite = Prefab.BrokenSprites[i].Sprite;
                    break;
                }
            }
        }

        public void InitSpriteStates()
        {
            Prefab.Sprite?.EnsureLazyLoaded();
            Prefab.InventoryIcon?.EnsureLazyLoaded();
            foreach (BrokenItemSprite brokenSprite in Prefab.BrokenSprites)
            {
                brokenSprite.Sprite.EnsureLazyLoaded();
            }
            foreach (var decorativeSprite in Prefab.DecorativeSprites)
            {
                decorativeSprite.Sprite.EnsureLazyLoaded();
                spriteAnimState.Add(decorativeSprite, new DecorativeSprite.State());
            }
            SetActiveSprite();
            UpdateSpriteStates(0.0f);
        }

        partial void InitProjSpecific()
        {
            InitSpriteStates();
        }

        private Rectangle? cachedVisibleExtents;

        public void ResetCachedVisibleSize()
        {
            cachedVisibleExtents = null;
        }

        public override bool IsVisible(Rectangle worldView)
        {
            // Inside of a container
            if (container != null)
            {
                return false;
            }

            //no drawable components and the body has been disabled = nothing to draw
            if (!hasComponentsToDraw && body != null && !body.Enabled)
            {
                return false;
            }

            if (parentInventory?.Owner is Character character && character.InvisibleTimer > 0.0f)
            {
                return false;
            }

            Rectangle extents;
            if (cachedVisibleExtents.HasValue)
            {
                extents = cachedVisibleExtents.Value;
            }
            else
            {
                int padding = 100;

                RectangleF boundingBox = GetTransformedQuad().BoundingAxisAlignedRectangle;
                Vector2 min = new Vector2(-boundingBox.Width / 2 - padding, -boundingBox.Height / 2 - padding);
                Vector2 max = -min;

                foreach (IDrawableComponent drawable in drawableComponents)
                {
                    min.X = Math.Min(min.X, -drawable.DrawSize.X / 2);
                    min.Y = Math.Min(min.Y, -drawable.DrawSize.Y / 2);
                    max.X = Math.Max(max.X, drawable.DrawSize.X / 2);
                    max.Y = Math.Max(max.Y, drawable.DrawSize.Y / 2);
                }
                foreach (DecorativeSprite decorativeSprite in Prefab.DecorativeSprites)
                {
                    float scale = decorativeSprite.GetScale(spriteAnimState[decorativeSprite].RandomScaleFactor) * Scale;
                    min.X = Math.Min(-decorativeSprite.Sprite.size.X * decorativeSprite.Sprite.RelativeOrigin.X * scale, min.X);
                    min.Y = Math.Min(-decorativeSprite.Sprite.size.Y * (1.0f - decorativeSprite.Sprite.RelativeOrigin.Y) * scale, min.Y);
                    max.X = Math.Max(decorativeSprite.Sprite.size.X * (1.0f - decorativeSprite.Sprite.RelativeOrigin.X) * scale, max.X);
                    max.Y = Math.Max(decorativeSprite.Sprite.size.Y * decorativeSprite.Sprite.RelativeOrigin.Y * scale, max.Y);
                }
                cachedVisibleExtents = extents = new Rectangle(min.ToPoint(), max.ToPoint());
            }

            Vector2 worldPosition = WorldPosition + GetCollapseEffectOffset();

            if (worldPosition.X + extents.X > worldView.Right || worldPosition.X + extents.Width < worldView.X) { return false; }
            if (worldPosition.Y + extents.Height < worldView.Y - worldView.Height || worldPosition.Y + extents.Y > worldView.Y) { return false; }

            return true;
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            Draw(spriteBatch, editing, back, overrideColor: null);
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, bool back = true, Color? overrideColor = null)
        {
            if (!Visible || (!editing && IsHidden) || !SubEditorScreen.IsLayerVisible(this)) { return; }

            if (editing)
            {
                if (isWire)
                {
                    if (!ShowWires) { return; }
                }
                else if (!ShowItems) { return; }
            }

            Color color = GetSpriteColor(spriteColor);

            bool isWiringMode = editing && SubEditorScreen.TransparentWiringMode && SubEditorScreen.IsWiringMode() && !isWire && parentInventory == null;
            bool renderTransparent = isWiringMode && GetComponent<ConnectionPanel>() == null;
            if (renderTransparent) { color *= 0.15f; }

            if (Character.Controlled != null && Character.DebugDrawInteract)
            {
                color = Color.Red;
                foreach (var ic in components)
                {
                    var interactionType = GetComponentInteractionVisibility(Character.Controlled, ic);
                    if (interactionType == InteractionVisibility.MissingRequirement)
                    {
                        color = Color.Orange;
                    }
                    else if (interactionType == InteractionVisibility.Visible)
                    {
                        color = Color.LightGreen;
                        break;
                    }
                }
            }

            BrokenItemSprite fadeInBrokenSprite = null;
            float fadeInBrokenSpriteAlpha = 0.0f;

            float displayCondition = FakeBroken ? 0.0f : ConditionPercentageRelativeToDefaultMaxCondition;
            Vector2 drawOffset = GetCollapseEffectOffset();
            drawOffset.Y = -drawOffset.Y;            

            if (displayCondition < MaxCondition)
            {
                for (int i = 0; i < Prefab.BrokenSprites.Length; i++)
                {
                    if (Prefab.BrokenSprites[i].FadeIn)
                    {
                        float min = i > 0 ? Prefab.BrokenSprites[i - i].MaxConditionPercentage : 0.0f;
                        float max = Prefab.BrokenSprites[i].MaxConditionPercentage;
                        fadeInBrokenSpriteAlpha = 1.0f - ((displayCondition - min) / (max - min));
                        if (fadeInBrokenSpriteAlpha > 0.0f && fadeInBrokenSpriteAlpha <= 1.0f)
                        {
                            fadeInBrokenSprite = Prefab.BrokenSprites[i];
                        }
                        continue;
                    }
                    if (displayCondition <= Prefab.BrokenSprites[i].MaxConditionPercentage)
                    {
                        activeSprite = Prefab.BrokenSprites[i].Sprite;
                        drawOffset = Prefab.BrokenSprites[i].Offset.ToVector2() * Scale;
                        break;
                    }
                }
            }

            float depth = GetDrawDepth();
            if (isWiringMode && isLogic && !PlayerInput.IsShiftDown()) { depth = 0.01f; }
            if (activeSprite != null)
            {
                SpriteEffects oldEffects = activeSprite.effects;
                activeSprite.effects ^= SpriteEffects;
                SpriteEffects oldBrokenSpriteEffects = SpriteEffects.None;
                if (fadeInBrokenSprite != null && fadeInBrokenSprite.Sprite != activeSprite)
                {
                    oldBrokenSpriteEffects = fadeInBrokenSprite.Sprite.effects;
                    fadeInBrokenSprite.Sprite.effects ^= SpriteEffects;
                }

                if (body == null)
                {
                    if (Prefab.ResizeHorizontal || Prefab.ResizeVertical)
                    {
                        if (color.A > 0)
                        {
                            Vector2 size = new Vector2(rect.Width, rect.Height);
                            activeSprite.DrawTiled(spriteBatch, new Vector2(DrawPosition.X - rect.Width / 2, -(DrawPosition.Y + rect.Height / 2)) + drawOffset,
                                size, color: color,
                                textureScale: Vector2.One * Scale,
                                depth: depth);

                            if (fadeInBrokenSprite != null)
                            {
                                float d = Math.Min(depth + (fadeInBrokenSprite.Sprite.Depth - activeSprite.Depth - 0.000001f), 0.999f);
                                fadeInBrokenSprite.Sprite.DrawTiled(spriteBatch, new Vector2(DrawPosition.X - rect.Width / 2, -(DrawPosition.Y + rect.Height / 2)) + fadeInBrokenSprite.Offset.ToVector2() * Scale, size, color: color * fadeInBrokenSpriteAlpha,
                                    textureScale: Vector2.One * Scale,
                                    depth: d);
                            }
                            DrawDecorativeSprites(spriteBatch, DrawPosition, flippedX && Prefab.CanSpriteFlipX, flippedY && Prefab.CanSpriteFlipY, rotation: 0, depth, overrideColor);
                        }
                    }
                    else
                    {
                        Vector2 origin = GetSpriteOrigin(activeSprite);
                        if (color.A > 0)
                        {
                            activeSprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y) + drawOffset, color, origin, RotationRad, Scale, activeSprite.effects, depth);
                            if (fadeInBrokenSprite != null)
                            {
                                float d = Math.Min(depth + (fadeInBrokenSprite.Sprite.Depth - activeSprite.Depth - 0.000001f), 0.999f);
                                fadeInBrokenSprite.Sprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y) + fadeInBrokenSprite.Offset.ToVector2() * Scale, color * fadeInBrokenSpriteAlpha, origin, RotationRad, Scale, activeSprite.effects, d);
                            }
                        }
                        if (Infector != null && (Infector.ParentBallastFlora.HasBrokenThrough || BallastFloraBehavior.AlwaysShowBallastFloraSprite))
                        {
                            Prefab.InfectedSprite?.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y) + drawOffset, color, Prefab.InfectedSprite.Origin, RotationRad, Scale, activeSprite.effects, depth - 0.001f);
                            Prefab.DamagedInfectedSprite?.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y) + drawOffset, Infector.HealthColor, Prefab.DamagedInfectedSprite.Origin, RotationRad, Scale, activeSprite.effects, depth - 0.002f);
                        }

                        DrawDecorativeSprites(spriteBatch, DrawPosition, flippedX && Prefab.CanSpriteFlipX, flippedY && Prefab.CanSpriteFlipY, -RotationRad, depth, overrideColor);
                    }
                }
                else if (body.Enabled)
                {
                    var holdable = GetComponent<Holdable>();
                    if (holdable != null && holdable.Picker?.AnimController != null)
                    {
                        //don't draw the item on hands if it's also being worn
                        if (GetComponent<Wearable>() is { IsActive: true }) { return; }
                        if (!back) { return; }
                        if (holdable.Picker.Inventory?.GetItemInLimbSlot(InvSlotType.RightHand) == this)
                        {
                            depth = GetHeldItemDepth(LimbType.RightHand, holdable, depth);
                        }
                        else if (holdable.Picker.Inventory?.GetItemInLimbSlot(InvSlotType.LeftHand) == this)
                        {
                            depth = GetHeldItemDepth(LimbType.LeftHand, holdable, depth);
                        }

                        static float GetHeldItemDepth(LimbType limb, Holdable holdable, float depth)
                        {
                            if (holdable?.Picker?.AnimController == null) { return depth; }
                            //offset used to make sure the item draws just slightly behind the right hand, or slightly in front of the left hand
                            float limbDepthOffset = 0.000001f;
                            float depthOffset = holdable.Picker.AnimController.GetDepthOffset();
                            //use the upper arm as a reference, to ensure the item gets drawn behind / in front of the whole arm (not just the forearm)
                            Limb holdLimb = holdable.Picker.AnimController.GetLimb(limb == LimbType.RightHand ? LimbType.RightArm : LimbType.LeftArm);
                            if (holdLimb?.ActiveSprite != null)
                            {
                                depth = 
                                    holdLimb.ActiveSprite.Depth 
                                    + depthOffset 
                                    + limbDepthOffset * 2 * (limb == LimbType.RightHand ? 1 : -1);
                                foreach (WearableSprite wearableSprite in holdLimb.WearingItems)
                                {
                                    if (!wearableSprite.InheritLimbDepth && wearableSprite.Sprite != null) 
                                    { 
                                        depth =
                                            limb == LimbType.RightHand ?
                                                Math.Max(wearableSprite.Sprite.Depth + limbDepthOffset, depth) : 
                                                Math.Min(wearableSprite.Sprite.Depth - limbDepthOffset, depth);
                                    }
                                }
                                var head = holdable.Picker.AnimController.GetLimb(LimbType.Head);
                                if (head != null)
                                {
                                    //ensure the holdable item is always drawn in front of the head no matter what the wearables or whatnot do with the sprite depths
                                    depth =
                                        limb == LimbType.RightHand ?
                                            Math.Min(head.Sprite.Depth + depthOffset - limbDepthOffset, depth) :
                                            Math.Max(head.Sprite.Depth + depthOffset + limbDepthOffset, depth);
                                }
                            }
                            return depth;
                        }
                    }
                    Vector2 origin = GetSpriteOrigin(activeSprite);
                    body.Draw(spriteBatch, activeSprite, color, depth, Scale, origin: origin);
                    if (fadeInBrokenSprite != null)
                    {
                        float d = Math.Min(depth + (fadeInBrokenSprite.Sprite.Depth - activeSprite.Depth - 0.000001f), 0.999f);
                        body.Draw(spriteBatch, fadeInBrokenSprite.Sprite, color * fadeInBrokenSpriteAlpha, d, Scale);
                    }
                    DrawDecorativeSprites(spriteBatch, body.DrawPosition, flipX: body.Dir < 0, flipY: false, rotation: body.Rotation, depth, overrideColor);
                }

                foreach (var upgrade in Upgrades)
                {
                    var upgradeSprites = GetUpgradeSprites(upgrade);

                    foreach (var decorativeSprite in upgradeSprites)
                    {
                        if (!spriteAnimState[decorativeSprite].IsActive) { continue; }
                        float rotation = decorativeSprite.GetRotation(ref spriteAnimState[decorativeSprite].RotationState, spriteAnimState[decorativeSprite].RandomRotationFactor);
                        Vector2 offset = decorativeSprite.GetOffset(ref spriteAnimState[decorativeSprite].OffsetState, spriteAnimState[decorativeSprite].RandomOffsetMultiplier, -RotationRad) * Scale;
                        if (flippedX && Prefab.CanSpriteFlipX) { offset.X = -offset.X; }
                        if (flippedY && Prefab.CanSpriteFlipY) { offset.Y = -offset.Y; }
                        decorativeSprite.Sprite.Draw(spriteBatch, new Vector2(DrawPosition.X + offset.X, -(DrawPosition.Y + offset.Y)), color,
                            rotation, decorativeSprite.GetScale(spriteAnimState[decorativeSprite].RandomScaleFactor) * Scale, activeSprite.effects,
                            depth: depth + (decorativeSprite.Sprite.Depth - activeSprite.Depth));
                    }
                }

                activeSprite.effects = oldEffects;
                if (fadeInBrokenSprite != null && fadeInBrokenSprite.Sprite != activeSprite)
                {
                    fadeInBrokenSprite.Sprite.effects = oldBrokenSpriteEffects;
                }
            }

            //use a backwards for loop because the drawable components may disable drawing,
            //causing them to be removed from the list
            for (int i = drawableComponents.Count - 1; i >= 0; i--)
            {
                drawableComponents[i].Draw(spriteBatch, editing, depth, overrideColor);
            }

            if (GameMain.DebugDraw)
            {
                body?.DebugDraw(spriteBatch, Color.White);
                if (GetComponent<TriggerComponent>()?.PhysicsBody is PhysicsBody triggerBody)
                {
                    triggerBody.UpdateDrawPosition();
                    triggerBody.DebugDraw(spriteBatch, Color.White);
                }
            }

            if (editing && IsSelected && PlayerInput.KeyDown(Keys.Space))
            {
                if (GetComponent<ElectricalDischarger>() is { } discharger)
                {
                    discharger.DrawElectricity(spriteBatch);
                }
            }

            if (!editing || (body != null && !body.Enabled))
            {
                return;
            }

            if (IsSelected || IsHighlighted)
            {
                Vector2 drawPos = new Vector2(DrawPosition.X - rect.Width / 2, -(DrawPosition.Y + rect.Height / 2));
                Vector2 drawSize = new Vector2(MathF.Ceiling(rect.Width + Math.Abs(drawPos.X - (int)drawPos.X)), MathF.Ceiling(rect.Height + Math.Abs(drawPos.Y - (int)drawPos.Y)));
                drawPos = new Vector2(MathF.Floor(drawPos.X), MathF.Floor(drawPos.Y));
                GUI.DrawRectangle(sb: spriteBatch,
                    center: drawPos + drawSize * 0.5f,
                    width: drawSize.X,
                    height: drawSize.Y,
                    rotation: RotationRad,
                    clr: Color.White,
                    depth: 0,
                    thickness: Math.Max(2f / Screen.Selected.Cam.Zoom, 1));

                foreach (Rectangle t in Prefab.Triggers)
                {
                    Rectangle transformedTrigger = TransformTrigger(t);

                    Vector2 rectWorldPos = new Vector2(transformedTrigger.X, transformedTrigger.Y);
                    if (Submarine != null) rectWorldPos += Submarine.Position;
                    rectWorldPos.Y = -rectWorldPos.Y;

                    GUI.DrawRectangle(spriteBatch,
                        rectWorldPos,
                        new Vector2(transformedTrigger.Width, transformedTrigger.Height),
                        GUIStyle.Green,
                        false,
                        0,
                        (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
                }
            }

            if (!ShowLinks || GUI.DisableHUD) { return; }

            foreach (MapEntity e in linkedTo)
            {
                bool isLinkAllowed = Prefab.IsLinkAllowed(e.Prefab);
                Color lineColor = GUIStyle.Red * 0.5f;
                if (isLinkAllowed)
                {
                    lineColor = e is Item i && (DisplaySideBySideWhenLinked || i.DisplaySideBySideWhenLinked) ? Color.Purple * 0.5f : Color.LightGreen * 0.5f;
                }
                Vector2 from = new Vector2(WorldPosition.X, -WorldPosition.Y);
                Vector2 to = new Vector2(e.WorldPosition.X, -e.WorldPosition.Y);
                GUI.DrawLine(spriteBatch, from, to, lineColor * 0.25f, width: 3);
                GUI.DrawLine(spriteBatch, from, to, lineColor, width: 1);
                //GUI.DrawString(spriteBatch, from, $"Linked to {e.Name}", lineColor, Color.Black * 0.5f);
            }

            Vector2 GetSpriteOrigin(Sprite sprite)
            {
                Vector2 origin = sprite.Origin;
                if ((sprite.effects & SpriteEffects.FlipHorizontally) == SpriteEffects.FlipHorizontally)
                {
                    origin.X = sprite.SourceRect.Width - origin.X;
                }
                if ((sprite.effects & SpriteEffects.FlipVertically) == SpriteEffects.FlipVertically)
                {
                    origin.Y = sprite.SourceRect.Height - origin.Y;
                }
                return origin;
            }

            Color GetSpriteColor(Color defaultColor)
            {
                return
                    overrideColor ??
                    (IsIncludedInSelection && editing ? GUIStyle.Blue : this.GetSpriteColor(defaultColor: defaultColor, withHighlight: true));
            }
        }

        public void DrawDecorativeSprites(SpriteBatch spriteBatch, Vector2 drawPos, bool flipX, bool flipY, float rotation, float depth, Color? overrideColor = null)
        {
            foreach (var decorativeSprite in Prefab.DecorativeSprites)
            {
                Color decorativeSpriteColor = overrideColor ?? GetSpriteColor(decorativeSprite.Color).Multiply(GetSpriteColor(spriteColor));
                if (!spriteAnimState[decorativeSprite].IsActive) { continue; }

                Vector2 offset = decorativeSprite.GetOffset(ref spriteAnimState[decorativeSprite].OffsetState, spriteAnimState[decorativeSprite].RandomOffsetMultiplier,
                    flipX ^ flipY ? -rotation : rotation) * Scale;

                if (ResizeHorizontal || ResizeVertical)
                {
                    decorativeSprite.Sprite.DrawTiled(spriteBatch,
                        new Vector2(DrawPosition.X + offset.X - rect.Width / 2, -(DrawPosition.Y + offset.Y + rect.Height / 2)),
                        new Vector2(rect.Width, rect.Height), color: decorativeSpriteColor,
                        textureScale: Vector2.One * Scale,
                        depth: Math.Min(depth + (decorativeSprite.Sprite.Depth - activeSprite.Depth), 0.999f));
                }
                else
                {
                    float spriteRotation = decorativeSprite.GetRotation(ref spriteAnimState[decorativeSprite].RotationState, spriteAnimState[decorativeSprite].RandomRotationFactor);
                    
                    Vector2 origin = decorativeSprite.Sprite.Origin;
                    SpriteEffects spriteEffects = SpriteEffects.None;
                    if (flipX && Prefab.CanSpriteFlipX) 
                    { 
                        offset.X = -offset.X;
                        origin.X = -origin.X + decorativeSprite.Sprite.size.X;
                        spriteEffects = SpriteEffects.FlipHorizontally;
                    }
                    if (flipY && Prefab.CanSpriteFlipY) 
                    { 
                        offset.Y = -offset.Y;
                        origin.Y = -origin.Y + decorativeSprite.Sprite.size.Y;
                        spriteEffects |= SpriteEffects.FlipVertically;
                    }
                    if (body != null)
                    {
                        var ca = MathF.Cos(-body.DrawRotation);
                        var sa = MathF.Sin(-body.DrawRotation);
                        offset = new Vector2(ca * offset.X + sa * offset.Y, -sa * offset.X + ca * offset.Y);
                    }
                    decorativeSprite.Sprite.Draw(spriteBatch, new Vector2(drawPos.X + offset.X, -(drawPos.Y + offset.Y)), decorativeSpriteColor, origin,
                        -rotation + spriteRotation, decorativeSprite.GetScale(spriteAnimState[decorativeSprite].RandomScaleFactor) * Scale, spriteEffects,
                        depth: depth + (decorativeSprite.Sprite.Depth - activeSprite.Depth));
                }
            }
        }

        partial void OnCollisionProjSpecific(float impact)
        {
            if (impact > 1.0f &&
                Container == null &&
                !string.IsNullOrEmpty(Prefab.ImpactSoundTag) &&
                Timing.TotalTime > LastImpactSoundTime + ImpactSoundInterval)
            {
                LastImpactSoundTime = (float)Timing.TotalTime;
                SoundPlayer.PlaySound(Prefab.ImpactSoundTag, WorldPosition, hullGuess: CurrentHull);
            }
        }

        partial void Splash()
        {
            if (body == null || CurrentHull == null) { return; }
            //create a splash particle
            float massFactor = MathHelper.Clamp(body.Mass, 0.5f, 20.0f);
            for (int i = 0; i < MathHelper.Clamp(Math.Abs(body.LinearVelocity.Y), 1.0f, 10.0f); i++)
            {
                var splash = GameMain.ParticleManager.CreateParticle("watersplash",
                    new Vector2(WorldPosition.X, CurrentHull.WorldSurface),
                    new Vector2(0.0f, Math.Abs(-body.LinearVelocity.Y * massFactor)) + Rand.Vector(Math.Abs(body.LinearVelocity.Y * 10)),
                    Rand.Range(0.0f, MathHelper.TwoPi), CurrentHull);
                if (splash != null)
                {
                    splash.Size *= MathHelper.Clamp(Math.Abs(body.LinearVelocity.Y) * 0.1f * massFactor, 1.0f, 4.0f);
                }
            }
            GameMain.ParticleManager.CreateParticle("bubbles",
                new Vector2(WorldPosition.X, CurrentHull.WorldSurface),
                body.LinearVelocity * massFactor,
                0.0f, CurrentHull);

            //create a wave
            if (body.LinearVelocity.Y < 0.0f)
            {
                int n = (int)((Position.X - CurrentHull.Rect.X) / Hull.WaveWidth);
                if (n >= 0 && n < currentHull.WaveVel.Length)
                {
                    CurrentHull.WaveVel[n] += MathHelper.Clamp(body.LinearVelocity.Y * massFactor, -5.0f, 5.0f);
                }
            }
            SoundPlayer.PlaySplashSound(WorldPosition, Math.Abs(body.LinearVelocity.Y) + Rand.Range(-10.0f, -5.0f));
        }

        public void CheckNeedsSoundUpdate(ItemComponent ic)
        {
            if (ic.NeedsSoundUpdate())
            {
                if (!updateableComponents.Contains(ic))
                {
                    updateableComponents.Add(ic);
                }
                isActive = true;
            }
        }

        public void UpdateSpriteStates(float deltaTime)
        {
            if (activeContainedSprite != null)
            {
                if (activeContainedSprite.DecorativeSpriteBehavior == ContainedItemSprite.DecorativeSpriteBehaviorType.HideWhenVisible)
                {
                    foreach (DecorativeSprite decorativeSprite in Prefab.DecorativeSprites)
                    {
                        var spriteState = spriteAnimState[decorativeSprite];
                        spriteState.IsActive = false;
                    }
                    return;
                }
            }
            else
            {
                foreach (var containedSprite in Prefab.ContainedSprites)
                {
                    if (containedSprite.Sprite != activeSprite && containedSprite.DecorativeSpriteBehavior == ContainedItemSprite.DecorativeSpriteBehaviorType.HideWhenNotVisible)
                    {
                        foreach (DecorativeSprite decorativeSprite in Prefab.DecorativeSprites)
                        {
                            var spriteState = spriteAnimState[decorativeSprite];
                            spriteState.IsActive = false;
                        }
                        return;
                    }
                }
            }

            if (Prefab.DecorativeSpriteGroups.Count > 0)
            {
                DecorativeSprite.UpdateSpriteStates(Prefab.DecorativeSpriteGroups, spriteAnimState, ID, deltaTime, ConditionalMatches);
            }

            foreach (var upgrade in Upgrades)
            {
                var upgradeSprites = GetUpgradeSprites(upgrade);
                foreach (var decorativeSprite in upgradeSprites)
                {
                    var spriteState = spriteAnimState[decorativeSprite];
                    spriteState.IsActive = true;
                    foreach (var conditional in decorativeSprite.IsActiveConditionals)
                    {
                        if (!ConditionalMatches(conditional))
                        {
                            spriteState.IsActive = false;
                            break;
                        }
                    }
                }
            }
        }

        public override void UpdateEditing(Camera cam, float deltaTime)
        {
            if (editingHUD == null || editingHUD.UserData as Item != this)
            {
                editingHUD = CreateEditingHUD(Screen.Selected != GameMain.SubEditorScreen);
                editingHUDRefreshTimer = 1.0f;
            }
            if (editingHUDRefreshTimer <= 0.0f)
            {
                activeEditors.ForEach(e => e?.RefreshValues());
                editingHUDRefreshTimer = 1.0f;
            }

            if (Screen.Selected != GameMain.SubEditorScreen) { return; }

            if (GetComponent<ElectricalDischarger>() is { } discharger)
            {
                if (PlayerInput.KeyDown(Keys.Space))
                {
                    discharger.FindNodes(WorldPosition, discharger.Range);
                }
                else
                {
                    discharger.IsActive = false;
                }
            }

            if (Character.Controlled == null) { activeHUDs.Clear(); }

            foreach (ItemComponent ic in components)
            {
                ic.UpdateEditing(deltaTime);
            }

            if (!Linkable) { return; }

            if (!PlayerInput.KeyDown(Keys.Space)) { return; }
            bool lClick = PlayerInput.PrimaryMouseButtonClicked();
            bool rClick = PlayerInput.SecondaryMouseButtonClicked();
            if (!lClick && !rClick) { return; }

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
            var otherEntity = highlightedEntities.FirstOrDefault(e => e != this && e.IsMouseOn(position));
            if (otherEntity != null)
            {
                if (linkedTo.Contains(otherEntity))
                {
                    linkedTo.Remove(otherEntity);
                    if (otherEntity.linkedTo != null && otherEntity.linkedTo.Contains(this))
                    {
                        otherEntity.linkedTo.Remove(this);
                    }
                }
                else
                {
                    linkedTo.Add(otherEntity);
                    if (otherEntity.Linkable && otherEntity.linkedTo != null)
                    {
                        otherEntity.linkedTo.Add(this);
                    }
                }
            }
        }

        public override bool IsMouseOn(Vector2 position)
        {
            Vector2 rectSize = rect.Size.ToVector2();

            Vector2 bodyPos = WorldPosition;

            Vector2 transformedMousePos = MathUtils.RotatePointAroundTarget(position, bodyPos, RotationRad);

            return
                Math.Abs(transformedMousePos.X - bodyPos.X) < rectSize.X / 2.0f &&
                Math.Abs(transformedMousePos.Y - bodyPos.Y) < rectSize.Y / 2.0f;
        }

        public GUIComponent CreateEditingHUD(bool inGame = false)
        {
            activeEditors.Clear();

            int heightScaled = (int)(20 * GUI.Scale);
            editingHUD = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GUI.Canvas, Anchor.CenterRight) { MinSize = new Point(400, 0) }) { UserData = this };
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(0.95f, 0.8f), editingHUD.RectTransform, Anchor.Center), style: null)
            {
                CanTakeKeyBoardFocus = false,
                Spacing = (int)(25 * GUI.Scale)
            };

            var itemEditor = new SerializableEntityEditor(listBox.Content.RectTransform, this, inGame, showName: true, titleFont: GUIStyle.LargeFont) { UserData = this };
            activeEditors.Add(itemEditor);
            itemEditor.Children.First().Color = Color.Black * 0.7f;
            if (!inGame)
            {
                if (Linkable)
                {
                    var linkText = new GUITextBlock(new RectTransform(new Point(editingHUD.Rect.Width, heightScaled), isFixedSize: true), TextManager.Get("HoldToLink"), font: GUIStyle.SmallFont);
                    var itemsText = new GUITextBlock(new RectTransform(new Point(editingHUD.Rect.Width, heightScaled), isFixedSize: true), TextManager.Get("AllowedLinks"), font: GUIStyle.SmallFont);
                    LocalizedString allowedItems = AllowedLinks.None() ? TextManager.Get("None") : string.Join(", ", AllowedLinks);
                    itemsText.Text = TextManager.AddPunctuation(':', itemsText.Text, allowedItems);
                    itemEditor.AddCustomContent(linkText, 1);
                    itemEditor.AddCustomContent(itemsText, 2);
                    linkText.TextColor = GUIStyle.Orange;
                    itemsText.TextColor = GUIStyle.Orange;
                }
                
                //create a tag picker for item containers to make it easier to pick relevant tags for PreferredContainers
                var itemContainer = GetComponent<ItemContainer>();
                if (itemContainer != null)
                {
                    var tagBox = itemEditor.Fields["Tags".ToIdentifier()].First() as GUITextBox;
                    var tagsField = tagBox?.Parent;

                    var containerTagLayout = new GUILayoutGroup(new RectTransform(new Point(editingHUD.Rect.Width, heightScaled), isFixedSize: true), isHorizontal: true);
                    var containerTagButtonLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.25f, 1), containerTagLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterRight);
                    new GUIButton(new RectTransform(new Vector2(0.95f, 1), containerTagButtonLayout.RectTransform), text: TextManager.Get("containertaguibutton"), style: "GUIButtonSmall")
                    {
                        OnClicked = (_, _) => { CreateContainerTagPicker(tagBox); return true; },
                        TextBlock = { AutoScaleHorizontal = true }
                    };
                    var containerTagText = new GUITextBlock(new RectTransform(new Vector2(0.8f, 1), containerTagLayout.RectTransform), TextManager.Get("containertaguibuttondescription"), font: GUIStyle.SmallFont)
                    {
                        TextColor = GUIStyle.Orange
                    };
                    var limitedString = ToolBox.LimitString(containerTagText.Text, containerTagText.Font, itemEditor.Rect.Width - containerTagButtonLayout.Rect.Width);
                    if (limitedString != containerTagText.Text)
                    {
                        containerTagText.ToolTip = containerTagText.Text;
                        containerTagText.Text = limitedString;
                    }
                    itemEditor.AddCustomContent(containerTagLayout, 3);
                }

                var buttonContainer = new GUILayoutGroup(new RectTransform(new Point(listBox.Content.Rect.Width, heightScaled)), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f,
                    CanBeFocused = true
                };

                GUINumberInput rotationField =
                    itemEditor.Fields.TryGetValue("Rotation".ToIdentifier(), out var rotationFieldComponents)
                        ? rotationFieldComponents.OfType<GUINumberInput>().FirstOrDefault()
                        : null;

                var mirrorX = new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("MirrorEntityX"), style: "GUIButtonSmall")
                {
                    ToolTip = TextManager.Get("MirrorEntityXToolTip"),
                    Enabled = Prefab.CanFlipX,
                    OnClicked = (button, data) =>
                    {
                        foreach (MapEntity me in SelectedList)
                        {
                            me.FlipX(relativeToSub: false);
                        }
                        if (!SelectedList.Contains(this)) { FlipX(relativeToSub: false); }
                        ColorFlipButton(button, FlippedX);
                        if (rotationField != null) { rotationField.FloatValue = Rotation; }
                        return true;
                    }
                };
                ColorFlipButton(mirrorX, FlippedX);
                var mirrorY = new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("MirrorEntityY"), style: "GUIButtonSmall")
                {
                    ToolTip = TextManager.Get("MirrorEntityYToolTip"),
                    Enabled = Prefab.CanFlipY,
                    OnClicked = (button, data) =>
                    {
                        foreach (MapEntity me in SelectedList)
                        {
                            me.FlipY(relativeToSub: false);
                        }
                        if (!SelectedList.Contains(this)) { FlipY(relativeToSub: false); }
                        ColorFlipButton(button, FlippedY);
                        if (rotationField != null) { rotationField.FloatValue = Rotation; }
                        return true;
                    }
                };
                ColorFlipButton(mirrorY, FlippedY);
                if (Sprite != null)
                {
                    var reloadTextureButton = new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("ReloadSprite"), style: "GUIButtonSmall");
                    reloadTextureButton.OnClicked += (button, data) =>
                    {
                        Sprite.ReloadXML();
                        Sprite.ReloadTexture();
                        return true;
                    };
                }
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
                buttonContainer.RectTransform.MinSize = new Point(0, buttonContainer.RectTransform.Children.Max(c => c.MinSize.Y));
                buttonContainer.RectTransform.IsFixedSize = true;
                itemEditor.AddCustomContent(buttonContainer, itemEditor.ContentCount);
                GUITextBlock.AutoScaleAndNormalize(buttonContainer.Children.Select(b => ((GUIButton)b).TextBlock));

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
                    itemEditor.AddCustomContent(tickBox, 1);
                }

                if (!Layer.IsNullOrEmpty())
                {
                    var layerText = new GUITextBlock(new RectTransform(new Point(listBox.Content.Rect.Width, heightScaled)) { MinSize = new Point(0, heightScaled) }, TextManager.AddPunctuation(':', TextManager.Get("editor.layer"), Layer));
                    itemEditor.AddCustomContent(layerText, 1);
                }
            }

            foreach (ItemComponent ic in components)
            {
                if (inGame)
                {
                    if (!ic.AllowInGameEditing) { continue; }
                    if (SerializableProperty.GetProperties<InGameEditable>(ic).Count == 0 &&
                        !SerializableProperty.GetProperties<ConditionallyEditable>(ic).Any(p => p.GetAttribute<ConditionallyEditable>().IsEditable(ic)))
                    {
                        continue;
                    }
                }
                else
                {
                    if (ic.RequiredItems.Count == 0 && ic.DisabledRequiredItems.Count == 0 && SerializableProperty.GetProperties<Editable>(ic).Count == 0) { continue; }
                }

                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), listBox.Content.RectTransform), style: "HorizontalLine");

                var componentEditor = new SerializableEntityEditor(listBox.Content.RectTransform, ic, inGame, showName: !inGame, titleFont: GUIStyle.SubHeadingFont) { UserData = ic };
                componentEditor.Children.First().Color = Color.Black * 0.7f;
                activeEditors.Add(componentEditor);

                if (inGame)
                {
                    ic.CreateEditingHUD(componentEditor);
                    componentEditor.Recalculate();
                    continue;
                }

                List<RelatedItem> requiredItems = new List<RelatedItem>();
                foreach (var kvp in ic.RequiredItems)
                {
                    foreach (RelatedItem relatedItem in kvp.Value)
                    {
                        requiredItems.Add(relatedItem);
                    }
                }
                requiredItems.AddRange(ic.DisabledRequiredItems);

                foreach (RelatedItem relatedItem in requiredItems)
                {
                    //TODO: add to localization
                    var textBlock = new GUITextBlock(new RectTransform(new Point(listBox.Content.Rect.Width, heightScaled)),
                        relatedItem.Type.ToString() + " required", font: GUIStyle.SmallFont)
                    {
                        Padding = new Vector4(10.0f, 0.0f, 10.0f, 0.0f)
                    };
                    textBlock.RectTransform.IsFixedSize = true;
                    componentEditor.AddCustomContent(textBlock, 1);

                    GUITextBox namesBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), textBlock.RectTransform, Anchor.CenterRight))
                    {
                        Font = GUIStyle.SmallFont,
                        Text = relatedItem.JoinedIdentifiers,
                        OverflowClip = true
                    };
                    textBlock.RectTransform.Resize(new Point(textBlock.Rect.Width, namesBox.RectTransform.MinSize.Y));

                    namesBox.OnDeselected += (textBox, key) =>
                    {
                        relatedItem.JoinedIdentifiers = textBox.Text;
                        textBox.Text = relatedItem.JoinedIdentifiers;
                    };

                    namesBox.OnEnterPressed += (textBox, text) =>
                    {
                        relatedItem.JoinedIdentifiers = text;
                        textBox.Text = relatedItem.JoinedIdentifiers;
                        return true;
                    };
                }

                ic.CreateEditingHUD(componentEditor);
                componentEditor.Recalculate();
            }

            PositionEditingHUD();
            SetHUDLayout();

            return editingHUD;
        }

        private ImmutableArray<DecorativeSprite> GetUpgradeSprites(Upgrade upgrade)
        {
            var upgradeSprites = upgrade.Prefab.DecorativeSprites;

            if (Prefab.UpgradeOverrideSprites.ContainsKey(upgrade.Prefab.Identifier))
            {
                upgradeSprites = Prefab.UpgradeOverrideSprites[upgrade.Prefab.Identifier];
            }

            return upgradeSprites;
        }

        public override bool AddUpgrade(Upgrade upgrade, bool createNetworkEvent = false)
        {
            if (upgrade.Prefab.IsWallUpgrade) { return false; }
            bool result = base.AddUpgrade(upgrade, createNetworkEvent);
            if (result && !upgrade.Disposed)
            {
                var upgradeSprites = GetUpgradeSprites(upgrade);

                if (upgradeSprites.Any())
                {
                    foreach (DecorativeSprite decorativeSprite in upgradeSprites)
                    {
                        decorativeSprite.Sprite.EnsureLazyLoaded();
                        spriteAnimState.Add(decorativeSprite, new DecorativeSprite.State());
                    }
                    UpdateSpriteStates(0.0f);
                }
            }
            return result;
        }

        public void CreateContainerTagPicker([MaybeNull] GUITextBox tagTextBox)
        {
            var msgBox = new GUIMessageBox(string.Empty, string.Empty, new[] { TextManager.Get("Ok") }, new Vector2(0.35f, 0.6f), new Point(400, 400));
            msgBox.Buttons[0].OnClicked = msgBox.Close;

            var infoIcon = new GUIImage(new RectTransform(new Vector2(0.066f), msgBox.InnerFrame.RectTransform)
            {
                RelativeOffset = new Vector2(0.015f)
            }, style: "GUIButtonInfo")
            {
                ToolTip = TextManager.Get("containertagui.tutorial"),
                IgnoreLayoutGroups = true
            };

            var layout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.85f), msgBox.Content.RectTransform));

            var list = new GUIListBox(new RectTransform(new Vector2(1f, 1f), layout.RectTransform));

            const float NameSize = 0.4f;
            const float ItemSize = 0.5f;
            const float CountSize = 0.1f;

            var headerLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.075f), list.Content.RectTransform), isHorizontal: true);
            new GUIButton(new RectTransform(new Vector2(NameSize, 1f), headerLayout.RectTransform), TextManager.Get("tagheader.tag"), style: "GUIButtonSmallFreeScale") { ForceUpperCase = ForceUpperCase.Yes, CanBeFocused = false };
            new GUIButton(new RectTransform(new Vector2(ItemSize, 1f), headerLayout.RectTransform), TextManager.Get("tagheader.items"), style: "GUIButtonSmallFreeScale") { ForceUpperCase = ForceUpperCase.Yes, CanBeFocused = false  };
            new GUIButton(new RectTransform(new Vector2(CountSize, 1f), headerLayout.RectTransform), TextManager.Get("tagheader.count"), style: "GUIButtonSmallFreeScale") { ForceUpperCase = ForceUpperCase.Yes, CanBeFocused = false };

            var itemsByTag =
                ContainerTagPrefab.Prefabs
                    .ToImmutableDictionary(
                        ct => ct,
                        ct => ct.GetItemsAndSpawnProbabilities());

            // Group the prefabs by category and turn them into a dictionary where the key is the category and value is the list of identifiers of the prefabs.
            // LINQ GroupBy returns GroupedEnumerable where the enumerable is the list of prefabs and key is what we grouped by.
            var tagCategories = ContainerTagPrefab.Prefabs
                .GroupBy(ct => ct.Category)
                .ToImmutableDictionary(
                    g => g.Key,
                    g => g.Select(ct => ct.Identifier).ToImmutableArray());

            foreach (var (category, categoryTags) in tagCategories)
            {
                var categoryButton = new GUIButton(new RectTransform(new Vector2(1f, 0.075f), list.Content.RectTransform), style: "GUIButtonSmallFreeScale");
                categoryButton.Color *= 0.66f;
                var categoryLayout = new GUILayoutGroup(new RectTransform(Vector2.One, categoryButton.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft) { Stretch = true };
                var categoryText = new GUITextBlock(new RectTransform(Vector2.One, categoryLayout.RectTransform), TextManager.Get($"tagcategory.{category}"), font: GUIStyle.SubHeadingFont);
                var arrowImage = new GUIImage(new RectTransform(new Vector2(1f, 0.5f), categoryLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonVerticalArrowFreeScale");
                var arrowPadding = new GUIFrame(new RectTransform(new Vector2(0.025f, 1f), categoryLayout.RectTransform), style: null);

                bool hasHiddenCategories = false;
                foreach (var categoryTag in categoryTags.OrderBy(t => t.Value))
                {
                    var found = itemsByTag.FirstOrNull(kvp => kvp.Key.Identifier == categoryTag);
                    if (found is null)
                    {
                        DebugConsole.ThrowError($"Failed to find tag with identifier {categoryTag} in itemsByTag");
                        continue;
                    }

                    var (tag, prefabsAndProbabilities) = found.Value;

                    bool isCorrectSubType = tag.IsRecommendedForSub(Submarine);

                    var tagLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), list.Content.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                    {
                        UserData = category,
                        Visible = isCorrectSubType
                    };

                    if (!isCorrectSubType)
                    {
                        hasHiddenCategories = true;
                    }

                    var checkBoxLayout = new GUILayoutGroup(new RectTransform(new Vector2(NameSize, 1f), tagLayout.RectTransform), childAnchor: Anchor.Center);
                    var enabledCheckBox = new GUITickBox(new RectTransform(Vector2.One, checkBoxLayout.RectTransform, Anchor.Center), tag.Name, font: GUIStyle.SmallFont)
                    {
                        Selected = tags.Contains(tag.Identifier),
                        ToolTip = tag.Description
                    };

                    var tickBoxText = enabledCheckBox.TextBlock;
                    tickBoxText.Text = ToolBox.LimitString(tickBoxText.Text, tickBoxText.Font, tickBoxText.Rect.Width);

                    var itemLayout = new GUILayoutGroup(new RectTransform(new Vector2(ItemSize, 1f), tagLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
                    var itemLayoutScissor = new GUIScissorComponent(new RectTransform(new Vector2(0.8f, 1f), itemLayout.RectTransform)) { CanBeFocused = false };
                    var itemLayoutButtonLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 1), itemLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.Center);
                    var itemLayoutButton = new GUIButton(new RectTransform(new Vector2(0.8f), itemLayoutButtonLayout.RectTransform), text: "...", style: "GUICharacterInfoButton")
                    {
                        UserData = tag,
                        ToolTip = TextManager.Get("containertagui.viewprobabilities")
                    };
                    
                    itemLayoutButtonLayout.Recalculate();

                    float scroll = 0f;
                    float localScroll = 0f;
                    int lastSkippedItems = 0;
                    int skippedItems = 0;
                    var itemLayoutDraw = new GUICustomComponent(new RectTransform(new Vector2(1f, 0.9f), itemLayoutScissor.Content.RectTransform, Anchor.CenterLeft), onDraw: (spriteBatch, component) =>
                    {
                        component.ToolTip = string.Empty;

                        const float padding = 8f;
                        float offset = 0f;
                        float size = component.Rect.Height;
                        int start = (int)Math.Floor(scroll);
                        int amountToDraw = (int)Math.Ceiling(component.Rect.Width / size) + 1; // +1 just to be on the safe side
                        bool shouldIncrementOnSkip = true;
                        float toDrawWidth = prefabsAndProbabilities.Length * (size + padding);

                        // if the width is less than the component width we need to limit how many items we draw or it looks weird
                        if (toDrawWidth < component.Rect.Width)
                        {
                            shouldIncrementOnSkip = false;
                            amountToDraw = prefabsAndProbabilities.Length;
                        }

                        for (int i = start; i < start + amountToDraw; i++)
                        {
                            var (ip, probability, _) = prefabsAndProbabilities[i % prefabsAndProbabilities.Length];
                            var sprite = ip.InventoryIcon ?? ip.Sprite;

                            if (sprite is null)
                            {
                                // I don't think this should happen but just in case
                                if (shouldIncrementOnSkip)
                                {
                                    amountToDraw++;
                                    skippedItems++;
                                }
                                continue;
                            }

                            if (ShouldHideItemPrefab(ip, probability))
                            {
                                if (shouldIncrementOnSkip)
                                {
                                    skippedItems++;
                                    amountToDraw++;
                                }
                                continue;
                            }

                            float partialScroll = localScroll * (size + padding);
                            var drawRect = new RectangleF(itemLayoutScissor.Rect.X + offset - partialScroll, component.Rect.Y, size, size);

                            var isMouseOver = drawRect.Contains(PlayerInput.MousePosition);
                            if (isMouseOver)
                            {
                                component.ToolTip = ip.CreateTooltipText();
                            }

                            var slotSprite = Inventory.SlotSpriteSmall;
                            slotSprite?.Draw(spriteBatch, drawRect.Location, Color.White, origin: Vector2.Zero, rotate: 0f, scale: size / slotSprite.size.X * Inventory.SlotSpriteSmallScale);

                            float iconScale = Math.Min(drawRect.Width / sprite.size.X, drawRect.Height / sprite.size.Y) * 0.9f;

                            Color drawColor = ip.InventoryIconColor;

                            sprite.Draw(spriteBatch, drawRect.Center, drawColor, origin: sprite.Origin, scale: iconScale);
                            offset += size + padding;
                        }

                        // we need to compensate for the skipped items so that the scroll doesn't jump around
                        if (skippedItems < lastSkippedItems)
                        {
                            scroll += lastSkippedItems - skippedItems;
                        }

                        lastSkippedItems = skippedItems;
                        skippedItems = 0;
                    }, onUpdate: (deltaTime, component) =>
                    {
                        if (GUI.MouseOn != component && MathUtils.NearlyEqual(localScroll, 0, deltaTime * 2))
                        {
                            localScroll = 0f;
                            return;
                        }

                        float totalWidth = prefabsAndProbabilities.Length * (component.Rect.Height + 8f);
                        if (totalWidth < component.Rect.Width) { return; }
                        scroll += deltaTime;
                        localScroll = scroll % 1f;
                    })
                    {
                        HoverCursor = CursorState.Default,
                        AlwaysOverrideCursor = true
                    };

                    var tooltip = TextManager.Get(tag.WarnIfLess ? "ContainerTagUI.RecommendedAmount" : "ContainerTagUI.SuggestedAmount");

                    var countBlock = new GUITextBlock(new RectTransform(new Vector2(CountSize, 1f), tagLayout.RectTransform), string.Empty, textAlignment: Alignment.Center)
                    {
                        ToolTip = tooltip
                    };
                    UpdateCountBlock(countBlock, tag);

                    enabledCheckBox.OnSelected += tickBox =>
                    {
                        if (tickBox.Selected)
                        {
                            AddTag(tag.Identifier);
                        }
                        else
                        {
                            RemoveTag(tag.Identifier);
                        }

                        if (tagTextBox is not null)
                        {
                            tagTextBox.Text = string.Join(',', tags.Where(t => !Prefab.Tags.Contains(t)));
                        }
                        UpdateCountBlock(countBlock, tag);
                        return true;
                    };

                    itemLayoutButton.OnClicked = (button, _) =>
                    {
                        CreateContainerTagItemListPopup(tag, button.Rect.Center, layout, prefabsAndProbabilities);
                        return true;
                    };

                    void UpdateCountBlock(GUITextBlock textBlock, ContainerTagPrefab containerTag)
                    {
                        if (textBlock is null) { return; }

                        var tagCount = Submarine.GetItems(alsoFromConnectedSubs: true).Count(i => i.HasTag(containerTag.Identifier));
                        textBlock.Text = $"{tagCount} ({containerTag.RecommendedAmount})";

                        if (!isCorrectSubType || !containerTag.WarnIfLess || containerTag.RecommendedAmount <= 0) { return; }

                        if (tagCount < containerTag.RecommendedAmount)
                        {
                            textBlock.TextColor = GUIStyle.Red;
                            textBlock.Text += "*";
                            textBlock.ToolTip = RichString.Rich($"{tooltip}\n\n‖color:gui.red‖{TextManager.Get("ContainerTagUI.RecommendedAmountWarning")}‖color:end‖");
                        }
                        else if (tagCount >= containerTag.RecommendedAmount)
                        {
                            textBlock.TextColor = GUIStyle.Green;
                            textBlock.ToolTip = tooltip;
                        }
                    }
                }

                arrowImage.SpriteEffects = hasHiddenCategories ? SpriteEffects.None : SpriteEffects.FlipVertically;
                categoryButton.OnClicked = (_, _) =>
                {
                    arrowImage.SpriteEffects ^= SpriteEffects.FlipVertically;

                    foreach (var child in list.Content.Children)
                    {
                        if (child.UserData is Identifier id && id == category)
                        {
                            child.Visible = !child.Visible;
                        }
                    }
                    return true;
                };
            }
        }

        private static void CreateContainerTagItemListPopup(ContainerTagPrefab tag, Point location, GUIComponent popupParent, ImmutableArray<ContainerTagPrefab.ItemAndProbability> prefabAndProbabilities)
        {
            const string TooltipUserData = "tooltip";
            const string ProbabilityUserData = "probability";

            if (popupParent.GetChildByUserData(TooltipUserData) is { } existingTooltip)
            {
                popupParent.RemoveChild(existingTooltip);
            }

            var tooltip = new GUIFrame(new RectTransform(new Point(popupParent.Rect.Height), popupParent.RectTransform)
            {
                AbsoluteOffset = location - popupParent.Rect.Location
            })
            {
                UserData = TooltipUserData,
                IgnoreLayoutGroups = true
            };

            if (tooltip.Rect.Bottom > GameMain.GraphicsHeight)
            {
                int diffY = tooltip.Rect.Bottom - GameMain.GraphicsHeight;
                tooltip.RectTransform.AbsoluteOffset -= new Point(0, diffY);
            }

            if (tooltip.Rect.Right > GameMain.GraphicsWidth)
            {
                int diffX = tooltip.Rect.Right - GameMain.GraphicsWidth;
                tooltip.RectTransform.AbsoluteOffset -= new Point(diffX, 0);
            }

            var tooltipLayout = new GUILayoutGroup(new RectTransform(ToolBox.PaddingSizeParentRelative(tooltip.RectTransform, 0.9f), tooltip.RectTransform, Anchor.Center));

            var tooltipHeader = new GUITextBlock(new RectTransform(new Vector2(1f, 0.1f), tooltipLayout.RectTransform), tag.Name, textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont);
            var tooltipList = new GUIListBox(new RectTransform(new Vector2(1f, 0.7f), tooltipLayout.RectTransform));

            var tooltipHeaderLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), tooltipList.Content.RectTransform), isHorizontal: true);
            new GUIButton(new RectTransform(new Vector2(0.66f, 1f), tooltipHeaderLayout.RectTransform), TextManager.Get("tagheader.item"), style: "GUIButtonSmallFreeScale") { ForceUpperCase = ForceUpperCase.Yes, CanBeFocused = false };
            new GUIButton(new RectTransform(new Vector2(0.33f, 1f), tooltipHeaderLayout.RectTransform), TextManager.Get("tagheader.probability"), style: "GUIButtonSmallFreeScale") { ForceUpperCase = ForceUpperCase.Yes, CanBeFocused = false };

            foreach (var itemAndProbability in prefabAndProbabilities.OrderByDescending(p => p.Probability))
            {
                var (ip, probability, campaignOnlyProbability) = itemAndProbability;
                if (ShouldHideItemPrefab(ip, probability)) { continue; }

                var itemLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), tooltipList.Content.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    UserData = itemAndProbability
                };

                var itemNameLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.66f, 1f), itemLayout.RectTransform), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                {
                    Stretch = true
                };

                var itemIcon = new GUIImage(new RectTransform(Vector2.One, itemNameLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), ip.InventoryIcon ?? ip.Sprite, scaleToFit: true)
                {
                    Color = ip.InventoryIconColor
                };
                
                var itemName = new GUITextBlock(new RectTransform(Vector2.One, itemNameLayout.RectTransform), ip.Name);
                itemName.Text = ToolBox.LimitString(ip.Name, itemName.Font, itemName.Rect.Width);
                
                var toolTipContainer = new GUIFrame(new RectTransform(Vector2.One, itemNameLayout.RectTransform), style: null)
                {
                    IgnoreLayoutGroups = true,
                    ToolTip = ip.CreateTooltipText()
                };

                var probabilityText = new GUITextBlock(new RectTransform(new Vector2(0.33f, 1f), itemLayout.RectTransform), ProbabilityToPercentage(campaignOnlyProbability), textAlignment: Alignment.Right)
                {
                    UserData = ProbabilityUserData
                };
                if (MathUtils.NearlyEqual(campaignOnlyProbability, 0f)) { probabilityText.TextColor = GUIStyle.Red; }
            }

            var campaignCheckbox = new GUITickBox(new RectTransform(new Vector2(1f, 0.1f), tooltipLayout.RectTransform), label: TextManager.Get("containertagui.campaignonly"))
            {
                ToolTip = TextManager.Get("containertagui.campaignonlytooltip"),
                Selected = true,
                OnSelected = box =>
                {
                    foreach (var child in tooltipList.Content.Children)
                    {
                        if (child.UserData is not ContainerTagPrefab.ItemAndProbability data) { continue; }

                        if (child.GetChildByUserData(ProbabilityUserData) is not GUITextBlock text) { continue; }
                        
                        float probability = box.Selected 
                            ? data.CampaignProbability 
                            : data.Probability;
                        text.Text = ProbabilityToPercentage(probability);

                        text.TextColor = MathUtils.NearlyEqual(probability, 0f) 
                            ? GUIStyle.Red 
                            : GUIStyle.TextColorNormal;
                    }

                    return true;
                }
            };

            var tooltipClose = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), tooltipLayout.RectTransform), TextManager.Get("Close"))
            {
                OnClicked = (_, _) =>
                {
                    popupParent.RemoveChild(tooltip);
                    return true;
                }
            };

            static LocalizedString ProbabilityToPercentage(float probability)
                => TextManager.GetWithVariable("percentageformat", "[value]", MathF.Round((probability * 100f), 1).ToString(CultureInfo.InvariantCulture));

        }

        private static bool ShouldHideItemPrefab(ItemPrefab ip, float probability)
            => ip.HideInMenus && MathUtils.NearlyEqual(probability, 0f);

        /// <summary>
        /// Reposition currently active item interfaces to make sure they don't overlap with each other
        /// </summary>
        private void SetHUDLayout(bool ignoreLocking = false)
        {
            //reset positions first
            List<GUIComponent> elementsToMove = new List<GUIComponent>();

            if (editingHUD != null && editingHUD.UserData == this &&
                ((HasInGameEditableProperties && Character.Controlled?.SelectedItem == this) || Screen.Selected == GameMain.SubEditorScreen))
            {
                elementsToMove.Add(editingHUD);
            }

            debugInitialHudPositions.Clear();
            foreach (ItemComponent ic in activeHUDs)
            {
                if (ic.GuiFrame == null || ic.AllowUIOverlap || ic.GetLinkUIToComponent() != null) { continue; }
                if (!ignoreLocking && ic.LockGuiFramePosition) { continue; }
                //if the frame covers nearly all of the screen, don't trying to prevent overlaps because it'd fail anyway
                if (ic.GuiFrame.Rect.Width >= GameMain.GraphicsWidth * 0.9f && ic.GuiFrame.Rect.Height >= GameMain.GraphicsHeight * 0.9f) { continue; }
                ic.GuiFrame.RectTransform.ScreenSpaceOffset = ic.GuiFrameOffset;
                elementsToMove.Add(ic.GuiFrame);
                debugInitialHudPositions.Add(ic.GuiFrame.Rect);
            }

            List<Rectangle> disallowedAreas = new List<Rectangle>();
            if (GameMain.GameSession?.CrewManager != null && Screen.Selected == GameMain.GameScreen)
            {
                int disallowedPadding = (int)(50 * GUI.Scale);
                disallowedAreas.Add(GameMain.GameSession.CrewManager.GetActiveCrewArea());
                disallowedAreas.Add(new Rectangle(
                    HUDLayoutSettings.ChatBoxArea.X - disallowedPadding, HUDLayoutSettings.ChatBoxArea.Y,
                    HUDLayoutSettings.ChatBoxArea.Width + disallowedPadding, HUDLayoutSettings.ChatBoxArea.Height));
            }

            if (Screen.Selected is SubEditorScreen editor)
            {
                disallowedAreas.Add(editor.EntityMenu.Rect);
                disallowedAreas.Add(editor.TopPanel.Rect);
                disallowedAreas.Add(editor.ToggleEntityMenuButton.Rect);
            }

            GUI.PreventElementOverlap(elementsToMove, disallowedAreas, clampArea: HUDLayoutSettings.ItemHUDArea);

            //System.Diagnostics.Debug.WriteLine("after: " + elementsToMove[0].Rect.ToString() + "   " + elementsToMove[1].Rect.ToString());
            foreach (ItemComponent ic in activeHUDs)
            {
                if (ic.GuiFrame == null) { continue; }

                var linkUIToComponent = ic.GetLinkUIToComponent();
                if (linkUIToComponent == null) { continue; }

                ic.GuiFrame.RectTransform.ScreenSpaceOffset = linkUIToComponent.GuiFrame.RectTransform.ScreenSpaceOffset;
            }
        }

        private readonly List<Rectangle> debugInitialHudPositions = new List<Rectangle>();

        private readonly List<ItemComponent> prevActiveHUDs = new List<ItemComponent>();
        private readonly List<ItemComponent> activeComponents = new List<ItemComponent>();
        private readonly List<ItemComponent> maxPriorityHUDs = new List<ItemComponent>();

        public void UpdateHUD(Camera cam, Character character, float deltaTime)
        {
            bool editingHUDCreated = false;
            if ((HasInGameEditableProperties && (character.SelectedItem == this || EditableWhenEquipped)) ||
                Screen.Selected == GameMain.SubEditorScreen)
            {
                GUIComponent prevEditingHUD = editingHUD;
                UpdateEditing(cam, deltaTime);
                editingHUDCreated = editingHUD != null && editingHUD != prevEditingHUD;
            }

            if (editingHUD == null ||
                !(GUI.KeyboardDispatcher.Subscriber is GUITextBox textBox) ||
                !editingHUD.IsParentOf(textBox))
            {
                editingHUDRefreshTimer -= deltaTime;
            }

            prevActiveHUDs.Clear();
            prevActiveHUDs.AddRange(activeHUDs);
            activeComponents.Clear();
            activeComponents.AddRange(components);

            foreach (MapEntity entity in linkedTo)
            {
                if (Prefab.IsLinkAllowed(entity.Prefab) && entity is Item i)
                {
                    if (!i.DisplaySideBySideWhenLinked) { continue; }
                    activeComponents.AddRange(i.components);
                }
            }

            activeHUDs.Clear();
            maxPriorityHUDs.Clear();
            bool DrawHud(ItemComponent ic)
            {
                if (!ic.ShouldDrawHUD(character)) { return false; }
                if (character.HasEquippedItem(this))
                {
                    return ic.DrawHudWhenEquipped;
                }
                else
                {
                    return ic.CanBeSelected && ic.HasRequiredItems(character, addMessage: false);
                }
            }
            //the HUD of the component with the highest priority will be drawn
            //if all components have a priority of 0, all of them are drawn
            foreach (ItemComponent ic in activeComponents)
            {
                if (ic.HudPriority > 0 && DrawHud(ic) && (maxPriorityHUDs.Count == 0 || ic.HudPriority >= maxPriorityHUDs[0].HudPriority))
                {
                    if (maxPriorityHUDs.Count > 0 && ic.HudPriority > maxPriorityHUDs[0].HudPriority) { maxPriorityHUDs.Clear(); }
                    maxPriorityHUDs.Add(ic);
                }
            }

            if (maxPriorityHUDs.Count > 0)
            {
                activeHUDs.AddRange(maxPriorityHUDs);
            }
            else
            {
                foreach (ItemComponent ic in activeComponents)
                {
                    if (DrawHud(ic))
                    {
                        activeHUDs.Add(ic);
                    }
                }
            }

            activeHUDs.Sort((h1, h2) => { return h2.HudLayer.CompareTo(h1.HudLayer); });

            //active HUDs have changed, need to reposition
            if (!prevActiveHUDs.SequenceEqual(activeHUDs) || editingHUDCreated)
            {
                SetHUDLayout();
            }

            Rectangle mergedHUDRect = Rectangle.Empty;
            foreach (ItemComponent ic in activeHUDs)
            {
                ic.UpdateHUD(character, deltaTime, cam);
                if (ic.GuiFrame != null && ic.GuiFrame.Rect.Height < GameMain.GraphicsHeight)
                {
                    mergedHUDRect = mergedHUDRect == Rectangle.Empty ?
                        ic.GuiFrame.Rect :
                        Rectangle.Union(mergedHUDRect, ic.GuiFrame.Rect);
                }
            }

            if (mergedHUDRect != Rectangle.Empty)
            {
                if (itemInUseWarning != null) { itemInUseWarning.Visible = false; }
                foreach (Character otherCharacter in Character.CharacterList)
                {
                    if (otherCharacter != character &&
                        otherCharacter.SelectedItem == this)
                    {
                        ItemInUseWarning.Visible = true;
                        if (mergedHUDRect.Width > GameMain.GraphicsWidth / 2) { mergedHUDRect.Inflate(-GameMain.GraphicsWidth / 4, 0); }
                        itemInUseWarning.RectTransform.ScreenSpaceOffset = new Point(mergedHUDRect.X, mergedHUDRect.Bottom);
                        itemInUseWarning.RectTransform.NonScaledSize = new Point(mergedHUDRect.Width, (int)(50 * GUI.Scale));
                        if (itemInUseWarning.UserData != otherCharacter)
                        {
                            itemInUseWarning.Text = TextManager.GetWithVariable("ItemInUse", "[character]", otherCharacter.Name);
                            itemInUseWarning.UserData = otherCharacter;
                        }
                        break;
                    }
                }
            }
        }

        public void DrawHUD(SpriteBatch spriteBatch, Camera cam, Character character)
        {
            if (HasInGameEditableProperties && (character.SelectedItem == this || EditableWhenEquipped))
            {
                DrawEditing(spriteBatch, cam);
            }

            foreach (ItemComponent ic in activeHUDs)
            {
                if (ic.CanBeSelected)
                {
                    ic.DrawHUD(spriteBatch, character);
                }
            }

            if (GameMain.DebugDraw)
            {
                int i = 0;
                foreach (ItemComponent ic in activeHUDs)
                {
                    if (i >= debugInitialHudPositions.Count) { break; }
                    if (activeHUDs[i].GuiFrame == null) { continue; }
                    if (ic.GuiFrame == null || ic.AllowUIOverlap || ic.GetLinkUIToComponent() != null) { continue; }

                    GUI.DrawRectangle(spriteBatch, debugInitialHudPositions[i], Color.Orange);
                    GUI.DrawRectangle(spriteBatch, ic.GuiFrame.Rect, Color.LightGreen);
                    GUI.DrawLine(spriteBatch, debugInitialHudPositions[i].Location.ToVector2(), ic.GuiFrame.Rect.Location.ToVector2(), Color.Orange);

                    i++;
                }
            }
        }

        readonly List<ColoredText> texts = new();
        public List<ColoredText> GetHUDTexts(Character character, bool recreateHudTexts = true)
        {
            // Always create the texts if they have not yet been created
            if (texts.Any() && !recreateHudTexts) { return texts; }
            texts.Clear();

            string nameText = Name;
            if (Prefab.Tags.Contains("identitycard") || Tags.Contains("despawncontainer"))
            {
                string[] readTags = Tags.Split(',');
                string idName = null;
                foreach (string tag in readTags)
                {
                    string[] s = tag.Split(':');
                    if (s[0] == "name")
                    {
                        idName = s[1];
                        break;
                    }
                }
                if (idName != null)
                {
                    nameText += $" ({idName})";
                }
            }
            if (DroppedStack.Any())
            {
                nameText += $" x{DroppedStack.Count()}";
            }

            texts.Add(new ColoredText(nameText, GUIStyle.TextColorNormal, isCommand: false, isError: false));

            if (CampaignMode.BlocksInteraction(CampaignInteractionType))
            {
                texts.Add(new ColoredText(TextManager.GetWithVariable($"CampaignInteraction.{CampaignInteractionType}", "[key]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Use)).Value, Color.Cyan, isCommand: false, isError: false));
            }
            else
            {
                foreach (ItemComponent itemComponent in components)
                {
                    var interactionVisibility = GetComponentInteractionVisibility(character, itemComponent);
                    if (interactionVisibility == InteractionVisibility.None) { continue; }
                    if (itemComponent.DisplayMsg.IsNullOrEmpty()) { continue; }
                    
                    Color color = interactionVisibility == InteractionVisibility.MissingRequirement ? Color.Gray : Color.Cyan;
                    texts.Add(new ColoredText(itemComponent.DisplayMsg.Value, color, isCommand: false, isError: false));
                }
            }
            if (PlayerInput.KeyDown(InputType.ContextualCommand))
            {
                texts.Add(new ColoredText(TextManager.ParseInputTypes(TextManager.Get("itemmsgcontextualorders")).Value, Color.Cyan, isCommand: false, isError: false));
            }
            else
            {
                texts.Add(new ColoredText(TextManager.Get("itemmsg.morreoptionsavailable").Value, Color.LightGray * 0.7f, isCommand: false, isError: false));
            }            
            return texts;
        }

        private enum InteractionVisibility
        {
            None,
            MissingRequirement,
            Visible
        }

        /// <summary>
        /// Determine, for UI display purposes, the type of interaction visibility for an item component.
        ///
        /// Example:
        /// Visible -> Display cyan "click to interact" type text on item hover.
        /// MissingRequirement -> Display gray "need tool" type text on item hover.
        /// None -> Hide from item hover texts.
        /// </summary>
        /// <param name="character">Character, for tool requirement purposes.</param>
        /// <param name="itemComponent">The item component to inspect.</param>
        /// <returns>The interaction visibility state for this component.</returns>
        private static InteractionVisibility GetComponentInteractionVisibility(Character character, ItemComponent itemComponent)
        {
            if (!itemComponent.CanBePicked && !itemComponent.CanBeSelected) { return InteractionVisibility.None; }
            if (itemComponent is Holdable holdable && !holdable.CanBeDeattached()) { return InteractionVisibility.None; }
            if (itemComponent is ConnectionPanel connectionPanel && !connectionPanel.CanRewire()) { return InteractionVisibility.None; }
            
            InteractionVisibility interactionVisibility = InteractionVisibility.MissingRequirement;
            if (itemComponent.HasRequiredItems(character, addMessage: false))
            {
                if (itemComponent is Repairable repairable)
                {
                    if (repairable.IsBelowRepairThreshold)
                    {
                        interactionVisibility = InteractionVisibility.Visible;
                    }
                }
                else
                {
                    interactionVisibility = InteractionVisibility.Visible;
                }
            }
            
            return interactionVisibility;
        }

        public bool HasVisibleInteraction(Character character)
        {
            foreach (var component in components)
            {
                if (GetComponentInteractionVisibility(character, component) == InteractionVisibility.Visible)
                {
                    return true;
                }
            }
            
            return false;
        }

        public void ForceHUDLayoutUpdate(bool ignoreLocking = false)
        {
            foreach (ItemComponent ic in activeHUDs)
            {
                if (ic.GuiFrame == null) { continue; }
                if (!ic.CanBeSelected && !ic.DrawHudWhenEquipped) { continue; }
                ic.GuiFrame.RectTransform.ScreenSpaceOffset = Point.Zero;
                if (ic.UseAlternativeLayout)
                {
                    ic.AlternativeLayout?.ApplyTo(ic.GuiFrame.RectTransform);
                }
                else
                {
                    ic.DefaultLayout?.ApplyTo(ic.GuiFrame.RectTransform);
                }
            }
            SetHUDLayout(ignoreLocking);
        }

        public override void AddToGUIUpdateList(int order = 0)
        {
            if (Screen.Selected is SubEditorScreen)
            {
                if (editingHUD != null && editingHUD.UserData == this) { editingHUD.AddToGUIUpdateList(); }
            }
            else
            {
                if (HasInGameEditableProperties && Character.Controlled != null && (Character.Controlled.SelectedItem == this || EditableWhenEquipped))
                {
                    if (editingHUD != null && editingHUD.UserData == this) { editingHUD.AddToGUIUpdateList(); }
                }
            }

            var character = Character.Controlled;
            var selectedItem = Character.Controlled?.SelectedItem;
            if (character != null && selectedItem != this && GetComponent<RemoteController>() == null)
            {
                bool insideCircuitBox = 
                    selectedItem?.GetComponent<CircuitBox>() != null &&
                    selectedItem.ContainedItems.Contains(this);
                if (!insideCircuitBox &&
                    selectedItem?.GetComponent<RemoteController>()?.TargetItem != this &&
                    !character.HeldItems.Any(it => it.GetComponent<RemoteController>()?.TargetItem == this))
                {
                    return;
                }
            }

            bool needsLayoutUpdate = false;
            foreach (ItemComponent ic in activeHUDs)
            {
                if (!ic.CanBeSelected) { continue; }

                bool useAlternativeLayout = activeHUDs.Count > 1;
                bool wasUsingAlternativeLayout = ic.UseAlternativeLayout;
                ic.UseAlternativeLayout = useAlternativeLayout;
                needsLayoutUpdate |= ic.UseAlternativeLayout != wasUsingAlternativeLayout;
                ic.AddToGUIUpdateList(order);
            }

            if (itemInUseWarning != null && itemInUseWarning.Visible)
            {
                itemInUseWarning.AddToGUIUpdateList();
            }

            if (needsLayoutUpdate)
            {
                SetHUDLayout();
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            EventType eventType =
                (EventType)msg.ReadRangedInteger((int)EventType.MinValue, (int)EventType.MaxValue);

            switch (eventType)
            {
                case EventType.ComponentState:
                    {
                        int componentIndex = msg.ReadRangedInteger(0, components.Count - 1);
                        if (components[componentIndex] is IServerSerializable serverSerializable)
                        {
                            serverSerializable.ClientEventRead(msg, sendingTime);
                        }
                        else
                        {
                            throw new Exception($"Failed to read component state - {components[componentIndex].GetType()} in item \"{Prefab.Identifier}\" is not IServerSerializable.");
                        }
                    }
                    break;
                case EventType.InventoryState:
                    {
                        int containerIndex = msg.ReadRangedInteger(0, components.Count - 1);
                        if (components[containerIndex] is ItemContainer container)
                        {
                            container.Inventory.ClientEventRead(msg);
                        }
                        else
                        {
                            throw new Exception($"Failed to read inventory state - {components[containerIndex].GetType()} in item \"{Prefab.Identifier}\"  is not an ItemContainer.");
                        }
                    }
                    break;
                case EventType.Status:
                    bool loadingRound = msg.ReadBoolean();
                    float newCondition = msg.ReadSingle();
                    SetCondition(newCondition, isNetworkEvent: true, executeEffects: !loadingRound);
                    break;
                case EventType.AssignCampaignInteraction:
                    bool isVisible = msg.ReadBoolean();
                    if (isVisible)
                    {
                        var interactionType = (CampaignMode.InteractionType)msg.ReadByte();
                        AssignCampaignInteractionType(interactionType);
                    }
                    break;
                case EventType.ApplyStatusEffect:
                    {
                        ActionType actionType = (ActionType)msg.ReadRangedInteger(0, Enum.GetValues(typeof(ActionType)).Length - 1);
                        byte componentIndex         = msg.ReadByte();
                        ushort targetCharacterID    = msg.ReadUInt16();
                        byte targetLimbID           = msg.ReadByte();
                        ushort useTargetID          = msg.ReadUInt16();
                        Vector2? worldPosition      = null;
                        bool hasPosition            = msg.ReadBoolean();
                        if (hasPosition)
                        {
                            worldPosition = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                        }

                        ItemComponent targetComponent = componentIndex < components.Count ? components[componentIndex] : null;
                        Character targetCharacter = FindEntityByID(targetCharacterID) as Character;
                        Limb targetLimb = targetCharacter != null && targetLimbID < targetCharacter.AnimController.Limbs.Length ?
                            targetCharacter.AnimController.Limbs[targetLimbID] : null;
                        Entity useTarget = FindEntityByID(useTargetID);

                        if (targetComponent == null)
                        {
                            ApplyStatusEffects(actionType, 1.0f, targetCharacter, targetLimb, useTarget, isNetworkEvent: true, worldPosition: worldPosition);
                        }
                        else
                        {
                            targetComponent.ApplyStatusEffects(actionType, 1.0f, targetCharacter, targetLimb, useTarget, worldPosition: worldPosition);
                        }
                    }
                    break;
                case EventType.ChangeProperty:
                    ReadPropertyChange(msg, false);
                    break;
                case EventType.ItemStat:
                    byte length = msg.ReadByte();
                    for (int i = 0; i < length; i++)
                    {
                        var statIdentifier = INetSerializableStruct.Read<TalentStatIdentifier>(msg);
                        var statValue = msg.ReadSingle();
                        StatManager.ApplyStatDirect(statIdentifier, statValue);
                    }
                    break;
                case EventType.Upgrade:
                    Identifier identifier = msg.ReadIdentifier();
                    byte level = msg.ReadByte();
                    if (UpgradePrefab.Find(identifier) is { } upgradePrefab)
                    {
                        Upgrade upgrade = new Upgrade(this, upgradePrefab, level);

                        byte targetCount = msg.ReadByte();
                        for (int i = 0; i < targetCount; i++)
                        {
                            byte propertyCount = msg.ReadByte();
                            for (int j = 0; j < propertyCount; j++)
                            {
                                float value = msg.ReadSingle();
                                upgrade.TargetComponents.ElementAt(i).Value[j].SetOriginalValue(value);
                            }
                        }

                        AddUpgrade(upgrade, false);
                    }
                    break;
                case EventType.DroppedStack:
                    int itemCount = msg.ReadRangedInteger(0, Inventory.MaxPossibleStackSize);
                    if (itemCount > 0)
                    {
                        List<Item> droppedStack = new List<Item>();
                        for (int i = 0; i < itemCount; i++)
                        {
                            var id = msg.ReadUInt16();
                            if (FindEntityByID(id) is not Item droppedItem)
                            {
                                DebugConsole.ThrowError($"Error while reading {EventType.DroppedStack} message: could not find an item with the ID {id}.");
                            }
                            else
                            {
                                droppedStack.Add(droppedItem);
                            }
                        }
                        CreateDroppedStack(droppedStack, allowClientExecute: true);
                    }
                    else
                    {
                        RemoveFromDroppedStack(allowClientExecute: true);
                    }
                    break;
                case EventType.SetHighlight:
                    bool isTargetedForThisClient = msg.ReadBoolean();
                    if (isTargetedForThisClient)
                    {
                        bool highlight = msg.ReadBoolean();
                        ExternalHighlight = highlight;
                        if (highlight)
                        { 
                            Color highlightColor = msg.ReadColorR8G8B8A8();
                            HighlightColor = highlightColor;
                        }
                        else
                        {
                            HighlightColor = null;
                        }
                    }
                    break;
                default:
                    throw new Exception($"Malformed incoming item event: unsupported event type {eventType}");
            }
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            Exception error(string reason)
            {
                string errorMsg = $"Failed to write a network event for the item \"{Name}\" - {reason}";
                GameAnalyticsManager.AddErrorEventOnce($"Item.ClientWrite:{Name}", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return new Exception(errorMsg);
            }
            
            if (extraData is null) { throw error("event data was null"); }
            if (!(extraData is IEventData eventData)) { throw error($"event data was of the wrong type (\"{extraData.GetType().Name}\")"); }

            EventType eventType = eventData.EventType;
            msg.WriteRangedInteger((int)eventType, (int)EventType.MinValue, (int)EventType.MaxValue);
            switch (eventData)
            {
                case ComponentStateEventData componentStateEventData:
                {
                    var component = componentStateEventData.Component;
                    if (component is null) { throw error("component was null"); }
                    if (component is not IClientSerializable clientSerializable) { throw error($"component was not {nameof(IClientSerializable)}"); }
                    int componentIndex = components.IndexOf(component);
                    if (componentIndex < 0) { throw error("component did not belong to item"); }
                    msg.WriteRangedInteger(componentIndex, 0, components.Count - 1);
                    clientSerializable.ClientEventWrite(msg, extraData);
                }
                break;
                case InventoryStateEventData inventoryStateEventData:
                {
                    var container = inventoryStateEventData.Component;
                    if (container is null) { throw error("container was null"); }
                    int containerIndex = components.IndexOf(container);
                    if (containerIndex < 0) { throw error("container did not belong to item"); }
                    msg.WriteRangedInteger(containerIndex, 0, components.Count - 1);
                    container.Inventory.ClientEventWrite(msg, inventoryStateEventData);
                }
                break;
                case TreatmentEventData treatmentEventData:
                    Character targetCharacter = treatmentEventData.TargetCharacter;

                    msg.WriteUInt16(targetCharacter.ID);
                    msg.WriteByte(treatmentEventData.LimbIndex);
                    break;
                case ChangePropertyEventData changePropertyEventData:
                    WritePropertyChange(msg, changePropertyEventData, inGameEditableOnly: true);
                    editingHUDRefreshTimer = 1.0f;
                    break;
                case CombineEventData combineEventData:
                    Item combineTarget = combineEventData.CombineTarget;
                    msg.WriteUInt16(combineTarget.ID);
                    break;
                default:
                    throw error($"Unsupported event type {eventData.GetType().Name}");
            }
        }

        partial void UpdateNetPosition(float deltaTime)
        {
            if (GameMain.Client == null) { return; }

            if (parentInventory != null || body == null || !body.Enabled || Removed || (GetComponent<Projectile>() is { IsStuckToTarget: true }))
            {
                positionBuffer.Clear();
                return;
            }

            isActive = true;

            if (positionBuffer.Count > 0)
            {
                transformDirty = true;
            }

            body.CorrectPosition(positionBuffer, out Vector2 newPosition, out Vector2 newVelocity, out float newRotation, out float newAngularVelocity);
            body.LinearVelocity = newVelocity;
            body.AngularVelocity = newAngularVelocity;
            float distSqr = Vector2.DistanceSquared(newPosition, body.SimPosition);

            if (distSqr > 0.0001f ||
                Math.Abs(newRotation - body.Rotation) > 0.01f)
            {
                body.TargetPosition = newPosition;
                body.TargetRotation = newRotation;
                body.MoveToTargetPosition(lerp: true);
                if (distSqr > 10.0f * 10.0f)
                {
                    //very large change in position, we need to recheck which submarine the item is in
                    Submarine = null;
                    UpdateTransform();
                }
            }

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);
            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);
        }

        public void ClientReadPosition(IReadMessage msg, float sendingTime)
        {
            if (body == null)
            {
                string errorMsg = "Received a position update for an item with no physics body (" + Name + ")";
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#else
                if (GameSettings.CurrentConfig.VerboseLogging) { DebugConsole.ThrowError(errorMsg); }
#endif
                GameAnalyticsManager.AddErrorEventOnce("Item.ClientReadPosition:nophysicsbody", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }


            var posInfo = body.ClientRead(msg, sendingTime, parentDebugName: Name);
            msg.ReadPadBits();

            if (GetComponent<Projectile>() is { IsStuckToTarget: true }) { return; }

            if (posInfo != null)
            {
                int index = 0;
                while (index < positionBuffer.Count && sendingTime > positionBuffer[index].Timestamp)
                {
                    index++;
                }

                positionBuffer.Insert(index, posInfo);
            }
            /*body.FarseerBody.Awake = awake;
            if (body.FarseerBody.Awake)
            {
                if ((newVelocity - body.LinearVelocity).LengthSquared() > 8.0f * 8.0f) body.LinearVelocity = newVelocity;
            }
            else
            {
                try
                {
                    body.FarseerBody.Enabled = false;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Exception in PhysicsBody.Enabled = false (" + body.PhysEnabled + ")", e);
                    if (body.UserData != null) DebugConsole.NewMessage("PhysicsBody UserData: " + body.UserData.GetType().ToString(), GUIStyle.Red);
                    if (GameMain.World.ContactManager == null) DebugConsole.NewMessage("ContactManager is null!", GUIStyle.Red);
                    else if (GameMain.World.ContactManager.BroadPhase == null) DebugConsole.NewMessage("Broadphase is null!", GUIStyle.Red);
                    if (body.FarseerBody.FixtureList == null) DebugConsole.NewMessage("FixtureList is null!", GUIStyle.Red);
                }
            }

            if ((newPosition - SimPosition).Length() > body.LinearVelocity.Length() * 2.0f)
            {
                if (body.SetTransform(newPosition, newRotation))
                {
                    Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);
                    rect.X = (int)(displayPos.X - rect.Width / 2.0f);
                    rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);
                }
            }*/
        }

        public void CreateClientEvent<T>(T ic) where T : ItemComponent, IClientSerializable
            => CreateClientEvent(ic, null);

        public void CreateClientEvent<T>(T ic, ItemComponent.IEventData extraData) where T : ItemComponent, IClientSerializable
        {
            if (GameMain.Client == null) { return; }

            #warning TODO: this should throw an exception
            if (!components.Contains(ic)) { return; }

            var eventData = new ComponentStateEventData(ic, extraData);
            if (!ic.ValidateEventData(eventData)) { throw new Exception($"Component event creation failed: {typeof(T).Name}.{nameof(ItemComponent.ValidateEventData)} returned false"); }
            GameMain.Client.CreateEntityEvent(this, eventData);
        }

        public static Item ReadSpawnData(IReadMessage msg, bool spawn = true)
        {
            string itemName = msg.ReadString();
            string itemIdentifier = msg.ReadString();
            bool descriptionChanged = msg.ReadBoolean();
            string itemDesc = "";
            if (descriptionChanged)
            {
                itemDesc = msg.ReadString();
            }
            ushort itemId = msg.ReadUInt16();
            ushort inventoryId = msg.ReadUInt16();

            DebugConsole.Log($"Received entity spawn message for item \"{itemName}\" (identifier: {itemIdentifier}, id: {itemId})");

            ItemPrefab itemPrefab = 
                string.IsNullOrEmpty(itemIdentifier) ?
                ItemPrefab.Find(itemName, Identifier.Empty) :
                ItemPrefab.Find(itemName, itemIdentifier.ToIdentifier());

            Vector2 pos = Vector2.Zero;
            Submarine sub = null;
            float rotation = 0.0f;
            int itemContainerIndex = -1;
            int inventorySlotIndex = -1;

            if (inventoryId > 0)
            {
                itemContainerIndex = msg.ReadByte();
                inventorySlotIndex = msg.ReadByte();
            }
            else
            {
                pos = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                rotation = msg.ReadRangedSingle(0, MathHelper.TwoPi, 8);
                ushort subID = msg.ReadUInt16();
                if (subID > 0)
                {
                    sub = Submarine.Loaded.Find(s => s.ID == subID);
                }
            }

            byte bodyType           = msg.ReadByte();
            bool spawnedInOutpost   = msg.ReadBoolean();
            bool allowStealing      = msg.ReadBoolean();
            int quality             = msg.ReadRangedInteger(0, Items.Components.Quality.MaxQuality);
            byte teamID             = msg.ReadByte();

            bool hasIdCard          = msg.ReadBoolean();
            string ownerName = "", ownerTags = "";
            int ownerBeardIndex = -1, ownerHairIndex = -1, ownerMoustacheIndex = -1, ownerFaceAttachmentIndex = -1;
            Color ownerHairColor = Color.White,
                ownerFacialHairColor = Color.White,
                ownerSkinColor = Color.White;
            Identifier ownerJobId = Identifier.Empty;
            Vector2 ownerSheetIndex = Vector2.Zero;
            int submarineSpecificId = 0;
            if (hasIdCard)
            {
                submarineSpecificId = msg.ReadInt32();
                ownerName = msg.ReadString();
                ownerTags = msg.ReadString();                
                ownerBeardIndex = msg.ReadByte() - 1;
                ownerHairIndex = msg.ReadByte() - 1;
                ownerMoustacheIndex = msg.ReadByte() - 1;
                ownerFaceAttachmentIndex = msg.ReadByte() - 1;                
                ownerHairColor = msg.ReadColorR8G8B8();
                ownerFacialHairColor = msg.ReadColorR8G8B8();
                ownerSkinColor = msg.ReadColorR8G8B8();                
                ownerJobId = msg.ReadIdentifier();
                
                int x = msg.ReadByte();
                int y = msg.ReadByte();
                ownerSheetIndex = (x, y);
            }
            
            bool tagsChanged        = msg.ReadBoolean();
            string tags = "";
            if (tagsChanged)
            {
                HashSet<Identifier> addedTags = msg.ReadString().Split(',').ToIdentifiers().ToHashSet();
                HashSet<Identifier> removedTags = msg.ReadString().Split(',').ToIdentifiers().ToHashSet();
                if (itemPrefab != null)
                {
                    tags = string.Join(',',itemPrefab.Tags.Where(t => !removedTags.Contains(t)).Concat(addedTags));
                }
            }
            
            bool isNameTag = msg.ReadBoolean();
            string writtenName = "";
            if (isNameTag)
            {
                writtenName = msg.ReadString();
            }

            if (!spawn) { return null; }

            //----------------------------------------

            if (itemPrefab == null)
            {
                string errorMsg = "Failed to spawn item, prefab not found (name: " + (itemName ?? "null") + ", identifier: " + (itemIdentifier ?? "null") + ")";
                errorMsg += "\n" + string.Join(", ", ContentPackageManager.EnabledPackages.All.Select(cp => cp.Name));
                GameAnalyticsManager.AddErrorEventOnce("Item.ReadSpawnData:PrefabNotFound" + (itemName ?? "null") + (itemIdentifier ?? "null"),
                    GameAnalyticsManager.ErrorSeverity.Critical,
                    errorMsg);
                DebugConsole.ThrowError(errorMsg);
                return null;
            }

            Inventory inventory = null;
            if (inventoryId > 0)
            {
                var inventoryOwner = FindEntityByID(inventoryId);
                if (inventoryOwner is Character character)
                {
                    inventory = character.Inventory;
                }
                else if (inventoryOwner is Item parentItem)
                {
                    if (itemContainerIndex < 0 || itemContainerIndex >= parentItem.components.Count)
                    {
                        string errorMsg =
                            $"Failed to spawn item \"{(itemIdentifier ?? "null")}\" in the inventory of \"{parentItem.Prefab.Identifier} ({parentItem.ID})\" (component index out of range). Index: {itemContainerIndex}, components: {parentItem.components.Count}.";
                        GameAnalyticsManager.AddErrorEventOnce("Item.ReadSpawnData:ContainerIndexOutOfRange" + (itemName ?? "null") + (itemIdentifier ?? "null"),
                            GameAnalyticsManager.ErrorSeverity.Error,
                            errorMsg);
                        DebugConsole.ThrowError(errorMsg);
                        inventory = parentItem.GetComponent<ItemContainer>()?.Inventory;
                    }
                    else if (parentItem.components[itemContainerIndex] is ItemContainer container)
                    {
                        inventory = container.Inventory;
                    }
                }
                else if (inventoryOwner == null)
                {
                    DebugConsole.ThrowError($"Failed to spawn item \"{(itemIdentifier ?? "null")}\" in the inventory of an entity with the ID {inventoryId} (entity not found)");
                }
                else
                {
                    DebugConsole.ThrowError($"Failed to spawn item \"{(itemIdentifier ?? "null")}\" in the inventory of \"{inventoryOwner} ({inventoryOwner.ID})\" (invalid entity, should be an item or a character)");
                }
            }

            Item item = null;
            try
            {
                item = new Item(itemPrefab, pos, sub, id: itemId)
                {
                    SpawnedInCurrentOutpost = spawnedInOutpost,
                    AllowStealing = allowStealing,
                    Quality = quality
                };
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Failed to spawn item {itemPrefab.Name}", e);
                throw;
            }

            if (item.body != null)
            {
                item.body.BodyType = (BodyType)bodyType;
            }

            foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
            {
                wifiComponent.TeamID = (CharacterTeamType)teamID;
            }
            foreach (IdCard idCard in item.GetComponents<IdCard>())
            {
                idCard.SubmarineSpecificID = submarineSpecificId;
                idCard.TeamID = (CharacterTeamType)teamID;
                idCard.OwnerName = ownerName;
                idCard.OwnerTags = ownerTags;
                idCard.OwnerBeardIndex = ownerBeardIndex;
                idCard.OwnerHairIndex = ownerHairIndex;
                idCard.OwnerMoustacheIndex = ownerMoustacheIndex;
                idCard.OwnerFaceAttachmentIndex = ownerFaceAttachmentIndex;
                idCard.OwnerHairColor = ownerHairColor;
                idCard.OwnerFacialHairColor = ownerFacialHairColor;
                idCard.OwnerSkinColor = ownerSkinColor;
                idCard.OwnerJobId = ownerJobId;
                idCard.OwnerSheetIndex = ownerSheetIndex;
            }
            if (descriptionChanged) { item.Description = itemDesc; }
            if (tagsChanged) { item.Tags = tags; }
            var nameTag = item.GetComponent<NameTag>();
            if (nameTag != null)
            {
                nameTag.WrittenName = writtenName;
            }

            if (sub != null)
            {
                item.CurrentHull = Hull.FindHull(pos + sub.Position, null, true);
                item.Submarine = item.CurrentHull?.Submarine;
            }

            if (inventory != null)
            {
                if (inventorySlotIndex >= 0 && inventorySlotIndex < 255 &&
                    inventory.TryPutItem(item, inventorySlotIndex, false, false, null, false))
                {
                    return item;
                }
                inventory.TryPutItem(item, null, item.AllowedSlots, false);
            }

            return item;
        }

        partial void RemoveProjSpecific()
        {
            if (Inventory.DraggingItems.Contains(this))
            {
                Inventory.DraggingItems.Clear();
                Inventory.DraggingSlot = null;
            }
        }

        public void OnPlayerSkillsChanged()
        {
            foreach (ItemComponent ic in components)
            {
                ic.OnPlayerSkillsChanged();
            }
        }
    }
}
