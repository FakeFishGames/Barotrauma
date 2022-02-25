#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    internal class NodeConnectionType
    {
        public static readonly NodeConnectionType Activate = new NodeConnectionType(Side.Left, "Activate");
        public static readonly NodeConnectionType Value = new NodeConnectionType(Side.Left, "Value");
        public static readonly NodeConnectionType Option = new NodeConnectionType(Side.Right, "Option", new[] { Activate });
        public static readonly NodeConnectionType Add = new NodeConnectionType(Side.Right, "Add", new[] { Activate });
        public static readonly NodeConnectionType Success = new NodeConnectionType(Side.Right, "Success", new[] { Activate });
        public static readonly NodeConnectionType Failure = new NodeConnectionType(Side.Right, "Failure", new[] { Activate });
        public static readonly NodeConnectionType Next = new NodeConnectionType(Side.Right, "Next", new[] { Activate });
        public static readonly NodeConnectionType Out = new NodeConnectionType(Side.Right, "Out", new[] { Value });

        public enum Side
        {
            Left,
            Right
        }

        public Side NodeSide { get; }

        public string Label { get; }

        public NodeConnectionType[]? AllowedConnections { get; }

        private NodeConnectionType(Side side, string name, NodeConnectionType[]? allowedConnections = null)
        {
            NodeSide = side;
            Label = name;
            AllowedConnections = allowedConnections;
        }
    }

    internal class NodeConnection
    {
        public string Attribute { get; }

        public int ID { get; set; }

        public bool EndConversation { get; set; }

        private string? optionText;

        public string? OptionText
        {
            get => optionText;
            set
            {
                optionText = value;
                if (value is string)
                {
                    actualValue = WrappedValue = TextManager.Get(value).Fallback(value).Value;
                }
                else
                {
                    actualValue = WrappedValue = value;
                }
            }
        }

        public NodeConnectionType Type { get; }

        public Type? ValueType { get; }

        private object? overrideValue;
        private object? actualValue;

        public object? OverrideValue
        {
            get => overrideValue;
            set
            {
                overrideValue = value;
                if (value is string str)
                {
                    actualValue = WrappedValue = TextManager.Get(str).Fallback(str).Value;
                }
                else
                {
                    actualValue = WrappedValue = value?.ToString() ?? string.Empty;
                }
            }
        }

        private string? wrappedValue;

        private string? WrappedValue
        {
            get => wrappedValue;
            set
            {
                string valueText = value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(valueText))
                {
                    wrappedValue = null;
                    return;
                }
                Vector2 textSize = GUIStyle.SmallFont.MeasureString(valueText);
                bool wasWrapped = false;
                while (textSize.X > 96)
                {
                    wasWrapped = true;
                    valueText = $"{valueText}...".Substring(0, valueText.Length - 4);
                    textSize = GUIStyle.SmallFont.MeasureString($"{valueText}...");
                }

                if (wasWrapped)
                {
                    valueText = valueText.TrimEnd(' ') + "...";
                }
                    

                wrappedValue = valueText;
            }
        }

        public PropertyInfo? PropertyInfo { get; }

        public Rectangle DrawRectangle = Rectangle.Empty;

        public readonly EditorNode Parent;

        public readonly List<NodeConnection> ConnectedTo = new List<NodeConnection>();

        private readonly Color bgColor = Color.DarkGray * 0.8f;

        private readonly Color outlineColor = Color.White * 0.8f;

        public object? GetValue()
        {
            if (OverrideValue != null)
            {
                return OverrideValue;
            }

            foreach (EditorNode editorNode in EventEditorScreen.nodeList)
            {
                var outNode = editorNode.Connections.Find(connection => connection.Type == NodeConnectionType.Out);
                if (outNode != null && outNode.ConnectedTo.Contains(this))
                {
                    return (outNode.Parent as ValueNode)?.Value;
                }
            }

            return null;
        }

        public void ClearConnections()
        {
            foreach (var connection in EventEditorScreen.nodeList.SelectMany(editorNode => editorNode.Connections.Where(connection => connection.ConnectedTo.Contains(this))))
            {
                connection.ConnectedTo.Remove(this);
            }

            ConnectedTo.Clear();
        }

        public NodeConnection(EditorNode parent, NodeConnectionType type, string attribute = "", Type? valueType = null, PropertyInfo? propertyInfo = null)
        {
            Type = type;
            ValueType = valueType;
            Attribute = attribute;
            PropertyInfo = propertyInfo;
            Parent = parent;
            ID = parent.Connections.Count;
        }

        private Point GetRenderPos(Rectangle parentRectangle, int yOffset)
        {
            int x = Type.NodeSide == NodeConnectionType.Side.Left ? parentRectangle.Left - 15 : parentRectangle.Right - 1;
            return new Point(x, parentRectangle.Y + 8 + parentRectangle.Height / 8 * yOffset);
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle parentRectangle, int yOffset)
        {
            float camZoom = Screen.Selected is EventEditorScreen eventEditor ? eventEditor.Cam.Zoom : 1.0f;
            Point pos = GetRenderPos(parentRectangle, yOffset);
            DrawRectangle = new Rectangle(pos, new Point(16, 16));
            GUI.DrawRectangle(spriteBatch, DrawRectangle, bgColor, isFilled: true);
            GUI.DrawRectangle(spriteBatch, DrawRectangle, EndConversation ? GUIStyle.Red : outlineColor, isFilled: false, thickness: (int)Math.Max(1, 1.25f / camZoom));

            string label = string.IsNullOrWhiteSpace(Attribute) ? Type.Label : Attribute;
            float xPos = parentRectangle.Center.X > pos.X ? 24 : -8 - GUIStyle.SmallFont.MeasureString(label).X;

            if (Type != NodeConnectionType.Out)
            {
                Vector2 size = GUIStyle.SmallFont.MeasureString(label);
                Vector2 positon = new Vector2(pos.X + xPos, pos.Y);
                Rectangle bgRect = new Rectangle(positon.ToPoint(), size.ToPoint());
                bgRect.Inflate(4, 4);

                GUI.DrawRectangle(spriteBatch, bgRect, Color.Black * 0.6f, isFilled: true);
                GUI.DrawString(spriteBatch, positon, label, GetPropertyColor(ValueType), font: GUIStyle.SmallFont);

                Vector2 mousePos = Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition);
                mousePos.Y = -mousePos.Y;
                if (bgRect.Contains(mousePos))
                {
                    CustomAttributeData? attribute = PropertyInfo?.CustomAttributes.FirstOrDefault();
                    if (attribute?.AttributeType == typeof(Serialize))
                    {
                        if (attribute.ConstructorArguments.Count > 2)
                        {
                            string? description = attribute.ConstructorArguments[2].Value as string;
                            if (!string.IsNullOrWhiteSpace(description))
                            {
                                EventEditorScreen.DrawnTooltip = description;
                            }
                        }
                    }
                }
            }

            if (OverrideValue != null)
            {
                DrawLabel(spriteBatch, new Vector2(DrawRectangle.Center.X - 96, pos.Y + (DrawRectangle.Height / 2) - (20 / 2)), WrappedValue ?? "null", actualValue?.ToString() ?? string.Empty);
            }
            
            if (OptionText != null)
            {
                DrawLabel(spriteBatch, new Vector2(DrawRectangle.Center.X, pos.Y + (DrawRectangle.Height / 2) - (20 / 2)), WrappedValue ?? "null", actualValue?.ToString() ?? string.Empty);
            }

            if (Parent.IsHighlighted)
            {
                DrawConnections(spriteBatch, yOffset, Math.Max(8.0f, 8.0f / camZoom), Color.Red);
            }

            DrawConnections(spriteBatch, yOffset, width: Math.Max(2.0f, 2.0f / camZoom));

            if (EventEditorScreen.DraggedConnection == this)
            {
                DrawSquareLine(spriteBatch, EventEditorScreen.DraggingPosition, yOffset, width: Math.Max(2.0f, 2.0f / camZoom));
            }
        }

        private void DrawConnections(SpriteBatch spriteBatch, int yOffset, float width = 2, Color? overrideColor = null)
        {
            foreach (NodeConnection? eventNodeConnection in ConnectedTo)
            {
                if (eventNodeConnection != null)
                {
                    DrawSquareLine(spriteBatch, new Vector2(eventNodeConnection.DrawRectangle.Left + 1, eventNodeConnection.DrawRectangle.Center.Y), yOffset, width, overrideColor);
                }
            }
        }

        private void DrawLabel(SpriteBatch spriteBatch, Vector2 pos, string text, string fullText)
        {
            float camZoom = Screen.Selected is EventEditorScreen eventEditor ? eventEditor.Cam.Zoom : 1.0f;
            Rectangle valueRect = new Rectangle((int)pos.X, (int)pos.Y, 96, 20);
            Vector2 textSize = GUIStyle.SmallFont.MeasureString(text);
            Vector2 position = valueRect.Location.ToVector2() + valueRect.Size.ToVector2() / 2 - textSize / 2;
            Rectangle drawRect = valueRect;
            drawRect.Inflate(4, 4);
            GUI.DrawRectangle(spriteBatch, drawRect, new Color(50, 50, 50), isFilled: true);
            GUI.DrawRectangle(spriteBatch, drawRect, EndConversation ? GUIStyle.Red : outlineColor, isFilled: false, thickness: (int)Math.Max(1, 1.25f / camZoom));
            GUI.DrawString(spriteBatch, position, text, GetPropertyColor(ValueType), font: GUIStyle.SmallFont);
            DrawRectangle = Rectangle.Union(DrawRectangle, drawRect);

            if (!string.IsNullOrWhiteSpace(fullText))
            {
                Vector2 mousePos = Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition);
                mousePos.Y = -mousePos.Y;
                if (DrawRectangle.Contains(mousePos))
                {
                    EventEditorScreen.DrawnTooltip = fullText;
                }
            }
        }

        private void DrawSquareLine(SpriteBatch spriteBatch, Vector2 position, int yOffset, float width = 2, Color? overrideColor = null)
        {
            // draw a line between 2 nodes using points
            // the order of this array is messed up, I know
            // order of points is from start node to end node: 0, 4, 1, 2, 5, 3
            Vector2[] points = new Vector2[6];
            int xOffset = 24 * (yOffset + 1);
            points[0] = new Vector2(DrawRectangle.Right, DrawRectangle.Center.Y);
            points[3] = position;
            points[1] = points[0];
            points[2] = points[3];

            points[4] = points[1];
            points[5] = points[2];

            points[1].X += (points[2].X - points[1].X) / 2;
            points[1].X = Math.Max(points[1].X, points[0].X + xOffset);
            points[2].X = points[1].X;

            // if the node is "behind" us do some magic to make the line curve to prevent overlapping
            if (points[1].X <= points[0].X + xOffset)
            {
                points[4].X += xOffset;
                points[1].X = points[4].X;
                points[1].Y += (points[2].Y - points[1].Y) / 2;
            }

            if (points[2].X >= points[3].X - xOffset)
            {
                points[5].X -= xOffset;
                points[2].X = points[5].X;
                points[2].Y -= points[2].Y - points[1].Y;
            }

            Color drawColor = Parent is ValueNode ? GetPropertyColor(ValueType) : GUIStyle.Red;

            if (overrideColor != null)
            {
                drawColor = overrideColor.Value;
            }

            GUI.DrawLine(spriteBatch, points[0], points[4], drawColor, width: (int)width);
            GUI.DrawLine(spriteBatch, points[4], points[1], drawColor, width: (int)width);
            GUI.DrawLine(spriteBatch, points[1], points[2], drawColor, width: (int)width);
            GUI.DrawLine(spriteBatch, points[2], points[5], drawColor, width: (int)width);
            GUI.DrawLine(spriteBatch, points[5], points[3], drawColor, width: (int)width);
        }

        private static readonly Color defaultColor = new Color(139, 233, 253);
        private static readonly Color yellowColor = new Color(241, 250, 140);
        private static readonly Color pinkColor = new Color(255, 121, 198);
        private static readonly Color purpleColor = new Color(189, 147, 249);

        public static Color GetPropertyColor(Type? valueType)
        {
            Color color = defaultColor;
            if (valueType == typeof(bool))
                color = pinkColor;
            else if (valueType == typeof(string))
                color = yellowColor;
            else if (valueType == typeof(int) ||
                     valueType == typeof(float) ||
                     valueType == typeof(double))
                color = purpleColor;
            else if (valueType == null) color = Color.White;
            return color;
        }

        public bool CanConnect(NodeConnection otherNode)
        {
            if (otherNode.OverrideValue != null)
            {
                return false;
            }

            return Type.AllowedConnections == null || Type.AllowedConnections.Contains(otherNode.Type);
        }
    }
}