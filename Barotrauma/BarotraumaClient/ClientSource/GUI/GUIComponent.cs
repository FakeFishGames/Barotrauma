using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using System;
using System.Xml.Linq;
using Barotrauma.IO;
using RestSharp;
using System.Net;
using System.Collections.Immutable;
using Barotrauma.Tutorials;

namespace Barotrauma
{
    public enum SlideDirection { Up, Down, Left, Right }

    public abstract class GUIComponent
    {
        #region Hierarchy
        public GUIComponent Parent => RectTransform.Parent?.GUIComponent;

        public CursorState HoverCursor = CursorState.Default;

        public bool AlwaysOverrideCursor = false;
        
        public delegate bool SecondaryButtonDownHandler(GUIComponent component, object userData);
        public SecondaryButtonDownHandler OnSecondaryClicked;
        
        public IEnumerable<GUIComponent> Children => RectTransform.Children.Select(c => c.GUIComponent);

        public T GetChild<T>() where T : GUIComponent
        {
            return Children.FirstOrDefault(c => c is T) as T;
        }

        public T GetAnyChild<T>() where T : GUIComponent
        {
            return GetAllChildren().FirstOrDefault(c => c is T) as T;
        }

        public IEnumerable<T> GetAllChildren<T>() where T : GUIComponent
        {
            return GetAllChildren().OfType<T>();
        }

        /// <summary>
        /// Returns all child elements in the hierarchy.
        /// </summary>
        public IEnumerable<GUIComponent> GetAllChildren()
        {
            return RectTransform.GetAllChildren().Select(c => c.GUIComponent);
        }

        public GUIComponent GetChild(int index)
        {
            if (index < 0 || index >= CountChildren) { return null; }
            return RectTransform.GetChild(index).GUIComponent;
        }

        public int GetChildIndex(GUIComponent child)
        {
            if (child == null) { return -1; }
            return RectTransform.GetChildIndex(child.RectTransform);
        }

        public GUIComponent GetChildByUserData(object obj)
        {
            foreach (GUIComponent child in Children)
            {
                if (Equals(child.UserData, obj)) { return child; }
            }
            return null;
        }

        public bool IsParentOf(GUIComponent component, bool recursive = true)
        {
            if (component == null) { return false; }
            return RectTransform.IsParentOf(component.RectTransform, recursive);
        }

        public bool IsChildOf(GUIComponent component, bool recursive = true)
        {
            if (component == null) { return false; }
            return RectTransform.IsChildOf(component.RectTransform, recursive);
        }

        public virtual void RemoveChild(GUIComponent child)
        {
            if (child == null) { return; }
            child.RectTransform.Parent = null;
        }

        // TODO: refactor?

        public GUIComponent FindChild(Func<GUIComponent, bool> predicate, bool recursive = false)
        {
            var matchingChild = Children.FirstOrDefault(predicate);
            if (recursive && matchingChild == null)
            {
                foreach (GUIComponent child in Children)
                {
                    matchingChild = child.FindChild(predicate, recursive);
                    if (matchingChild != null) return matchingChild;
                }
            }

            return matchingChild;
        }
        public GUIComponent FindChild(object userData, bool recursive = false)
        {
            var matchingChild = Children.FirstOrDefault(c => Equals(c.UserData, userData));
            if (recursive && matchingChild == null)
            {
                foreach (GUIComponent child in Children)
                {
                    matchingChild = child.FindChild(userData, recursive);
                    if (matchingChild != null) return matchingChild;
                }
            }

            return matchingChild;
        }

        public IEnumerable<GUIComponent> FindChildren(object userData)
        {
            return Children.Where(c => Equals(c.UserData, userData));
        }

        public IEnumerable<GUIComponent> FindChildren(Func<GUIComponent, bool> predicate)
        {
            return Children.Where(c => predicate(c));
        }

        public virtual void ClearChildren()
        {
            RectTransform.ClearChildren();
        }

        public void SetAsFirstChild()
        {
            RectTransform.SetAsFirstChild();
        }

        public void SetAsLastChild()
        {
            RectTransform.SetAsLastChild();
        }
        #endregion

        public bool AutoUpdate { get; set; } = true;
        public bool AutoDraw { get; set; } = true;
        public int UpdateOrder { get; set; }
        
        public bool Bounce { get; set; }
        private float bounceTimer;
        private float bounceJump;
        private bool bounceDown;

        public Action<GUIComponent> OnAddedToGUIUpdateList;

        public enum ComponentState { None, Hover, Pressed, Selected, HoverSelected };

        protected Alignment alignment;

        protected Identifier[] styleHierarchy;

        public bool CanBeFocused;

        protected Color color;
        protected Color hoverColor;
        protected Color selectedColor;
        protected Color disabledColor;
        protected Color pressedColor;

        public bool GlowOnSelect { get; set; }

        public Vector2 UVOffset { get; set; }

        private CoroutineHandle pulsateCoroutine;

        protected Color flashColor;
        protected float flashDuration = 1.5f;
        // TODO: We should use an enum for the flash modes, but it would require a bit of refactoring, because Flash method is use in so many places.
        private bool useRectangleFlash;
        private bool useCircularFlash;
        public virtual float FlashTimer
        {
            get { return flashTimer; }
        }
        protected float flashTimer;
        private Vector2 flashRectInflate;

        private bool ignoreLayoutGroups;
        public bool IgnoreLayoutGroups
        {
            get { return ignoreLayoutGroups; }
            set 
            {
                if (ignoreLayoutGroups == value) { return; }
                ignoreLayoutGroups = value;
                if (Parent is GUILayoutGroup layoutGroup)
                {
                    layoutGroup.NeedsToRecalculate = true;
                }
            }
        }

        public virtual GUIFont Font
        {
            get;
            set;
        }
        
        private RichString toolTip;
        public virtual RichString ToolTip
        {
            get
            {
                return toolTip;
            }
            set
            {
                toolTip = value;
            }
        }

        public GUIComponentStyle Style
            => GUIComponentStyle.FromHierarchy(styleHierarchy);

        public bool Visible
        {
            get;
            set;
        }

        protected bool enabled;
        public virtual bool Enabled
        {
            get { return enabled; }
            set { enabled = value; }
        }

        private static GUITextBlock toolTipBlock;

        public Vector2 Center
        {
            get { return new Vector2(Rect.Center.X, Rect.Center.Y); }
        }

        protected Rectangle ClampRect(Rectangle r)
        {
            if (Parent is null) { return r; }
            Rectangle parentRect = !Parent.ClampMouseRectToParent ? Parent.Rect : Parent.ClampRect(Parent.Rect);
            if (parentRect.Width <= 0 || parentRect.Height <= 0) { return Rectangle.Empty; }
            if (parentRect.X > r.X)
            {
                int diff = parentRect.X - r.X;
                r.X = parentRect.X;
                r.Width -= diff;
            }
            if (parentRect.Y > r.Y)
            {
                int diff = parentRect.Y - r.Y;
                r.Y = parentRect.Y;
                r.Height -= diff;
            }
            if (parentRect.X + parentRect.Width < r.X + r.Width)
            {
                int diff = (r.X + r.Width) - (parentRect.X + parentRect.Width);
                r.Width -= diff;
            }
            if (parentRect.Y + parentRect.Height < r.Y + r.Height)
            {
                int diff = (r.Y + r.Height) - (parentRect.Y + parentRect.Height);
                r.Height -= diff;
            }
            if (r.Width <= 0 || r.Height <= 0) { return Rectangle.Empty; }
            return r;
        }

        public virtual Rectangle Rect
        {
            get { return RectTransform.Rect; }
        }

        public bool ClampMouseRectToParent { get; set; } = false;

        public virtual Rectangle MouseRect
        {
            get
            {
                if (!CanBeFocused) { return Rectangle.Empty; }

                return ClampMouseRectToParent ? ClampRect(Rect) : Rect;
            }
        }

        public Dictionary<ComponentState, List<UISprite>> sprites;

        public SpriteEffects SpriteEffects;

        public virtual Color OutlineColor { get; set; }

        protected ComponentState _state;
        protected ComponentState _previousState;
        protected bool isSelected;
        public virtual bool Selected
        {
            get { return isSelected; }
            set
            {
                isSelected = value;
                foreach (var child in Children)
                {
                    child.Selected = value;
                }
            }
        }
        public virtual ComponentState State
        {
            get { return _state; }
            set
            {
                if (_state != value)
                {
                    spriteFadeTimer = SpriteCrossFadeTime;
                    colorFadeTimer = ColorCrossFadeTime;
                    _previousState = _state;
                }
                _state = value;
            }
        }

        #warning TODO: this is cursed, stop using this
        public object UserData;
        
        public int CountChildren
        {
            get { return RectTransform.CountChildren; }
        }
        
        /// <summary>
        /// Currently only used for the fade effect in GUIListBox, should be set to the same value as Color but only assigned once
        /// </summary>
        public Color DefaultColor { get; set; }

        public virtual Color Color
        {
            get { return color; }
            set { color = value; }
        }

        public virtual Color HoverColor
        {
            get { return hoverColor; }
            set { hoverColor = value; }
        }

        public virtual Color SelectedColor
        {
            get { return selectedColor; }
            set { selectedColor = value; }
        }
        public virtual Color DisabledColor
        {
            get { return disabledColor; }
            set { disabledColor = value; }
        }

        public virtual Color PressedColor
        {
            get { return pressedColor; }
            set { pressedColor = value; }
        }

        public TransitionMode ColorTransition { get; private set; }
        public SpriteFallBackState FallBackState { get; private set; } 
        public float SpriteCrossFadeTime { get; private set; }
        public float ColorCrossFadeTime { get; private set; }

        private float spriteFadeTimer;
        private float colorFadeTimer;

        public bool ExternalHighlight = false;

        public virtual bool PlaySoundOnSelect { get; set; } = false;

        private RectTransform rectTransform;
        public RectTransform RectTransform
        {
            get { return rectTransform; }
            private set
            {
                rectTransform = value;
                // This is the only place where the element should be assigned!
                if (rectTransform != null)
                {
                    rectTransform.GUIComponent = this;
                }
            }
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        protected GUIComponent(string style, RectTransform rectT)
        {
            RectTransform = rectT;

            Visible = true;
            OutlineColor = Color.Transparent;
            Font = GUIStyle.Font;
            CanBeFocused = true;

            if (style != null) { GUIStyle.Apply(this, style); }
        }

        protected GUIComponent(string style)
        {
            Visible = true;
            OutlineColor = Color.Transparent;
            Font = GUIStyle.Font;
            CanBeFocused = true;

            if (style != null) { GUIStyle.Apply(this, style); }
        }

        #region Updating
        public virtual void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            if (!Visible) { return; }

            UpdateOrder = order;
            GUI.AddToUpdateList(this);
            if (!ignoreChildren)
            {
                RectTransform.AddChildrenToGUIUpdateList(ignoreChildren, order);
            }
            OnAddedToGUIUpdateList?.Invoke(this);
        }

        public void RemoveFromGUIUpdateList(bool alsoChildren = true)
        {
            GUI.RemoveFromUpdateList(this, alsoChildren);
        }

        /// <summary>
        /// Only GUI should call this method. Auto updating follows the order of GUI update list. This order can be tweaked by changing the UpdateOrder property.
        /// </summary>
        public void UpdateAuto(float deltaTime)
        {
            if (AutoUpdate)
            {
                Update(deltaTime);
            }
        }

        /// <summary>
        /// By default, all the gui elements are updated automatically in the same order they appear on the update list. 
        /// </summary>
        public void UpdateManually(float deltaTime, bool alsoChildren = false, bool recursive = true)
        {
            if (!Visible) { return; }

            AutoUpdate = false;
            Update(deltaTime);
            if (alsoChildren)
            {
                UpdateChildren(deltaTime, recursive);
            }
        }

        protected virtual void Update(float deltaTime)
        {
            if (!Visible) { return; }
            
            if (CanBeFocused && OnSecondaryClicked != null)
            {
                if (GUI.IsMouseOn(this) && PlayerInput.SecondaryMouseButtonClicked())
                {
                    OnSecondaryClicked?.Invoke(this, UserData);
                }
            }

            if (Bounce)
            {
                if (bounceTimer > 3.0f || bounceDown)
                {
                    RectTransform.ScreenSpaceOffset = new Point(RectTransform.ScreenSpaceOffset.X, (int) -(bounceJump * 15f * GUI.Scale));
                    if (!bounceDown)
                    {
                        bounceJump += deltaTime * 4;
                        if (bounceJump > 0.5f)
                        {
                            bounceDown = true;
                        }
                    }
                    else
                    {
                        bounceJump -= deltaTime * 4;
                        if (bounceJump <= 0.0f)
                        {
                            bounceJump = 0.0f;
                            bounceTimer = 0.0f;
                            bounceDown = false;
                            Bounce = false;
                        }
                    }
                }
                else
                {
                    bounceTimer += deltaTime;
                }
            }
            
            if (flashTimer > 0.0f)
            {
                flashTimer -= deltaTime;
            }
            if (spriteFadeTimer > 0)
            {
                spriteFadeTimer -= deltaTime;
            }
            if (colorFadeTimer > 0)
            {
                colorFadeTimer -= deltaTime;
            }
        }

        public virtual void ForceLayoutRecalculation()
        {
            //This is very ugly but it gets the job done, it
            //would be real nice to un-jank this some day
            ForceUpdate();
            ForceUpdate();
            foreach (var child in Children) 
            { 
                child.ForceLayoutRecalculation(); 
            }
        }

        public void ForceUpdate() => Update((float)Timing.Step);

        /// <summary>
        /// Updates all the children manually.
        /// </summary>
        public void UpdateChildren(float deltaTime, bool recursive)
        {
            foreach (var child in RectTransform.Children)
            {
                child.GUIComponent.UpdateManually(deltaTime, recursive, recursive);
            }
        }
        #endregion

        #region Drawing
        /// <summary>
        /// Only GUI should call this method. Auto drawing follows the order of GUI update list. This order can be tweaked by changing the UpdateOrder property.
        /// </summary>
        public void DrawAuto(SpriteBatch spriteBatch)
        {
            if (AutoDraw)
            {
                Draw(spriteBatch);
            }
        }

        /// <summary>
        /// By default, all the gui elements are drawn automatically in the same order they appear on the update list.
        /// </summary>
        public virtual void DrawManually(SpriteBatch spriteBatch, bool alsoChildren = false, bool recursive = true)
        {
            if (!Visible) { return; }

            AutoDraw = false;
            Draw(spriteBatch);
            if (alsoChildren)
            {
                DrawChildren(spriteBatch, recursive);
            }
        }

        /// <summary>
        /// Draws all the children manually.
        /// </summary>
        public virtual void DrawChildren(SpriteBatch spriteBatch, bool recursive)
        {
            foreach (RectTransform child in RectTransform.Children)
            {
                child.GUIComponent.DrawManually(spriteBatch, recursive, recursive);
            }
        }

        protected Color _currentColor;

        protected virtual Color GetColor(ComponentState state)
        {
            if (!Enabled) { return DisabledColor; }
            if (ExternalHighlight) { return HoverColor; }

            return state switch
            {
                ComponentState.Hover => HoverColor,
                ComponentState.HoverSelected => HoverColor,
                ComponentState.Pressed => PressedColor,
                ComponentState.Selected when !GlowOnSelect => SelectedColor,
                _ => Color,
            };
        }

        protected Color GetBlendedColor(Color targetColor, ref Color blendedColor)
        {
            blendedColor = ColorCrossFadeTime > 0 ? Color.Lerp(blendedColor, targetColor, MathUtils.InverseLerp(ColorCrossFadeTime, 0, ToolBox.GetEasing(ColorTransition, colorFadeTimer))) : targetColor;
            return blendedColor;
        }

        protected virtual void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) { return; }
            var rect = Rect;

            GetBlendedColor(GetColor(State), ref _currentColor);

            if (_currentColor.A > 0.0f && (sprites == null || !sprites.Any()))
            {
                GUI.DrawRectangle(spriteBatch, rect, _currentColor * (_currentColor.A / 255.0f), true);
            }

            if (sprites != null && _currentColor.A > 0)
            {
                if (!sprites.TryGetValue(_previousState, out List<UISprite> previousSprites) || previousSprites.None())
                {
                    switch (FallBackState)
                    {
                        case SpriteFallBackState.Toggle:
                            sprites.TryGetValue(Selected ? ComponentState.Selected : ComponentState.None, out previousSprites);
                            break;
                        default:
                            if (Enum.TryParse(FallBackState.ToString(), ignoreCase: true, out ComponentState fallBackState))
                            {
                                sprites.TryGetValue(fallBackState, out previousSprites);
                            }
                            break;
                    }
                }
                // Handle fallbacks when some of the sprites are not defined
                if (!sprites.TryGetValue(State, out List<UISprite> currentSprites) || currentSprites.None())
                {
                    switch (FallBackState)
                    {
                        case SpriteFallBackState.Toggle:
                            sprites.TryGetValue(Selected ? ComponentState.Selected : ComponentState.None, out currentSprites);
                            break;
                        default:
                            if (Enum.TryParse(FallBackState.ToString(), ignoreCase: true, out ComponentState fallBackState))
                            {
                                sprites.TryGetValue(fallBackState, out currentSprites);
                            }
                            break;
                    }
                }
                if (_previousState != State && currentSprites != previousSprites)
                {
                    if (previousSprites != null && previousSprites.Any())
                    {
                        // Draw the previous sprites(s) only while cross fading out
                        Color previousColor = GetColor(_previousState);
                        foreach (UISprite uiSprite in previousSprites)
                        {
                            float alphaMultiplier = SpriteCrossFadeTime > 0 && (uiSprite.CrossFadeOut || currentSprites != null && currentSprites.Any(s => s.CrossFadeIn))
                                ? MathUtils.InverseLerp(0, SpriteCrossFadeTime, ToolBox.GetEasing(uiSprite.TransitionMode, spriteFadeTimer)) : 0;
                            if (alphaMultiplier > 0)
                            {
                                uiSprite.Draw(spriteBatch, rect, previousColor * alphaMultiplier, SpriteEffects, uvOffset: UVOffset);
                            }
                        }
                    }
                }
                if (currentSprites != null && currentSprites.Any())
                {
                    // Draw the current sprite(s)
                    foreach (UISprite uiSprite in currentSprites)
                    {
                        float alphaMultiplier = SpriteCrossFadeTime > 0 && (uiSprite.CrossFadeIn || previousSprites != null && previousSprites.Any(s => s.CrossFadeOut))
                            ? MathUtils.InverseLerp(SpriteCrossFadeTime, 0, ToolBox.GetEasing(uiSprite.TransitionMode, spriteFadeTimer)) : (_currentColor.A / 255.0f);
                        if (alphaMultiplier > 0)
                        {
                            //  * (rect.Location.Y - PlayerInput.MousePosition.Y) / rect.Height
                            Vector2 offset = new Vector2(
                                MathUtils.PositiveModulo((int)-UVOffset.X, uiSprite.Sprite.SourceRect.Width),
                                MathUtils.PositiveModulo((int)-UVOffset.Y, uiSprite.Sprite.SourceRect.Height));
                            uiSprite.Draw(spriteBatch, rect, _currentColor * alphaMultiplier, SpriteEffects, uvOffset: offset);
                        }
                    }
                }
            }

            if (GlowOnSelect && State == ComponentState.Selected)
            {
                GUIStyle.UIGlow.Draw(spriteBatch, Rect, SelectedColor);
            }

            if (flashTimer > 0.0f)
            {
                //the number of flashes depends on the duration, 1 flash per 1 full second
                int flashCycleCount = (int)Math.Max(flashDuration, 1);
                float flashCycleDuration = flashDuration / flashCycleCount;

                Rectangle flashRect = Rect;
                flashRect.Inflate(flashRectInflate.X, flashRectInflate.Y);

                //MathHelper.Pi * 0.8f -> the curve goes from 144 deg to 0, 
                //i.e. quickly bumps up from almost full brightness to full and then fades out
                if (useRectangleFlash)
                {
                    GUI.DrawRectangle(spriteBatch, flashRect, flashColor * (float)Math.Sin(flashTimer % flashCycleDuration / flashCycleDuration * MathHelper.Pi * 0.8f), true);
                }
                else
                {
                    var glow = useCircularFlash ? GUIStyle.UIGlowCircular : GUIStyle.UIGlow;
                    glow.Draw(spriteBatch,
                        flashRect,
                        flashColor * (float)Math.Sin(flashTimer % flashCycleDuration / flashCycleDuration * MathHelper.Pi * 0.8f));
                }
            }
        }

        /// <summary>
        /// Creates and draws a tooltip.
        /// </summary>
        public void DrawToolTip(SpriteBatch spriteBatch)
        {
            if (!Visible) { return; }
            DrawToolTip(spriteBatch, ToolTip, Rect);
        }
        
        public static void DrawToolTip(SpriteBatch spriteBatch, RichString toolTip, Vector2 pos)
        {
            if (ObjectiveManager.ContentRunning) { return; }

            int width = (int)(400 * GUI.Scale);
            int height = (int)(18 * GUI.Scale);
            Point padding = new Point((int)(10 * GUI.Scale));

            if (toolTipBlock == null || (RichString)toolTipBlock.UserData != toolTip)
            {
                toolTipBlock = new GUITextBlock(new RectTransform(new Point(width, height), null), toolTip, font: GUIStyle.SmallFont, wrap: true, style: "GUIToolTip");
                toolTipBlock.RectTransform.NonScaledSize = new Point(
                    (int)(GUIStyle.SmallFont.MeasureString(toolTipBlock.WrappedText).X + padding.X + toolTipBlock.Padding.X + toolTipBlock.Padding.Z),
                    (int)(GUIStyle.SmallFont.MeasureString(toolTipBlock.WrappedText).Y + padding.Y + toolTipBlock.Padding.Y + toolTipBlock.Padding.W));
                toolTipBlock.UserData = toolTip;
            }

            toolTipBlock.RectTransform.AbsoluteOffset = pos.ToPoint();
            toolTipBlock.SetTextPos();

            toolTipBlock.DrawManually(spriteBatch);
        }

        public static void DrawToolTip(SpriteBatch spriteBatch, RichString toolTip, Rectangle targetElement, Anchor anchor = Anchor.BottomCenter, Pivot pivot = Pivot.TopLeft)
        {
            if (ObjectiveManager.ContentRunning) { return; }

            int width = (int)(400 * GUI.Scale);
            int height = (int)(18 * GUI.Scale);
            Point padding = new Point((int)(10 * GUI.Scale));

            if (toolTipBlock == null || (RichString)toolTipBlock.UserData != toolTip)
            {
                toolTipBlock = new GUITextBlock(new RectTransform(new Point(width, height), null), toolTip, font: GUIStyle.SmallFont, wrap: true, style: "GUIToolTip");
                toolTipBlock.RectTransform.NonScaledSize = new Point(
                    (int)(toolTipBlock.Font.MeasureString(toolTipBlock.WrappedText).X + padding.X + toolTipBlock.Padding.X + toolTipBlock.Padding.Z),
                    (int)(toolTipBlock.Font.MeasureString(toolTipBlock.WrappedText).Y + padding.Y + toolTipBlock.Padding.Y + toolTipBlock.Padding.W));
                toolTipBlock.UserData = toolTip;
            }

            toolTipBlock.RectTransform.AbsoluteOffset =
                RectTransform.CalculateAnchorPoint(anchor, targetElement) +
                RectTransform.CalculatePivotOffset(pivot, toolTipBlock.RectTransform.NonScaledSize);

            if (toolTipBlock.Rect.Right > GameMain.GraphicsWidth - 10)
            {
                toolTipBlock.RectTransform.AbsoluteOffset -= new Point(toolTipBlock.Rect.Width, 0);
            }
            if (toolTipBlock.Rect.Bottom > GameMain.GraphicsHeight - 10)
            {
                toolTipBlock.RectTransform.AbsoluteOffset -= new Point(
                    0, 
                    toolTipBlock.Rect.Bottom - (GameMain.GraphicsHeight - 10));
            }
            toolTipBlock.SetTextPos();

            toolTipBlock.DrawManually(spriteBatch);
        }
        #endregion

        protected virtual void SetAlpha(float a)
        {
            color = new Color(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, a);
            hoverColor = new Color(hoverColor.R / 255.0f, hoverColor.G / 255.0f, hoverColor.B / 255.0f, a);;
        }

        public virtual void Flash(Color? color = null, float flashDuration = 1.5f, bool useRectangleFlash = false, bool useCircularFlash = false, Vector2? flashRectInflate = null)
        {
            flashTimer = flashDuration;
            this.flashRectInflate = flashRectInflate ?? Vector2.Zero;            
            this.useRectangleFlash = useRectangleFlash;
            this.useCircularFlash = useCircularFlash;
            this.flashDuration = flashDuration;
            flashColor = (color == null) ? GUIStyle.Red : (Color)color;
        }

        public void ImmediateFlash(Color? color = null)
        {
            flashTimer = MathHelper.Pi / 4.0f * 0.1f;
            flashDuration = 1.0f *0.1f;
            flashColor = (color == null) ? GUIStyle.Red : (Color)color;
        }

        public void FadeOut(float duration, bool removeAfter, float wait = 0.0f, Action onRemove = null)
        {
            CoroutineManager.StartCoroutine(LerpAlpha(0.0f, duration, removeAfter, wait, onRemove));
        }

        public void FadeIn(float wait, float duration)
        {
            SetAlpha(0.0f);
            CoroutineManager.StartCoroutine(LerpAlpha(1.0f, duration, false, wait));
        }

        public void SlideIn(float wait, float duration, int amount, SlideDirection direction)
        {
            RectTransform.ScreenSpaceOffset = direction switch
            {
                SlideDirection.Up    => new Point(0, amount),
                SlideDirection.Down  => new Point(0, -amount),
                SlideDirection.Left  => new Point(amount, 0),
                SlideDirection.Right => new Point(-amount, 0),
                _ => RectTransform.ScreenSpaceOffset
            };
            CoroutineManager.StartCoroutine(SlideToPosition(duration, wait, Vector2.Zero));
        }

        public void SlideOut(float duration, int amount, SlideDirection direction)
        {
            RectTransform.ScreenSpaceOffset = Point.Zero;

            Vector2 targetPos = direction switch
            {
                SlideDirection.Up    => new Vector2(0, amount),
                SlideDirection.Down  => new Vector2(0, -amount),
                SlideDirection.Left  => new Vector2(amount, 0),
                SlideDirection.Right => new Vector2(-amount, 0),
                _ => Vector2.Zero
            };

            CoroutineManager.StartCoroutine(SlideToPosition(duration, 0.0f, targetPos));
        }

        private IEnumerable<CoroutineStatus> SlideToPosition(float duration, float wait, Vector2 target)
        {
            float t = 0.0f;
            var (startX, startY) = RectTransform.ScreenSpaceOffset.ToVector2();
            var (endX, endY) = target;
            while (t < wait)
            {
                t += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }
            t = 0.0f;

            while (t < duration)
            {
                t += CoroutineManager.DeltaTime;
                RectTransform.ScreenSpaceOffset = new Point((int)MathHelper.Lerp(startX, endX, t / duration), (int)MathHelper.Lerp(startY, endY, t / duration));
                yield return CoroutineStatus.Running;
            }

            RectTransform.ScreenSpaceOffset = new Point(0, 0);

            yield return CoroutineStatus.Success;
        }

        private IEnumerable<CoroutineStatus> LerpAlpha(float to, float duration, bool removeAfter, float wait = 0.0f, Action onRemove = null)
        {
            State = ComponentState.None;
            float t = 0.0f;
            float startA = color.A / 255.0f;

            while (t < wait)
            {
                t += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }
            t = 0.0f;

            while (t < duration)
            {
                t += CoroutineManager.DeltaTime;
                SetAlpha(MathHelper.Lerp(startA, to, t / duration));
                yield return CoroutineStatus.Running;
            }

            SetAlpha(to);

            if (removeAfter && Parent != null)
            {
                Parent.RemoveChild(this);
                onRemove?.Invoke();
            }

            yield return CoroutineStatus.Success;
        }

        public void Pulsate(Vector2 startScale, Vector2 endScale, float duration)
        {
            if (CoroutineManager.IsCoroutineRunning(pulsateCoroutine))
            {
                return;
            }
            pulsateCoroutine = CoroutineManager.StartCoroutine(DoPulsate(startScale, endScale, duration), "Pulsate" + ToString());
        }

        private IEnumerable<CoroutineStatus> DoPulsate(Vector2 startScale, Vector2 endScale, float duration)
        {
            float t = 0.0f;
            while (t < duration)
            {
                t += CoroutineManager.DeltaTime;
                RectTransform.LocalScale = Vector2.Lerp(startScale, endScale, (float)Math.Sin(t / duration * MathHelper.Pi));
                yield return CoroutineStatus.Running;
            }
            RectTransform.LocalScale = startScale;
            yield return CoroutineStatus.Success;
        }

        public virtual void ApplyStyle(GUIComponentStyle style)
        {
            if (style == null) { return; }

            color = style.Color;
            _currentColor = color;
            hoverColor = style.HoverColor;
            selectedColor = style.SelectedColor;
            pressedColor = style.PressedColor;
            disabledColor = style.DisabledColor;
            sprites = style.Sprites;
            OutlineColor = style.OutlineColor;
            SpriteCrossFadeTime = style.SpriteCrossFadeTime;
            ColorCrossFadeTime = style.ColorCrossFadeTime;
            ColorTransition = style.TransitionMode;
            FallBackState = style.FallBackState;

            if (rectTransform != null)
            {
                ApplySizeRestrictions(style);
            }

            styleHierarchy = GUIComponentStyle.ToHierarchy(style);
        }

        public void ApplySizeRestrictions(GUIComponentStyle style)
        {
            if (style.Width.HasValue)
            {
                RectTransform.MinSize = new Point(style.Width.Value, RectTransform.MinSize.Y);
                RectTransform.MaxSize = new Point(style.Width.Value, RectTransform.MaxSize.Y);
                if (rectTransform.IsFixedSize) { RectTransform.Resize(new Point(style.Width.Value, rectTransform.NonScaledSize.Y)); }
            }
            if (style.Height.HasValue)
            {
                RectTransform.MinSize = new Point(RectTransform.MinSize.X, style.Height.Value);
                RectTransform.MaxSize = new Point(RectTransform.MaxSize.X, style.Height.Value);
                if (rectTransform.IsFixedSize) { RectTransform.Resize(new Point(rectTransform.NonScaledSize.X, style.Height.Value)); }
            }
        }

        public static GUIComponent FromXML(ContentXElement element, RectTransform parent)
        {
            GUIComponent component = null;

            foreach (var subElement in element.Elements())
            {
                if (subElement.Name.ToString().Equals("conditional", StringComparison.OrdinalIgnoreCase) && !CheckConditional(subElement))
                {
                    return null;
                }
            }

            switch (element.Name.ToString().ToLowerInvariant())
            {
                case "text":
                case "guitextblock":
                    component = LoadGUITextBlock(element, parent);
                    break;
                case "link":
                    component = LoadLink(element, parent);
                    break;
                case "frame":
                case "guiframe":
                case "spacing":
                    component = LoadGUIFrame(element, parent);
                    break;
                case "button":
                case "guibutton":
                    component = LoadGUIButton(element, parent);
                    break;
                case "listbox":
                case "guilistbox":
                    component = LoadGUIListBox(element, parent);
                    break;
                case "guilayoutgroup":
                case "layoutgroup":
                    component = LoadGUILayoutGroup(element, parent);
                    break;
                case "image":
                case "guiimage":
                    component = LoadGUIImage(element, parent);
                    break;
                case "accordion":
                    return LoadAccordion(element, parent);
                case "gridtext":
                    LoadGridText(element, parent);
                    return null;
                case "conditional":
                    break;
                default:
                    throw new NotImplementedException("Loading GUI component \"" + element.Name + "\" from XML is not implemented.");
            }

            if (component != null)
            {
                foreach (var subElement in element.Elements())
                {
                    if (subElement.Name.ToString().Equals("conditional", StringComparison.OrdinalIgnoreCase)) { continue; }
                    FromXML(subElement, component is GUIListBox listBox ? listBox.Content.RectTransform : component.RectTransform);
                }

                if (element.GetAttributeBool("resizetofitchildren", false))
                {
                    Vector2 relativeResizeScale = element.GetAttributeVector2("relativeresizescale", Vector2.One);
                    if (component is GUILayoutGroup layoutGroup)
                    {
                        layoutGroup.RectTransform.NonScaledSize =
                            layoutGroup.IsHorizontal ?
                            new Point(layoutGroup.Children.Sum(c => c.Rect.Width), layoutGroup.Rect.Height) :
                            component.RectTransform.MinSize = new Point(layoutGroup.Rect.Width, layoutGroup.Children.Sum(c => c.Rect.Height));
                        if (layoutGroup.CountChildren > 0)
                        {
                            layoutGroup.RectTransform.NonScaledSize +=
                                layoutGroup.IsHorizontal ?
                                new Point((int)((layoutGroup.CountChildren - 1) * (layoutGroup.AbsoluteSpacing + layoutGroup.Rect.Width * layoutGroup.RelativeSpacing)), 0) :
                                new Point(0, (int)((layoutGroup.CountChildren - 1) * (layoutGroup.AbsoluteSpacing + layoutGroup.Rect.Height * layoutGroup.RelativeSpacing)));
                        }
                    }
                    else if (component is GUIListBox listBox)
                    {
                        listBox.RectTransform.NonScaledSize =
                            listBox.ScrollBar.IsHorizontal ?
                            new Point(listBox.Children.Sum(c => c.Rect.Width + listBox.Spacing), listBox.Rect.Height) :
                            component.RectTransform.MinSize = new Point(listBox.Rect.Width, listBox.Children.Sum(c => c.Rect.Height + listBox.Spacing));
                    }
                    else
                    {
                        component.RectTransform.NonScaledSize =
                            new Point(
                                component.Children.Max(c => c.Rect.Right) - component.Children.Min(c => c.Rect.X),
                                component.Children.Max(c => c.Rect.Bottom) - component.Children.Min(c => c.Rect.Y));
                    }
                    component.RectTransform.NonScaledSize =
                        component.RectTransform.NonScaledSize.Multiply(relativeResizeScale);
                }
            }
            return component;
        }

        private static bool CheckConditional(XElement element)
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                switch (attribute.Name.ToString().ToLowerInvariant())
                {
                    case "language":
                        var languages = element.GetAttributeIdentifierArray(attribute.Name.ToString(), Array.Empty<Identifier>())
                            .Select(s => new LanguageIdentifier(s));
                        if (!languages.Any(l => GameSettings.CurrentConfig.Language == l)) { return false; }
                        break;
                    case "gameversion":
                        var version = new Version(attribute.Value);
                        if (GameMain.Version != version) { return false; }
                        break;
                    case "mingameversion":
                        var minVersion = new Version(attribute.Value);
                        if (GameMain.Version < minVersion) { return false; }
                        break;
                    case "maxgameversion":
                        var maxVersion = new Version(attribute.Value);
                        if (GameMain.Version > maxVersion) { return false; }
                        break;
                    case "buildconfiguration":
                        switch (attribute.Value.ToString().ToLowerInvariant())
                        {
                            case "debug":
#if DEBUG
                                return true;
#else
                                break;
#endif
                            case "unstable":
#if UNSTABLE
                                return true;
#else
                                break;
#endif
                            case "release":
#if !DEBUG && !UNSTABLE
                                return true;
#else
                                break;
#endif
                        }
                        return false;
                }
            }

            return true;
        }

        private static GUITextBlock LoadGUITextBlock(XElement element, RectTransform parent, string overrideText = null, Anchor? anchor = null)
        {
            string text = overrideText ??
                (element.Attribute("text") == null ?
                    element.ElementInnerText() :
                    element.GetAttributeString("text", ""));
            text = text.Replace(@"\n", "\n");

            string style = element.GetAttributeString("style", "");
            if (style == "null") { style = null; }
            Color? color = null;
            if (element.Attribute("color") != null) { color = element.GetAttributeColor("color", Color.White); }           
            float scale = element.GetAttributeFloat("scale", 1.0f);
            bool wrap = element.GetAttributeBool("wrap", true);
            Alignment alignment =
                element.GetAttributeEnum("alignment", text.Contains('\n') ? Alignment.Left : Alignment.Center);
            if (!GUIStyle.Fonts.TryGetValue(element.GetAttributeIdentifier("font", "Font"), out GUIFont font))
            {
                font = GUIStyle.Font;
            }

            var textBlock = new GUITextBlock(RectTransform.Load(element, parent),
                RichString.Rich(text), color, font, alignment, wrap: wrap, style: style)
            {
                TextScale = scale
            };
            if (anchor.HasValue) { textBlock.RectTransform.SetPosition(anchor.Value); }
            textBlock.RectTransform.IsFixedSize = true;
            textBlock.RectTransform.NonScaledSize = new Point(textBlock.Rect.Width, textBlock.Rect.Height);
            return textBlock;
        }

        private static GUIButton LoadLink(XElement element, RectTransform parent)
        {
            var button = LoadGUIButton(element, parent);
            string url = element.GetAttributeString("url", "");
            button.OnClicked = (btn, userdata) =>
            {
                try
                {
#if USE_STEAM
                    Steam.SteamManager.OverlayCustomUrl(url);
#else
                    ToolBox.OpenFileWithShell(url);
#endif
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to open url \""+url+"\".", e);
                }
                return true;
            };
            return button;
        }

        private static void LoadGridText(XElement element, RectTransform parent)
        {
            string text = element.Attribute("text") == null ?
                element.ElementInnerText() :
                element.GetAttributeString("text", "");
            text = text.Replace(@"\n", "\n");

            string[] elements = text.Split(',');
            RectTransform lineContainer = null;
            for (int i = 0; i < elements.Length; i++)
            {
                switch (i % 3)
                {
                    case 0:
                        lineContainer = LoadGUITextBlock(element, parent, elements[i], Anchor.CenterLeft).RectTransform;
                        lineContainer.Anchor = Anchor.TopCenter;
                        lineContainer.Pivot = Pivot.TopCenter;
                        lineContainer.NonScaledSize = new Point((int)(parent.NonScaledSize.X * 0.7f), lineContainer.NonScaledSize.Y);
                        break;
                    case 1:
                        LoadGUITextBlock(element, lineContainer, elements[i], Anchor.Center).TextAlignment = Alignment.Center;
                        break;
                    case 2:
                        LoadGUITextBlock(element, lineContainer, elements[i], Anchor.CenterRight).TextAlignment = Alignment.CenterRight;
                        break;
                }
            }
        }

        private static GUIFrame LoadGUIFrame(XElement element, RectTransform parent)
        {
            string style = element.GetAttributeString("style", element.Name.ToString().Equals("spacing", StringComparison.OrdinalIgnoreCase) ? null : "");
            if (style == "null") { style = null; }
            return new GUIFrame(RectTransform.Load(element, parent), style: style);
        }

        private static GUIButton LoadGUIButton(XElement element, RectTransform parent)
        {
            string style = element.GetAttributeString("style", "");
            if (style == "null") { style = null; }

            Alignment textAlignment = Alignment.Center;
            Enum.TryParse(element.GetAttributeString("textalignment", "Center"), out textAlignment);

            string text = element.Attribute("text") == null ?
                element.ElementInnerText() :
                element.GetAttributeString("text", "");
            text = text.Replace(@"\n", "\n");

            return new GUIButton(RectTransform.Load(element, parent),
                text: text,
                textAlignment: textAlignment,
                style: style);
        }

        private static GUIListBox LoadGUIListBox(XElement element, RectTransform parent)
        {
            string style = element.GetAttributeString("style", "");
            if (style == "null") { style = null; }
            bool isHorizontal = element.GetAttributeBool("ishorizontal", !element.GetAttributeBool("isvertical", true));
            return new GUIListBox(RectTransform.Load(element, parent), isHorizontal, style: style);
        }

        private static GUILayoutGroup LoadGUILayoutGroup(XElement element, RectTransform parent)
        {
            bool isHorizontal = element.GetAttributeBool("ishorizontal", !element.GetAttributeBool("isvertical", true));

            Enum.TryParse(element.GetAttributeString("childanchor", "TopLeft"), out Anchor childAnchor);
            return new GUILayoutGroup(RectTransform.Load(element, parent), isHorizontal, childAnchor)
            {
                Stretch = element.GetAttributeBool("stretch", false),
                RelativeSpacing = element.GetAttributeFloat("relativespacing", 0.0f),
                AbsoluteSpacing = element.GetAttributeInt("absolutespacing", 0),
            };
        }

        private static GUIImage LoadGUIImage(ContentXElement element, RectTransform parent)
        {
            Sprite sprite;
            string url = element.GetAttributeString("url", "");
            if (!string.IsNullOrEmpty(url))
            {
                string localFileName = Path.GetFileNameWithoutExtension(url.Replace("/", "").Replace(":", "").Replace("https", "").Replace("http", ""))
                    .Replace(".", "");
                localFileName += Path.GetExtension(url);
                string localFilePath = Path.Combine("Downloads", localFileName);
                if (!File.Exists(localFilePath))
                {
                    Uri baseAddress = new Uri(url);
                    Uri remoteDirectory = new Uri(baseAddress, ".");
                    string remoteFileName = Path.GetFileName(baseAddress.LocalPath);
                    IRestClient client = new RestClient(remoteDirectory);
                    var response = client.Execute(new RestRequest(remoteFileName, Method.GET));
                    if (response.ResponseStatus != ResponseStatus.Completed) { return null; }
                    if (response.StatusCode != HttpStatusCode.OK) { return null; }

                    if (!Directory.Exists("Downloads")) { Directory.CreateDirectory("Downloads"); }
                    File.WriteAllBytes(localFilePath, response.RawBytes);
                }
                sprite = new Sprite(element, "Downloads", localFileName);
            }
            else
            {
                sprite = new Sprite(element);
            }

            return new GUIImage(RectTransform.Load(element, parent), sprite, scaleToFit: true);
        }

        private static GUIButton LoadAccordion(ContentXElement element, RectTransform parent)
        {
            var button = LoadGUIButton(element, parent);
            List<GUIComponent> content = new List<GUIComponent>();
            foreach (var subElement in element.Elements())
            {
                var contentElement = FromXML(subElement, parent);
                if (contentElement != null)
                {
                    contentElement.Visible = false;
                    contentElement.IgnoreLayoutGroups = true;
                    content.Add(contentElement);
                }
            }
            button.OnClicked = (btn, userdata) =>
            {
                bool visible = content.FirstOrDefault()?.Visible ?? true;
                foreach (GUIComponent contentElement in content)
                {
                    contentElement.Visible = !visible;
                    contentElement.IgnoreLayoutGroups = !contentElement.Visible;
                }
                if (button.Parent is GUILayoutGroup layoutGroup)
                {
                    layoutGroup.Recalculate();
                }
                return true;
            };
            return button;
        }
    }
}
