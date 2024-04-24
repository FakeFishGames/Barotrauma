#nullable enable

using System.Xml.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal sealed partial class CircuitBoxLabelNode : CircuitBoxNode, ICircuitBoxIdentifiable
    {
        public Color Color;
        public ushort ID { get; }

        public override bool IsResizable => true;

        public static NetLimitedString DefaultHeaderText => new("label");

        public NetLimitedString BodyText = NetLimitedString.Empty;
        public NetLimitedString HeaderText = DefaultHeaderText;

        public static Vector2 MinSize = new(128, 8);

        public CircuitBoxLabelNode(ushort id, Color color, Vector2 pos, CircuitBox circuitBox) : base(circuitBox)
        {
            Size = new Vector2(256);
            Position = pos;
            ID = id;
            Color = color;
            UpdatePositions();
#if CLIENT
            bodyLabel = new GUITextBlock(new RectTransform(Point.Zero), text: string.Empty, font: GUIStyle.Font, textAlignment: Alignment.TopLeft, wrap: true);
            headerLabel = new CircuitBoxLabel(HeaderText.Value, GUIStyle.LargeFont);
            UpdateDrawRects();
            UpdateTextSizes(DrawRect);
#endif
        }

        public void EditText(NetLimitedString header, NetLimitedString body)
        {
            HeaderText = header;
            BodyText = body;
#if CLIENT
            UpdateTextSizes(DrawRect);
#endif
        }

        public XElement Save()
        {
            var element = new XElement("Label",
                new XAttribute("id", ID),
                new XAttribute("color", Color.ToStringHex()),
                new XAttribute("position", XMLExtensions.Vector2ToString(Position)),
                new XAttribute("size", XMLExtensions.Vector2ToString(Size)),
                new XAttribute("header", HeaderText),
                new XAttribute("body", BodyText));
            return element;
        }

        public static CircuitBoxLabelNode LoadFromXML(ContentXElement element, CircuitBox circuitBox)
        {
            ushort id = element.GetAttributeUInt16("id", ICircuitBoxIdentifiable.NullComponentID);
            Vector2 position = element.GetAttributeVector2("position", Vector2.Zero);
            Vector2 size = element.GetAttributeVector2("size", Vector2.Zero);
            Color color = element.GetAttributeColor("color", Color.White);
            string header = element.GetAttributeString("header", string.Empty);
            string body = element.GetAttributeString("body", string.Empty);

            var labelNode = new CircuitBoxLabelNode(id, color, position, circuitBox)
            {
                Size = size,
                HeaderText = new NetLimitedString(header),
                BodyText = new NetLimitedString(body)
            };
            // proc a edit to force the sizes to be updated
            labelNode.EditText(new NetLimitedString(header), new NetLimitedString(body));
            labelNode.UpdatePositions();
#if CLIENT
            labelNode.UpdateTextSizes(labelNode.Rect);
#endif
            return labelNode;
        }
    }
}