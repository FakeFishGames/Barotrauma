#nullable enable

using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    internal abstract partial class CircuitBoxConnection
    {
        public string Name => Connection.Name;

        public CircuitBoxLabel Label { get; private set; }

        private Sprite? knobSprite,
                        screwSprite,
                        connectorSprite;

        private static int Padding => GUI.IntScale(8);

        private Option<LocalizedString> tooltip = Option.None;

        private partial void InitProjSpecific(CircuitBox circuitBox)
        {
            Label = new CircuitBoxLabel(Connection.DisplayName, GUIStyle.SubHeadingFont);
            knobSprite = circuitBox.ConnectionSprite;
            screwSprite = circuitBox.ConnectionScrewSprite;
            connectorSprite = circuitBox.WireConnectorSprite;
            Length = Rect.Width + Padding + Label.Size.X;
        }

        public void SetLabel(LocalizedString label, CircuitBoxNode node)
        {
            Label = new CircuitBoxLabel(label, GUIStyle.SubHeadingFont);
            Length = Rect.Width + Padding + Label.Size.X;
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 drawPos, Vector2 parentPos, Color color)
        {
            if (CircuitBox.UI is not { } circuitBoxUi) { return; }
            var drawRect = CircuitBoxNode.OverrideRectLocation(Rect, drawPos, parentPos);

            Vector2 cursorPos = circuitBoxUi.GetCursorPosition();
            cursorPos.Y = -cursorPos.Y;

            bool isMouseOver = drawRect.Contains(cursorPos);

            float xPos;
            if (IsOutput)
            {
                xPos = drawRect.Left - Padding - Label.Size.X;
            }
            else
            {
                xPos = drawRect.Right + Padding;
            }

            Vector2 stringPos = new Vector2(xPos, drawRect.Center.Y - Label.Size.Y / 2f);
            GUI.DrawString(spriteBatch, stringPos, Label.Value, GUIStyle.TextColorNormal, font: Label.Font);

            if (knobSprite is null)
            {
                CircuitBoxUI.DrawRectangleWithBorder(spriteBatch, drawRect, GUIStyle.Blue * 0.3f, GUIStyle.Blue);
            }
            else
            {
                float scale = drawRect.Height / knobSprite.size.Y;
                knobSprite?.Draw(spriteBatch, drawRect.Center, color, 0f, scale);
            }

            bool isScrewed = this switch
            {
                CircuitBoxOutputConnection output => output.ExternallyConnectedFrom.Count > 0,
                CircuitBoxInputConnection input => input.ExternallyConnectedTo.Count > 0,
                _ => Connection.Wires.Count > 0 ||
                     Connection.CircuitBoxConnections.Count > 0 ||
                     ExternallyConnectedFrom.Count > 0
            };

            if (isMouseOver)
            {
                var glowSprite = GUIStyle.UIGlowCircular.Value?.Sprite;
                if (glowSprite is not null)
                {
                    float glowScale = 40f / glowSprite.size.X;
                    if (isScrewed)
                    {
                        glowScale *= 1.2f;
                    }
                    glowSprite.Draw(spriteBatch, position, GUIStyle.Yellow, glowSprite.size / 2, scale: glowScale);
                }
            }

            tooltip = Option.None;
            if (ConnectionPanel.ShouldDebugDrawWiring)
            {
                Connection.DrawConnectionDebugInfo(spriteBatch, Connection, drawRect.Center, isScrewed ? 1.1f : 0.9f, out var tooltipText);

                if (isMouseOver && !tooltipText.IsNullOrEmpty())
                {
                    tooltip = Option.Some(tooltipText);
                }
            }

            if (!isScrewed) { return; }

            if (screwSprite is not null)
            {
                float screwScale = drawRect.Height / screwSprite.size.Y;
                screwSprite.Draw(spriteBatch, drawRect.Center, color, 0f, screwScale);
            }

            if (connectorSprite is not null)
            {
                float screwScale = drawRect.Height / connectorSprite.size.Y * 2f;
                Vector2 pos = drawRect.Center;

                connectorSprite.Draw(spriteBatch,
                    pos: pos,
                    color: Color.White,
                    origin: connectorSprite.Origin,
                    rotate: MathHelper.Pi / (IsOutput ? -2f : 2f),
                    scale: screwScale,
                    spriteEffect: SpriteEffects.None);
            }
        }

        public void DrawHUD(SpriteBatch spriteBatch, Camera camera)
        {
            if (!tooltip.TryUnwrap(out var text)) { return; }

            var drawPos = camera.WorldToScreen(new Vector2(Rect.Right, -Rect.Bottom));

            GUIComponent.DrawToolTip(spriteBatch, text, drawPos);
        }
    }
}