#nullable enable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal sealed partial class CircuitBoxInputOutputNode : CircuitBoxNode
    {
        public enum Type
        {
            Invalid,
            Input,
            Output
        }

        public readonly Type NodeType;

        private const int MaxConnectionLabelLength = 32;
        private const string ConnectionLabelOverrideElementName = "ConnectionLabelOverride";

        public Dictionary<string, string> ConnectionLabelOverrides = new();

        public CircuitBoxInputOutputNode(IReadOnlyList<CircuitBoxConnection> conns, Vector2 initialPosition, Type type, CircuitBox circuitBox): base(circuitBox)
        {
            InitSize(conns);
            Connectors = conns.ToImmutableArray();
            Position = initialPosition;
            NodeType = type;
            UpdatePositions();
        }

        public void ReplaceAllConnectionLabelOverrides(Dictionary<string, string> replace)
        {
            foreach (var (_, value) in replace)
            {
                if (value.Length > MaxConnectionLabelLength)
                {
                    DebugConsole.ThrowError($"Label override value \"{value}\" is too long (max {MaxConnectionLabelLength} characters)");
                    return;
                }
            }

            foreach (var (name, value) in replace)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    ConnectionLabelOverrides.Remove(name);
                }
                else
                {
                    ConnectionLabelOverrides[name] = value;
                }
            }

            InitSize(Connectors);
            UpdatePositions();
        }

        private void InitSize(IReadOnlyList<CircuitBoxConnection> conns)
        {
#if CLIENT
            foreach (CircuitBoxConnection conn in conns)
            {
                if (ConnectionLabelOverrides.TryGetValue(conn.Name, out string? value))
                {
                    LocalizedString newLabel =
                        string.IsNullOrWhiteSpace(value)
                            ? conn.Connection.DisplayName
                            : TextManager.Get(value).Fallback(value);

                    conn.SetLabel(newLabel, this);
                }
                else
                {
                    conn.SetLabel(conn.Connection.DisplayName, this);
                }
            }
#endif
            Size = CalculateSize(conns);
        }

        public XElement Save()
        {
            XElement element = new XElement($"{NodeType}Node", new XAttribute("pos", XMLExtensions.Vector2ToString(Position)));

            foreach (var (name, value) in ConnectionLabelOverrides)
            {
                element.Add(new XElement(ConnectionLabelOverrideElementName,
                    new XAttribute("name", name),
                    new XAttribute("value", value)));
            }

            return element;
        }

        public void Load(ContentXElement element)
        {
            Position = element.GetAttributeVector2("pos", Vector2.Zero);

            Dictionary<string, string> loadedOverrides = new();
            foreach (var subElement in element.Elements())
            {
                if (subElement.Name != ConnectionLabelOverrideElementName) { continue; }

                string name = subElement.GetAttributeString("name", string.Empty);
                string value = subElement.GetAttributeString("value", string.Empty);

                loadedOverrides[name] = value;
            }

            ConnectionLabelOverrides = loadedOverrides;
            InitSize(Connectors);
            UpdatePositions();
        }
    }
}