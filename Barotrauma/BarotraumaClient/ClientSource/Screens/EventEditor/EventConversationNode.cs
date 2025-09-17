#nullable enable
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Barotrauma
{
    /// <summary>
    /// Base class for event nodes that display text content and can have inner Text nodes
    /// </summary>
    internal abstract class EventTextDisplayNode(Type type, string name) : EventNode(type, name)
    {
        protected virtual bool ShowOptions => false;

        private new Rectangle HeaderRectangle
        {
            get
            {
                if (!EventEditorScreen.ConversationMode) { return base.HeaderRectangle; }

                Rectangle drawRect = GetDrawRectangle();
                return new Rectangle(Position.ToPoint(), new Point(drawRect.Width, 32));
            }
        }

        public override Rectangle GetDrawRectangle()
        {
            if (!EventEditorScreen.ConversationMode) { return base.GetDrawRectangle(); }
            
            var textConnection = Connections.Find(c => string.Equals(c.Attribute, "Text", StringComparison.OrdinalIgnoreCase));
            var optionConnections = ShowOptions ? Connections.Where(c => c.Type == NodeConnectionType.Option) : Enumerable.Empty<EventEditorNodeConnection>();
            
            const int width = 300;
            int height = 50;
            
            // Calculate height for text section
            if (textConnection != null)
            {
                string textContent = GetTextContent(textConnection);
                if (!string.IsNullOrEmpty(textContent) && GUIStyle.Font.Value != null)
                {
                    string wrappedText = ToolBox.WrapText(textContent, width - 16, GUIStyle.Font.Value);
                    Vector2 textSize = GUIStyle.Font.MeasureString(wrappedText);
                    height += (int)textSize.Y + 10;
                }
                else
                {
                    height += 25; 
                }
            }
            
            // Calculate height for each option (only for conversation nodes)
            if (ShowOptions)
            {
                int optionIndex = 0;
                foreach (var option in optionConnections)
                {
                    string optionText = GetOptionText(option, optionIndex);
                    
                    if (GUIStyle.Font.Value != null)
                    {
                        string wrappedOption = ToolBox.WrapText(optionText, width - 40, GUIStyle.Font.Value);
                        Vector2 optionSize = GUIStyle.Font.MeasureString(wrappedOption);
                        height += (int)optionSize.Y + 20; 
                    }
                    else
                    {
                        height += 40; 
                    }
                    optionIndex++;
                }
            }
            
            Rectangle rect = Rectangle;
            return new Rectangle(rect.X, rect.Y, width, height);
        }

        protected override void DrawBack(SpriteBatch spriteBatch)
        {
            if (!EventEditorScreen.ConversationMode)
            {
                base.DrawBack(spriteBatch);
                return;
            }
            
            Rectangle bodyRect = GetDrawRectangle();
            
            // Background colors
            Color headerColor = IsSelected ? new Color(100, 150, 200) : new Color(120, 170, 220);
            Color bodyColor = new Color(90, 120, 150);
            Color borderColor = Color.LightBlue;
            
            // Draw background
            GUI.DrawRectangle(spriteBatch, HeaderRectangle, headerColor, isFilled: true, depth: 1.0f);
            GUI.DrawRectangle(spriteBatch, bodyRect, bodyColor, isFilled: true, depth: 1.0f);
            GUI.DrawRectangle(spriteBatch, HeaderRectangle, borderColor, isFilled: false, depth: 1.0f);
            GUI.DrawRectangle(spriteBatch, bodyRect, borderColor, isFilled: false, depth: 1.0f);

            // Draw header text
            Vector2 headerSize = GUIStyle.SubHeadingFont.MeasureString(Name);
            Vector2 headerPos = HeaderRectangle.Location.ToVector2() + (HeaderRectangle.Size.ToVector2() / 2) - (headerSize / 2);
            GUIStyle.SubHeadingFont.DrawString(spriteBatch, Name, headerPos, Color.White);

            // Draw text content
            DrawTextContent(spriteBatch, bodyRect);
            
            // Let base class handle standard connections (Activate, Next, etc.)
            DrawConnections(spriteBatch);
        }

        protected virtual void DrawTextContent(SpriteBatch spriteBatch, Rectangle bodyRect)
        {
            var textConnection = Connections.Find(c => string.Equals(c.Attribute, "Text", StringComparison.OrdinalIgnoreCase));
            var optionConnections = ShowOptions ? Connections.Where(c => c.Type == NodeConnectionType.Option) : Enumerable.Empty<EventEditorNodeConnection>();
            
            Vector2 mousePos = Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition);
            mousePos.Y = -mousePos.Y;
            
            const int padding = 8;
            int currentY = bodyRect.Y + padding + 30;
            
            // Draw text section
            if (textConnection != null)
            {
                string textContent = GetTextContent(textConnection);
                
                // Wrap text and calculate height
                string wrappedText = textContent;
                int textHeight = 25;
                if (GUIStyle.Font.Value != null)
                {
                    wrappedText = ToolBox.WrapText(textContent, bodyRect.Width - 24, GUIStyle.Font.Value);
                    Vector2 textSize = GUIStyle.Font.MeasureString(wrappedText);
                    textHeight = (int)textSize.Y + 10;
                }
                
                Rectangle textRect = new Rectangle(bodyRect.X + padding, currentY, bodyRect.Width - padding * 2, textHeight);
                
                // background
                GUI.DrawRectangle(spriteBatch, textRect, new Color(70, 100, 130), isFilled: true);
                GUI.DrawRectangle(spriteBatch, textRect, Color.CornflowerBlue, isFilled: false);
                
                // wrapped text
                Vector2 textPos = new Vector2(textRect.X + 4, textRect.Y + 4);
                GUI.DrawString(spriteBatch, textPos, wrappedText, Color.Yellow, font: GUIStyle.Font);
                
                // tooltip
                if (textRect.Contains(mousePos))
                {
                    string rawTextKey = GetRawTextKey(textConnection);
                    if (!string.IsNullOrEmpty(rawTextKey))
                    {
                        EventEditorScreen.DrawnTooltip = rawTextKey;
                    }
                }
                
                currentY += textHeight + 5;
            }
            
            // Draw options (only for conversation nodes)
            if (ShowOptions)
            {
                DrawOptions(spriteBatch, bodyRect, optionConnections, currentY);
            }
        }

        protected static void DrawOptions(SpriteBatch spriteBatch, Rectangle bodyRect, IEnumerable<EventEditorNodeConnection> optionConnections, int startY)
        {
            Vector2 mousePos = Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition);
            mousePos.Y = -mousePos.Y;
            const int padding = 8;
            int currentY = startY;
            
            int optionIndex = 0;
            foreach (var option in optionConnections)
            {
                string optionText = GetOptionText(option, optionIndex);

                // Wrap option text and calculate height
                string wrappedOption = optionText;
                int optionHeight = 30;
                if (GUIStyle.Font.Value != null)
                {
                    wrappedOption = ToolBox.WrapText(optionText, bodyRect.Width - 40, GUIStyle.Font.Value);
                    Vector2 optionSize = GUIStyle.Font.MeasureString(wrappedOption);
                    optionHeight = (int)optionSize.Y + 16;
                }
                
                Rectangle optionRect = new Rectangle(bodyRect.X + padding, currentY, bodyRect.Width - padding * 2, optionHeight);
                
                // background - red for end conversation, blue for normal
                Color optionBg = option.EndConversation ? new Color(120, 80, 80) : new Color(80, 80, 120);
                GUI.DrawRectangle(spriteBatch, optionRect, optionBg, isFilled: true);
                GUI.DrawRectangle(spriteBatch, optionRect, Color.White, isFilled: false);
                
                Vector2 optionPos = new Vector2(optionRect.X + 4, optionRect.Y + 4);
                GUI.DrawString(spriteBatch, optionPos, wrappedOption, Color.White, font: GUIStyle.Font);
                
                // tooltip
                if (optionRect.Contains(mousePos))
                {
                    string rawOptionKey = option.OptionText ?? "";
                    if (!string.IsNullOrEmpty(rawOptionKey))
                    {
                        EventEditorScreen.DrawnTooltip = rawOptionKey;
                    }
                }
                
                // connection point
                Rectangle connRect = new Rectangle(bodyRect.Right - 1, optionRect.Y + optionHeight / 2 - 8, 16, 16);
                GUI.DrawRectangle(spriteBatch, connRect, Color.DarkGray, isFilled: true);
                GUI.DrawRectangle(spriteBatch, connRect, Color.White, isFilled: false);
                option.DrawRectangle = connRect;
                
                // connection lines
                foreach (var connected in option.ConnectedTo)
                {
                    Vector2 start = new Vector2(connRect.Right, connRect.Center.Y);
                    Vector2 end = new Vector2(connected.DrawRectangle.Left, connected.DrawRectangle.Center.Y);
                    
                    float knobLength = 24;
                    var (points, _) = ToolBox.GetSquareLineBetweenPoints(start, end, knobLength);
                    
                    Color lineColor = GUIStyle.Red;
                    float lineWidth = Math.Max(2.0f, 2.0f / (Screen.Selected is EventEditorScreen eventEditor ? eventEditor.Cam.Zoom : 1.0f));
                    
                    GUI.DrawLine(spriteBatch, points[0], points[1], lineColor, width: (int)lineWidth);
                    GUI.DrawLine(spriteBatch, points[1], points[2], lineColor, width: (int)lineWidth);
                    GUI.DrawLine(spriteBatch, points[2], points[3], lineColor, width: (int)lineWidth);
                    GUI.DrawLine(spriteBatch, points[3], points[4], lineColor, width: (int)lineWidth);
                    GUI.DrawLine(spriteBatch, points[4], points[5], lineColor, width: (int)lineWidth);
                }
                
                currentY += optionHeight + 5;
                optionIndex++;
            }
        }

        private static string GetOptionText(EventEditorNodeConnection option, int optionIndex)
        {
            string optionTextKey = option.OptionText ?? $"Option {optionIndex + 1}";
            var allVariants = TextManager.GetAll(optionTextKey);

            int variantCount = allVariants.Count();
            return variantCount switch
            {
                > 1 => $"[{variantCount} variants] {string.Join(" / ", allVariants)}",
                1 => allVariants.First(),
                _ => optionTextKey
            };
        }

        private string GetTextContent(EventEditorNodeConnection textConnection)
        {
            string textContent = "";
            
            // First check if there's a direct text attribute
            if (textConnection.OverrideValue != null)
            {
                textContent = textConnection.OverrideValue.ToString() ?? "";
            }
            else
            {
                object? connectedValue = textConnection.GetValue();
                if (connectedValue != null)
                {
                    textContent = connectedValue.ToString() ?? "";
                }
            }
            
            // If no direct text, check for inner Text nodes via Add connections
            if (string.IsNullOrEmpty(textContent))
            {
                var addConnection = Connections.FirstOrDefault(c => c.Type == NodeConnectionType.Add);
                if (addConnection != null && addConnection.ConnectedTo.Any())
                {
                    var connectedNode = addConnection.ConnectedTo.First();
                    if (connectedNode.Parent?.Name == "Text")
                    {
                        // Get the text content from the connected Text node
                        var textNodeConnection = connectedNode.Parent.Connections.FirstOrDefault(c => 
                            string.Equals(c.Attribute, "tag", StringComparison.OrdinalIgnoreCase));
                        
                        if (textNodeConnection?.OverrideValue != null)
                        {
                            textContent = textNodeConnection.OverrideValue.ToString() ?? "";
                        }
                    }
                }
            }
            
            // Translate the text if we found any
            if (!string.IsNullOrEmpty(textContent))
            {
                var translated = TextManager.Get(textContent);
                if (translated.Loaded)
                {
                    textContent = translated.Value;
                }
            }
            
            return textContent;
        }

        private string GetRawTextKey(EventEditorNodeConnection textConnection)
        {
            string textKey = "";
            
            // First check if there's a direct text attribute
            if (textConnection.OverrideValue != null)
            {
                textKey = textConnection.OverrideValue.ToString() ?? "";
            }
            else
            {
                var connectedValue = textConnection.GetValue();
                if (connectedValue != null)
                {
                    textKey = connectedValue.ToString() ?? "";
                }
            }
            
            // If no direct text, check for inner Text nodes via Add connections
            if (string.IsNullOrEmpty(textKey))
            {
                var addConnection = Connections.FirstOrDefault(c => c.Type == NodeConnectionType.Add);
                if (addConnection != null && addConnection.ConnectedTo.Any())
                {
                    var connectedNode = addConnection.ConnectedTo.First();
                    if (connectedNode.Parent?.Name == "Text")
                    {
                        // Get the text key from the connected Text node
                        var textNodeConnection = connectedNode.Parent.Connections.FirstOrDefault(c => 
                            string.Equals(c.Attribute, "tag", StringComparison.OrdinalIgnoreCase));
                        
                        if (textNodeConnection?.OverrideValue != null)
                        {
                            textKey = textNodeConnection.OverrideValue.ToString() ?? "";
                        }
                    }
                }
            }
            
            return textKey;
        }

        protected override bool ShouldDrawConnection(EventEditorNodeConnection connection)
        {
            if (!EventEditorScreen.ConversationMode) { return base.ShouldDrawConnection(connection); }

            // In conversation mode, exclude Options and Text since we draw them manually
            // Also exclude Add connections since we hide the child Text nodes and display their content inline
            return connection.Type == NodeConnectionType.Activate ||
                   connection.Type == NodeConnectionType.Next;
        }

        protected override void DrawConnections(SpriteBatch spriteBatch)
        {
            if (!EventEditorScreen.ConversationMode)
            {
                base.DrawConnections(spriteBatch);
                return;
            }

            // In conversation mode, use the correct rectangle for connection positioning
            Rectangle correctRect = GetDrawRectangle();
            int x = 0, y = 0;
            foreach (EventEditorNodeConnection connection in Connections)
            {
                if (!ShouldDrawConnection(connection)) { continue; }
                
                switch (connection.Type.NodeSide)
                {
                    case NodeConnectionType.Side.Left:
                        connection.Draw(spriteBatch, correctRect, y);
                        y++;
                        break;
                    case NodeConnectionType.Side.Right:
                        connection.Draw(spriteBatch, correctRect, x);
                        x++;
                        break;
                }
            }
        }
    }
    
    internal class EventConversationNode(Type type, string name) : EventTextDisplayNode(type, name)
    {
        protected override bool ShowOptions => true;
    }
    
    internal class EventLogNode(Type type, string name) : EventTextDisplayNode(type, name)
    {
        protected override bool ShowOptions => false;
    }
} 