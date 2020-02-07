using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using System;
using System.Xml.Linq;
using System.IO;
using RestSharp;
using System.Net;

namespace Barotrauma
{
    public abstract class GUIComponent
    {
        #region Hierarchy
        public GUIComponent Parent => RectTransform.Parent?.GUIComponent;

        public CursorState HoverCursor = CursorState.Default;
        
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
            return GetAllChildren().Where(c => c is T).Select(c => c as T);
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
            if (index < 0 || index >= CountChildren) return null;
            return RectTransform.GetChild(index).GUIComponent;
        }

        public int GetChildIndex(GUIComponent child)
        {
            if (child == null) return -1;
            return RectTransform.GetChildIndex(child.RectTransform);
        }

        public GUIComponent GetChildByUserData(object obj)
        {
            foreach (GUIComponent child in Children)
            {
                if (child.UserData == obj || (child.userData != null && child.userData.Equals(obj))) return child;
            }
            return null;
        }

        public bool IsParentOf(GUIComponent component, bool recursive = true)
        {
            if (component == null) { return false; }
            return RectTransform.IsParentOf(component.RectTransform, recursive);
        }

        public virtual void RemoveChild(GUIComponent child)
        {
            if (child == null) return;
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
            var matchingChild = Children.FirstOrDefault(c => c.userData == userData);
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
            return Children.Where(c => c.userData == userData);
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

        public Action<GUIComponent> OnAddedToGUIUpdateList;

        public enum ComponentState { None, Hover, Pressed, Selected, HoverSelected };

        protected Alignment alignment;

        protected GUIComponentStyle style;

        protected object userData;

        public bool CanBeFocused;

        protected Color color;
        protected Color hoverColor;
        protected Color selectedColor;
        protected Color disabledColor;
        protected Color pressedColor;

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

        public virtual ScalableFont Font
        {
            get;
            set;
        }

        // Use the rawtooltip when copying displayed tooltips so that any possible color-data related values are translated over as well
        public string RawToolTip;
        private string toolTip;
        public virtual string ToolTip
        {
            get
            {
                return toolTip;
            }
            set
            {
                RawToolTip = value;
                TooltipColorData = ColorData.GetColorData(value, out value);
                toolTip = value;
            }
        }

        public List<ColorData> TooltipColorData = null;

        public GUIComponentStyle Style
        {
            get { return style; }
        }

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
            if (Parent == null || !ClampMouseRectToParent) return r;
            Rectangle parentRect = Parent.ClampRect(Parent.Rect);
            if (parentRect.Width <= 0 || parentRect.Height <= 0) return Rectangle.Empty;
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
            if (r.Width <= 0 || r.Height <= 0) return Rectangle.Empty;
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
                if (!CanBeFocused) return Rectangle.Empty;
                return ClampMouseRectToParent ? ClampRect(Rect) : Rect;
            }
        }

        public Dictionary<ComponentState, List<UISprite>> sprites;

        public SpriteEffects SpriteEffects;

        public virtual Color OutlineColor { get; set; }

        protected ComponentState _state;
        protected ComponentState _previousState;
        protected bool selected;
        public virtual bool Selected
        {
            get { return selected; }
            set
            {
                selected = value;
                Children.ForEach(c => c.Selected = value);
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

        public object UserData
        {
            get { return userData; }
            set { userData = value; }
        }
        
        public int CountChildren
        {
            get { return RectTransform.CountChildren; }
        }

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
            Font = GUI.Font;
            CanBeFocused = true;

            if (style != null) { GUI.Style.Apply(this, style); }
        }

        protected GUIComponent(string style)
        {
            Visible = true;
            OutlineColor = Color.Transparent;
            Font = GUI.Font;
            CanBeFocused = true;

            if (style != null) { GUI.Style.Apply(this, style); }
        }

        #region Updating
        public virtual void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            if (!Visible) return;

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
            if (!Visible) return;

            AutoUpdate = false;
            Update(deltaTime);
            if (alsoChildren)
            {
                UpdateChildren(deltaTime, recursive);
            }
        }

        protected virtual void Update(float deltaTime)
        {
            if (!Visible) return;
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

        /// <summary>
        /// Updates all the children manually.
        /// </summary>
        public void UpdateChildren(float deltaTime, bool recursive)
        {
            RectTransform.Children.ForEach(c => c.GUIComponent.UpdateManually(deltaTime, recursive, recursive));
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
            if (!Visible) return;

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
            RectTransform.Children.ForEach(c => c.GUIComponent.DrawManually(spriteBatch, recursive, recursive));
        }

        protected Color _currentColor;

        protected virtual Color GetColor(ComponentState state)
        {
            if (!Enabled) { return DisabledColor; }
            return state switch
            {
                ComponentState.Hover => HoverColor,
                ComponentState.HoverSelected => HoverColor,
                ComponentState.Pressed => PressedColor,
                ComponentState.Selected => SelectedColor,
                _ => Color,
            };
        }

        private float GetEasing(TransitionMode easing, float t)
        {
            return easing switch
            {
                TransitionMode.Smooth => MathUtils.SmoothStep(t),
                TransitionMode.Smoother => MathUtils.SmootherStep(t),
                TransitionMode.EaseIn => MathUtils.EaseIn(t),
                TransitionMode.EaseOut => MathUtils.EaseOut(t),
                TransitionMode.Exponential => t * t,
                TransitionMode.Linear => t,
                _ => t,
            };
        }

        protected Color GetBlendedColor(Color targetColor, ref Color blendedColor)
        {
            blendedColor = ColorCrossFadeTime > 0 ? Color.Lerp(blendedColor, targetColor, MathUtils.InverseLerp(ColorCrossFadeTime, 0, GetEasing(ColorTransition, colorFadeTimer))) : targetColor;
            return blendedColor;
        }

        protected virtual void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;
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
                                ? MathUtils.InverseLerp(0, SpriteCrossFadeTime, GetEasing(uiSprite.TransitionMode, spriteFadeTimer)) : 0;
                            if (alphaMultiplier > 0)
                            {
                                uiSprite.Draw(spriteBatch, rect, previousColor * alphaMultiplier, SpriteEffects);
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
                            ? MathUtils.InverseLerp(SpriteCrossFadeTime, 0, GetEasing(uiSprite.TransitionMode, spriteFadeTimer)) : (_currentColor.A / 255.0f);
                        if (alphaMultiplier > 0)
                        {
                            uiSprite.Draw(spriteBatch, rect, _currentColor * alphaMultiplier, SpriteEffects);
                        }
                    }
                }
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
                    var glow = useCircularFlash ? GUI.UIGlowCircular : GUI.UIGlow;
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
            if (!Visible) return;
            DrawToolTip(spriteBatch, ToolTip, GUI.MouseOn.Rect, TooltipColorData);
        }

        public static void DrawToolTip(SpriteBatch spriteBatch, string toolTip, Rectangle targetElement, List<ColorData> colorData = null)
        {
            if (Tutorials.Tutorial.ContentRunning) { return; }

            int width = (int)(400 * GUI.Scale);
            int height = (int)(18 * GUI.Scale);
            Point padding = new Point((int)(10 * GUI.Scale));

            if (toolTipBlock == null || (string)toolTipBlock.userData != toolTip)
            {
                toolTipBlock = new GUITextBlock(new RectTransform(new Point(width, height), null), colorData, toolTip, font: GUI.SmallFont, wrap: true, style: "GUIToolTip");
                toolTipBlock.RectTransform.NonScaledSize = new Point(
                    (int)(GUI.SmallFont.MeasureString(toolTipBlock.WrappedText).X + padding.X + toolTipBlock.Padding.X + toolTipBlock.Padding.Z),
                    (int)(GUI.SmallFont.MeasureString(toolTipBlock.WrappedText).Y + padding.Y + toolTipBlock.Padding.Y + toolTipBlock.Padding.W));
                toolTipBlock.userData = toolTip;
            }

            toolTipBlock.RectTransform.AbsoluteOffset = new Point(targetElement.Center.X, targetElement.Bottom);
            if (toolTipBlock.Rect.Right > GameMain.GraphicsWidth - 10)
            {
                toolTipBlock.RectTransform.AbsoluteOffset -= new Point(toolTipBlock.Rect.Width, 0);
            }
            if (toolTipBlock.Rect.Bottom > GameMain.GraphicsHeight - 10)
            {
                toolTipBlock.RectTransform.AbsoluteOffset -= new Point(
                    (targetElement.Width / 2) * Math.Sign(targetElement.Center.X - toolTipBlock.Center.X), 
                    toolTipBlock.Rect.Bottom - (GameMain.GraphicsHeight - 10));
            }
            toolTipBlock.SetTextPos();

            toolTipBlock.DrawManually(spriteBatch);
        }
        #endregion

        protected virtual void SetAlpha(float a)
        {
            color = new Color(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, a);
        }

        public virtual void Flash(Color? color = null, float flashDuration = 1.5f, bool useRectangleFlash = false, bool useCircularFlash = false, Vector2? flashRectInflate = null)
        {
            flashTimer = flashDuration;
            this.flashRectInflate = flashRectInflate ?? Vector2.Zero;            
            this.useRectangleFlash = useRectangleFlash;
            this.useCircularFlash = useCircularFlash;
            this.flashDuration = flashDuration;
            flashColor = (color == null) ? GUI.Style.Red : (Color)color;
        }

        public void FadeOut(float duration, bool removeAfter)
        {
            CoroutineManager.StartCoroutine(LerpAlpha(0.0f, duration, removeAfter));
        }

        private IEnumerable<object> LerpAlpha(float to, float duration, bool removeAfter)
        {
            float t = 0.0f;
            float startA = color.A;

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

        private IEnumerable<object> DoPulsate(Vector2 startScale, Vector2 endScale, float duration)
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


            this.style = style;
        }

        public static GUIComponent FromXML(XElement element, RectTransform parent)
        {
            GUIComponent component = null;

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() == "conditional" &&
                    !CheckConditional(subElement))
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
                default:
                    throw new NotImplementedException("Loading GUI component \""+element.Name+"\" from XML is not implemented.");
            }

            if (component != null)
            {
                foreach (XElement subElement in element.Elements())
                {
                    if (subElement.Name.ToString().ToLowerInvariant() == "conditional") { continue; }
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
                        string[] languages = element.GetAttributeStringArray(attribute.Name.ToString(), new string[0]);
                        if (!languages.Any(l => GameMain.Config.Language.ToLower() == l.ToLower())) { return false; }
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
            Alignment alignment = Alignment.Center;
            Enum.TryParse(element.GetAttributeString("alignment", "Center"), out alignment);
            ScalableFont font = GUI.Font;
            switch (element.GetAttributeString("font", "Font").ToLowerInvariant())
            {
                case "font":
                    font = GUI.Font;
                    break;
                case "smallfont":
                    font = GUI.SmallFont;
                    break;
                case "largefont":
                    font = GUI.LargeFont;
                    break;
                case "subheading":
                    font = GUI.SubHeadingFont;
                    break;
            }

            var textBlock = new GUITextBlock(RectTransform.Load(element, parent),
                text, color, font, alignment, wrap: wrap, style: style)
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
                    Steam.SteamManager.OverlayCustomURL(url);
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
            string style = element.GetAttributeString("style", element.Name.ToString().ToLowerInvariant() == "spacing" ? null : "");
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

        private static GUIImage LoadGUIImage(XElement element, RectTransform parent)
        {
            Sprite sprite = null;

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

        private static GUIButton LoadAccordion(XElement element, RectTransform parent)
        {
            var button = LoadGUIButton(element, parent);
            List<GUIComponent> content = new List<GUIComponent>();
            foreach (XElement subElement in element.Elements())
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
