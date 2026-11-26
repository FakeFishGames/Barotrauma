using Barotrauma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor
{
    internal static class OutpostResourceLogUI
    {
        private const string UserData = "logwindow";

        public const string BackgroundStyle = "HappeningsBackground";
        private static readonly Color BackgroundColor = new Color(80, 80, 80, 255);

        private const float LogLineWidth = 0.85f;

        public static void CreateOrCloseUI(PersistentOutpostData data, GUIComponent parent)
        {
            if (parent.GetChildByUserData(UserData) is { } existing)
            {
                parent.RemoveChild(existing);
                return;
            }

            GUIFrame logFrame = new GUIFrame(new RectTransform(Vector2.One, parent.RectTransform, Anchor.TopRight, Pivot.TopLeft)
                { MinSize = new Point(0, GUI.IntScale(GameMain.GraphicsHeight * 0.33f)) }, style: "GUIToolTip"
            )
            {
                UserData = UserData
            };
            logFrame.Color *= 0.8f;

            if (logFrame.Rect.Right > GameMain.GraphicsWidth)
            {
                logFrame.RectTransform.Anchor = Anchor.TopLeft;
                logFrame.RectTransform.Pivot = Pivot.TopRight;
            }
            if (logFrame.Rect.Bottom > GameMain.GraphicsHeight)
            {
                logFrame.RectTransform.ScreenSpaceOffset += new Point(0, GameMain.GraphicsHeight - logFrame.Rect.Bottom);
            }

            if (GameMain.GameSession?.Map is not { } map) { return; }
            GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), logFrame.RectTransform, anchor: Anchor.Center))
            {
                Stretch = true,
            };

            GUIFrame logContainer = new GUIFrame(new RectTransform(Vector2.One, layout.RectTransform), style: null);
            GUIButton closeButton = new GUIButton(new RectTransform(Vector2.UnitX, layout.RectTransform), TextManager.Get("close"))
            {
                OnPressed = () =>
                {
                    parent.RemoveChild(parent.GetChildByUserData(UserData));
                    return true;
                }
            };
            // Recalculate so the log lines get the correct width when calculating their height
            layout.Recalculate();

            CreateBackgroundImage(new RectTransform(Vector2.One, logContainer.RectTransform), BackgroundStyle);
            GUIListBox log = new GUIListBox(new RectTransform(new Vector2(0.95f, 1f), logContainer.RectTransform, Anchor.Center), style: null)
            {
                Spacing = GUI.IntScale(16),
                HoverCursor = CursorState.Default,
                AutoHideScrollBar = false,
                ScrollBarVisible = true
            };

            foreach (var (step, events) in data.Logs)
            {
                int stepsAgo = map.WorldStepCount - step;
                for (int i = events.Count - 1; i >= 0; i--)
                {
                    CreateLogEntry(events[i], stepsAgo, log.Content.RectTransform);
                }
            }

            log.RecalculateChildren();
            log.UpdateScrollBarSize();
        }

        /// <summary>
        /// Creates a resource log line, with the "x world steps ago" text on the right side of it (if stepsAgo is given)
        /// </summary>
        public static GUITextBlock CreateLogEntry(EconomyLogEntry log, int? stepsAgo, RectTransform parent)
        {
            GUITextBlock block = new GUITextBlock(new RectTransform(new Vector2(LogLineWidth, 0f), parent),
                RichString.Rich(log.GetMessage()),
                wrap: true,
                textAlignment: Alignment.TopLeft,
                font: GUIStyle.IconFont)
            {
                CanBeFocused = false,
                Shadow = true
            };
            block.CalculateHeightFromText();
            // Height is based on the text, don't let resizing the parent mess with it
            block.RectTransform.IsFixedSize = true;

            if (log.IsDebugOnly)
            {
                block.TextColor = Color.MediumPurple;
            }

            if (stepsAgo.HasValue)
            {
                new GUITextBlock(new RectTransform(new Vector2((1f - LogLineWidth) / LogLineWidth, 1f), block.RectTransform, anchor: Anchor.CenterRight, Pivot.CenterLeft),
                    RichString.Rich(TextManager.GetWithVariable("timestepago", "[amount]", $"{stepsAgo.Value}")),
                    font: GUIStyle.IconFont,
                    textAlignment: Alignment.Center)
                {
                    Shadow = true,
                    Padding = Vector4.Zero,
                    AutoScaleHorizontal = true
                };
            }

            return block;
        }

        /// <summary>
        /// Creates a component that draws the sprite of the given style as a background image, scaled to cover the whole component
        /// </summary>
        /// <param name="getDrawArea">Optional override for the area to draw the background in (defaults to the component's rect)</param>
        public static GUICustomComponent CreateBackgroundImage(RectTransform rectT, string backgroundStyle, Func<Rectangle>? getDrawArea = null)
        {
            Sprite? backgroundSprite = null;
            if (GUIStyle.GetComponentStyle(backgroundStyle) is { } style)
            {
                if (style.Sprites[GUIComponent.ComponentState.None].FirstOrDefault() is { } sprite)
                {
                    backgroundSprite = sprite.Sprite;
                }
            }

            return new GUICustomComponent(rectT, onDraw: OnDraw)
            {
                CanBeFocused = false
            };

            void OnDraw(SpriteBatch spriteBatch, GUICustomComponent component)
            {
                // No background image, just let the parent's background show
                if (backgroundSprite is null) { return; }

                Rectangle drawArea = getDrawArea?.Invoke() ?? component.Rect;

                Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
                RasterizerState prevRasterizerState = spriteBatch.GraphicsDevice.RasterizerState;
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, drawArea);
                spriteBatch.Begin(samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);

                spriteBatch.Draw(backgroundSprite.Texture,
                                 position: drawArea.Center.ToVector2(),
                                 sourceRectangle: backgroundSprite.SourceRect,
                                 color: BackgroundColor,
                                 rotation: 0f,
                                 origin: backgroundSprite.size / 2f,
                                 scale: Math.Max(
                                     (float)drawArea.Width / backgroundSprite.SourceRect.Width,
                                     (float)drawArea.Height / backgroundSprite.SourceRect.Height),
                                 effects: SpriteEffects.None,
                                 layerDepth: 0f);

                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(samplerState: GUI.SamplerState, rasterizerState: prevRasterizerState);
            }
        }
    }
}
