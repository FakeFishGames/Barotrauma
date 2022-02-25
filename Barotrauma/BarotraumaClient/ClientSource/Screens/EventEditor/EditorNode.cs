#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    internal class EditorNode
    {
        public Vector2 Position { get; set; }

        public Vector2 Size { get; set; }

        public int ID;

        private const int HeaderSize = 32;

        public Rectangle HeaderRectangle => new Rectangle(Position.ToPoint(), new Point((int) Size.X, HeaderSize));
        public Rectangle Rectangle => new Rectangle(new Point((int) Position.X, (int) Position.Y + HeaderSize), Size.ToPoint());
        public string Name { get; protected set; }

        public bool CanAddConnections { get; set; }

        public readonly List<NodeConnection> Connections = new List<NodeConnection>();

        public readonly List<NodeConnectionType> RemovableTypes = new List<NodeConnectionType>();

        public bool IsHighlighted;

        public bool IsSelected;

        protected EditorNode(string name)
        {
            Name = name;
            Position = Vector2.Zero;
        }

        public virtual XElement Save()
        {
            throw new NotImplementedException();
        }

        public XElement SaveConnections()
        {
            XElement allConnections = new XElement("Connections", new XAttribute("i", ID));
            foreach (NodeConnection connection in Connections)
            {
                XElement connectionElement = new XElement("Connection");
                connectionElement.Add(new XAttribute("i", connection.ID));
                connectionElement.Add(new XAttribute("type", connection.Type.Label));

                if (connection.EndConversation)
                {
                    connectionElement.Add(new XAttribute("endconversation", connection.EndConversation));
                }

                if (!string.IsNullOrWhiteSpace(connection.OptionText))
                {
                    connectionElement.Add(new XAttribute("optiontext", connection.OptionText));
                }

                if (connection.OverrideValue is { } overrideValue && !string.IsNullOrWhiteSpace(connection.OverrideValue?.ToString()))
                {
                    connectionElement.Add(new XAttribute("overridevalue", overrideValue.ToString() ?? string.Empty));
                    connectionElement.Add(new XAttribute("valuetype", overrideValue.GetType().ToString()));
                }

                foreach (var nodeConnection in connection.ConnectedTo)
                {
                    XElement connectedTo = new XElement("ConnectedTo",
                        new XAttribute("i", nodeConnection.ID),
                        new XAttribute("node", nodeConnection.Parent.ID));
                    connectionElement.Add(connectedTo);
                }

                allConnections.Add(connectionElement);
            }

            return allConnections;
        }

        public void LoadConnections(XElement element)
        {
            foreach (var subElement in element.Elements())
            {
                int id = subElement.GetAttributeInt("i", -1);
                string? connectionType = subElement.GetAttributeString("type", null);
                bool endConversation = subElement.GetAttributeBool("endconversation", false);

                if (id < 0) { continue; }

                NodeConnection? connection = Connections.Find(c => c.ID == id);
                if (connection == null)
                {
                    if (string.Equals(connectionType, NodeConnectionType.Option.Label, StringComparison.InvariantCultureIgnoreCase))
                    {
                        connection = new NodeConnection(this, NodeConnectionType.Option) { ID = id, EndConversation = endConversation };
                        Connections.Add(connection);
                    }
                    else
                    {
                        continue;
                    }
                }

                string? optionText = subElement.GetAttributeString("optiontext", null);
                string? overrideValue = subElement.GetAttributeString("overridevalue", null);
                string? valueType = subElement.GetAttributeString("valuetype", null);

                if (optionText != null) { connection.OptionText = optionText; }

                if (overrideValue != null && valueType != null)
                {
                    Type? type = Type.GetType(valueType);
                    if (type != null)
                    {
                        if (type.IsEnum)
                        {
                            Array enums = Enum.GetValues(type);
                            foreach (object? @enum in enums)
                            {
                                if (string.Equals(@enum?.ToString(), overrideValue, StringComparison.InvariantCultureIgnoreCase))
                                {
                                    connection.OverrideValue = @enum;
                                }
                            }
                        }
                        else
                        {
                            connection.OverrideValue = Convert.ChangeType(overrideValue, type);
                        }
                    }
                }

                foreach (XElement connectedTo in subElement.Elements())
                {
                    int id2 = connectedTo.GetAttributeInt("i", -1);
                    int node = connectedTo.GetAttributeInt("node", -1);
                    if (id2 < 0 || node < 0) { continue; }

                    EditorNode? otherNode = EventEditorScreen.nodeList.Find(editorNode => editorNode.ID == node);
                    NodeConnection? otherConnection = otherNode?.Connections.Find(c => c.ID == id2);
                    if (otherConnection != null)
                    {
                        connection.ConnectedTo.Add(otherConnection);
                    }
                }
            }
        }

        public static EditorNode? Load(XElement element)
        {
            return element.Name.ToString().ToLowerInvariant() switch
            {
                "eventnode" => EventNode.LoadEventNode(element),
                "valuenode" => ValueNode.LoadValueNode(element),
                "customnode" => CustomNode.LoadCustomNode(element),
                _ => null
            };
        }

        public virtual XElement? ToXML()
        {
            XElement newElement = new XElement(Name);
            foreach (var connection in Connections)
            {
                if (connection.Type == NodeConnectionType.Value)
                {
                    if (connection.GetValue() is { } connValue)
                    {
                        newElement.Add(new XAttribute(connection.Attribute.ToLowerInvariant(), connValue));
                    }
                }
            }

            newElement.Add(new XAttribute("_npos", XMLExtensions.Vector2ToString(Position)));

            return newElement;
        }

        public void Connect(EditorNode otherNode, NodeConnectionType type)
        {
            NodeConnection? conn = Connections.Find(connection => connection.Type == type && !connection.ConnectedTo.Any());
            NodeConnection? found = otherNode.Connections.Find(connection => connection.Type == NodeConnectionType.Activate);
            if (found != null)
            {
                conn?.ConnectedTo.Add(found);
            }
        }

        public void Connect(NodeConnection connection, NodeConnection ownConnection)
        {
            connection.ConnectedTo.Add(ownConnection);
        }

        public void Disconnect(NodeConnection conn)
        {
            foreach (var connection in EventEditorScreen.nodeList.SelectMany(editorNode => editorNode.Connections.Where(connection => connection.ConnectedTo.Contains(conn))))
            {
                connection.ConnectedTo.Remove(conn);
            }
        }

        public void ClearConnections()
        {
            foreach (NodeConnection conn in Connections)
            {
                conn.ClearConnections();
            }
        }

        public virtual Rectangle GetDrawRectangle()
        {
            return Rectangle;
        }

        public NodeConnection? GetConnectionOnMouse(Vector2 mousePos)
        {
            return Connections.FirstOrDefault(eventNodeConnection => eventNodeConnection.DrawRectangle.Contains(mousePos));
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            DrawBack(spriteBatch);
            DrawFront(spriteBatch);
        }

        protected virtual void DrawFront(SpriteBatch spriteBatch) { }

        protected virtual Color BackgroundColor => new Color(150, 150, 150);

        private void DrawBack(SpriteBatch spriteBatch)
        {
            Color outlineColor = Color.White * 0.8f;
            Color fontColor = Color.White;
            Color headerColor = IsHighlighted ? new Color(100, 100, 100) : new Color(120, 120, 120);
            if (IsSelected)
            {
                headerColor = new Color(80, 80, 80);
            }
            
            float camZoom = Screen.Selected is EventEditorScreen eventEditor ? eventEditor.Cam.Zoom : 1.0f;

            Rectangle bodyRect = GetDrawRectangle();

            GUI.DrawRectangle(spriteBatch, HeaderRectangle, headerColor, isFilled: true, depth: 1.0f);
            GUI.DrawRectangle(spriteBatch, bodyRect, BackgroundColor, isFilled: true, depth: 1.0f);

            GUI.DrawRectangle(spriteBatch, HeaderRectangle, outlineColor, isFilled: false, depth: 1.0f, thickness: (int) Math.Max(1, 1.25f / camZoom));
            GUI.DrawRectangle(spriteBatch, bodyRect, outlineColor, isFilled: false, depth: 1.0f, thickness: (int) Math.Max(1, 1.25f / camZoom));

            int x = 0, y = 0;
            foreach (NodeConnection connection in Connections)
            {
                switch (connection.Type.NodeSide)
                {
                    case NodeConnectionType.Side.Left:
                        connection.Draw(spriteBatch, Rectangle, y);
                        y++;
                        break;
                    case NodeConnectionType.Side.Right:
                        connection.Draw(spriteBatch, Rectangle, x);
                        x++;
                        break;
                }
            }

            Vector2 headerSize = GUIStyle.SubHeadingFont.MeasureString(Name);
            GUIStyle.SubHeadingFont.DrawString(spriteBatch, Name, HeaderRectangle.Location.ToVector2() + (HeaderRectangle.Size.ToVector2() / 2) - (headerSize / 2), fontColor);
        }

        public virtual void AddOption()
        {
            Connections.Add(new NodeConnection(this, NodeConnectionType.Option));
        }

        public void RemoveOption(NodeConnection connection)
        {
            int index = Connections.IndexOf(connection);
            foreach (var nodeConnection in Connections.Skip(index))
            {
                nodeConnection.ID--;
            }

            Connections.Remove(connection);
        }
        
        public EditorNode? GetNext()
        {
            var nextNode = Connections.Find(connection => connection.Type == NodeConnectionType.Next);
            return nextNode?.ConnectedTo.FirstOrDefault()?.Parent;
        }

        public EditorNode? GetNext(NodeConnectionType type)
        {
            var nextNode = Connections.Find(connection => connection.Type == type);
            return nextNode?.ConnectedTo.FirstOrDefault()?.Parent;
        }

        public static bool IsInstanceOf(Type type1, Type type2)
        {
            return type1.IsAssignableFrom(type2) || type1.IsSubclassOf(type2);
        }

        public EditorNode? GetParent()
        {
            var myNode = Connections.Find(connection => connection.Type == NodeConnectionType.Activate);
            if (myNode == null) { return null; }

            foreach (EditorNode editorNode in EventEditorScreen.nodeList)
            {
                List<NodeConnection> childConnection = editorNode.Connections.Where(connection => connection.Type == NodeConnectionType.Next ||
                                                                                                  connection.Type == NodeConnectionType.Option ||
                                                                                                  connection.Type == NodeConnectionType.Failure ||
                                                                                                  connection.Type == NodeConnectionType.Success ||
                                                                                                  connection.Type == NodeConnectionType.Add).ToList();
                if (childConnection.Any(connection => connection != null && connection.ConnectedTo.Contains(myNode)))
                {
                    return editorNode;
                }
            }

            return null;
        }
    }

    internal class EventNode : EditorNode
    {
        private readonly Type type;

        public EventNode(Type type, string name) : base(name)
        {
            this.type = type;
            Size = new Vector2(256, 256);
            PropertyInfo[] properties = type.GetProperties().Where(info => info.CustomAttributes.Any(data => data.AttributeType == typeof(Serialize))).ToArray();

            Connections.Add(new NodeConnection(this, NodeConnectionType.Activate));
            Connections.Add(new NodeConnection(this, NodeConnectionType.Next));

            foreach (PropertyInfo property in properties)
            {
                Connections.Add(new NodeConnection(this, NodeConnectionType.Value, property.Name, property.PropertyType, property));
            }

            if (IsInstanceOf(type, typeof(BinaryOptionAction)))
            {
                Connections.Add(new NodeConnection(this, NodeConnectionType.Success));
                Connections.Add(new NodeConnection(this, NodeConnectionType.Failure));
            }
            
            if (IsInstanceOf(type, typeof(ConversationAction)))
            {
                CanAddConnections = true;
                RemovableTypes.Add(NodeConnectionType.Option);
            }

            if (IsInstanceOf(type, typeof(StatusEffectAction)) || IsInstanceOf(type, typeof(MissionAction)))
            {
                Connections.Add(new NodeConnection(this, NodeConnectionType.Add));
            }
        }

        public override XElement Save()
        {
            XElement newElement = new XElement(nameof(EventNode),
                new XAttribute("i", ID),
                new XAttribute("type", type.ToString()),
                new XAttribute("name", Name),
                new XAttribute("xpos", Position.X),
                new XAttribute("ypos", Position.Y));

            return newElement;
        }

        public static EditorNode? LoadEventNode(XElement element)
        {
            if (!string.Equals(element.Name.ToString(), nameof(EventNode), StringComparison.InvariantCultureIgnoreCase)) { return null; }

            Type? t = Type.GetType(element.GetAttributeString("type", string.Empty));
            if (t == null) { return null; }

            EventNode newNode = new EventNode(t, element.GetAttributeString("name", string.Empty)) { ID = element.GetAttributeInt("i", -1) };
            float posX = element.GetAttributeFloat("xpos", 0f);
            float posY = element.GetAttributeFloat("ypos", 0f);
            newNode.Position = new Vector2(posX, posY);
            return newNode;
        }

        public override Rectangle GetDrawRectangle()
        {
            return ScaleRectFromConnections(Connections, Rectangle);
        }

        public static Rectangle ScaleRectFromConnections(List<NodeConnection> connections, Rectangle baseRect)
        {
            // determine how big this box should get based on how many input/output nodes the sides have
            int y = connections.Count(connection => connection.Type.NodeSide == NodeConnectionType.Side.Left),
                x = connections.Count(connection => connection.Type.NodeSide == NodeConnectionType.Side.Right);
            int maxHeight = Math.Max(x, y);

            Rectangle bodyRect = baseRect;
            bodyRect.Height = bodyRect.Height / 8 * maxHeight;
            return bodyRect;
        }

        public Tuple<EditorNode?, string?, bool>[] GetOptions()
        {
            IEnumerable<NodeConnection> myNode = Connections.Where(connection => connection.Type == NodeConnectionType.Option).ToArray();
            List<Tuple<EditorNode?, string?, bool>> list = new List<Tuple<EditorNode?, string?, bool>>();
            if (myNode != null)
            {
                foreach (NodeConnection connection in myNode)
                {
                    if (connection.ConnectedTo.Any())
                    {
                        foreach (NodeConnection nodeConnection in connection.ConnectedTo)
                        {
                            list.Add(Tuple.Create((EditorNode?) nodeConnection.Parent, connection.OptionText, connection.EndConversation));
                        }
                    }
                    else
                    {
                        list.Add(Tuple.Create<EditorNode?, string?, bool>(null, connection.OptionText, connection.EndConversation));
                    }
                }
            }

            return list.ToArray();
        }
    }

    internal class ValueNode : EditorNode
    {
        private object? nodeValue;

        public object? Value
        {
            get => nodeValue;
            set
            {
                nodeValue = value;
                if (value is string str)
                {
                    WrappedText = TextManager.Get(str) is { Loaded:true } translated ? translated.Value : str;
                }
                else
                {
                    WrappedText = value?.ToString() ?? string.Empty;
                }
                valueTextSize = GUIStyle.SubHeadingFont.MeasureString(WrappedText);
            }
        }

        private Vector2 valueTextSize = Vector2.Zero;

        public Type Type { get; }

        public ValueNode(Type type, string name) : base(name)
        {
            Type = type;
            Value = type.IsValueType ? Activator.CreateInstance(type) : null;
            Size = new Vector2(256, 32);
            Connections.Add(new NodeConnection(this, NodeConnectionType.Out, "Output", Type));
        }

        public override XElement Save()
        {
            XElement newElement = new XElement(nameof(ValueNode));
            newElement.Add(new XAttribute("i", ID));
            if (Value != null)
            {
                newElement.Add(new XAttribute("value", Value));
            }

            newElement.Add(new XAttribute("type", Type.ToString()));
            newElement.Add(new XAttribute("name", Name));
            newElement.Add(new XAttribute("xpos", Position.X));
            newElement.Add(new XAttribute("ypos", Position.Y));
            return newElement;
        }

        public override XElement? ToXML() { return null; }

        public static EditorNode? LoadValueNode(XElement element)
        {
            if (!string.Equals(element.Name.ToString(), nameof(ValueNode), StringComparison.InvariantCultureIgnoreCase)) { return null; }

            string? value = element.GetAttributeString("value", null);
            Type? type = Type.GetType(element.GetAttributeString("type", string.Empty));
            if (type != null)
            {
                ValueNode newNode = new ValueNode(type, element.GetAttributeString("name", string.Empty)) { ID = element.GetAttributeInt("i", -1) };
                float posX = element.GetAttributeFloat("xpos", 0f);
                float posY = element.GetAttributeFloat("ypos", 0f);
                newNode.Position = new Vector2(posX, posY);

                if (value != null)
                {
                    if (type.IsEnum)
                    {
                        Array enums = Enum.GetValues(type);
                        foreach (object? @enum in enums)
                        {
                            if (string.Equals(@enum?.ToString(), value, StringComparison.InvariantCultureIgnoreCase))
                            {
                                newNode.Value = @enum;
                            }
                        }
                    }
                    else
                    {
                        newNode.Value = Convert.ChangeType(value, type);
                    }
                }

                return newNode;
            }

            return null;
        }

        protected override Color BackgroundColor => new Color(50, 50, 50);

        private string? wrappedText;

        private string? WrappedText
        {
            get => wrappedText;
            set
            {
                string valueText = value ?? "null";
                int width = Rectangle.Width;
                if (width == 0)
                {
                    wrappedText = valueText;
                    return;
                }

                if (width > 16)
                {
                    width -= 16;
                }

                valueText = ToolBox.WrapText(valueText, width, GUIStyle.SubHeadingFont.Value);
                wrappedText = valueText;
            }
        }

        public override Rectangle GetDrawRectangle()
        {
            Rectangle drawRectangle = Rectangle;
            Vector2 size = GUIStyle.SubHeadingFont.MeasureString(WrappedText ?? "");
            drawRectangle.Height = (int) Math.Max(size.Y + 16, drawRectangle.Height);
            return drawRectangle;
        }

        protected override void DrawFront(SpriteBatch spriteBatch)
        {
            base.DrawFront(spriteBatch);
            Vector2 pos = GetDrawRectangle().Location.ToVector2() + (GetDrawRectangle().Size.ToVector2() / 2) - (valueTextSize / 2);
            Rectangle drawRect = Rectangle;
            drawRect.Inflate(-1, -1);
            GUI.DrawString(spriteBatch, pos, WrappedText, NodeConnection.GetPropertyColor(Type), font: GUIStyle.SubHeadingFont);
        }
    }

    class SpecialNode : EditorNode
    {
        public SpecialNode(string name) : base(name)
        {
            Size = new Vector2(256, 256);
        }

        public override Rectangle GetDrawRectangle()
        {
            return EventNode.ScaleRectFromConnections(Connections, Rectangle);
        }
    }

    class CustomNode : SpecialNode
    {
        public CustomNode(string name) : base(name)
        {
            CanAddConnections = true;
            RemovableTypes.Add(NodeConnectionType.Value);
            Connections.Add(new NodeConnection(this, NodeConnectionType.Activate));
            Connections.Add(new NodeConnection(this, NodeConnectionType.Next));
            Connections.Add(new NodeConnection(this, NodeConnectionType.Add));
        }

        public CustomNode() : this("Custom")
        {
            Prompt(s =>
            {
                Name = s;
                return true;
            });
        }

        public override void AddOption()
        {
            Prompt(s =>
            {
                Connections.Add(new NodeConnection(this, NodeConnectionType.Value, s, typeof(string)));
                return true;
            });
        }
        
        public override XElement Save()
        {
            XElement newElement = new XElement(nameof(CustomNode));
            newElement.Add(new XAttribute("i", ID));
            newElement.Add(new XAttribute("name", Name));
            newElement.Add(new XAttribute("xpos", Position.X));
            newElement.Add(new XAttribute("ypos", Position.Y));
            foreach (NodeConnection connection in Connections.FindAll(connection => connection.Type == NodeConnectionType.Value))
            {
                newElement.Add(new XElement("Value", new XAttribute("name", connection.Attribute)));
            }
            return newElement;
        }

        public static EditorNode? LoadCustomNode(XElement element)
        {
            if (!string.Equals(element.Name.ToString(), nameof(CustomNode), StringComparison.OrdinalIgnoreCase)) { return null; }

            CustomNode newNode = new CustomNode(element.GetAttributeString("name", string.Empty)) { ID = element.GetAttributeInt("i", -1) };
            float posX = element.GetAttributeFloat("xpos", 0f);
            float posY = element.GetAttributeFloat("ypos", 0f);
            newNode.Position = new Vector2(posX, posY);
            foreach (XElement valueElement in element.Elements())
            {
                newNode.Connections.Add(new NodeConnection(newNode, NodeConnectionType.Value, valueElement.GetAttributeString("name", string.Empty), typeof(string)));
            }
            return newNode;
        }

        private static void Prompt(Func<string, bool> OnAccepted)
        {
            var msgBox = new GUIMessageBox(TextManager.Get("Name"), "", new[] { TextManager.Get("Ok"), TextManager.Get("Cancel") }, new Vector2(0.2f, 0.175f), minSize: new Point(300, 175));
            var layout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), msgBox.Content.RectTransform), isHorizontal: true);
            GUITextBox nameInput = new GUITextBox(new RectTransform(Vector2.One, layout.RectTransform));

            msgBox.Buttons[1].OnClicked = delegate
            {
                msgBox.Close();
                return true;
            };

            msgBox.Buttons[0].OnClicked = delegate
            {
                OnAccepted.Invoke(nameInput.Text);
                msgBox.Close();
                return true;
            };
        }
    }
}