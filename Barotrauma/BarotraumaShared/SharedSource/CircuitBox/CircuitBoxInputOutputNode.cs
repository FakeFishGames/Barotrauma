#nullable enable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal sealed class CircuitBoxInputOutputNode : CircuitBoxNode
    {
        public enum Type
        {
            Invalid,
            Input,
            Output
        }

        public Type NodeType;

        public CircuitBoxInputOutputNode(IReadOnlyList<CircuitBoxConnection> conns, Vector2 initialPosition, Type type, CircuitBox circuitBox): base(circuitBox)
        {
            Size = CalculateSize(conns);
            Connectors = conns.ToImmutableArray();
            Position = initialPosition;
            NodeType = type;
            UpdatePositions();
        }

        public XElement Save() => new XElement($"{NodeType}Node", new XAttribute("pos", XMLExtensions.Vector2ToString(Position)));

        public void Load(ContentXElement element)
        {
            Position = element.GetAttributeVector2("pos", Vector2.Zero);
        }
    }
}