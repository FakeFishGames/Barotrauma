#nullable enable

using System.Collections.Generic;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{

    internal class CircuitBoxInputConnection : CircuitBoxConnection
    {
        /// <summary>
        /// Circuit box input connection is classified as an output because it behaves like an output inside the circuit box.
        /// As in you can connect it to a input pin of a component.
        /// </summary>
        public override bool IsOutput => true;
        public readonly List<CircuitBoxConnection> ExternallyConnectedTo = new();

        public CircuitBoxInputConnection(Vector2 position, Connection connection, CircuitBox box) : base(position, connection, box) { }

        public override void ReceiveSignal(Signal signal)
        {
            foreach (CircuitBoxConnection connector in ExternallyConnectedTo)
            {
                if (connector is CircuitBoxOutputConnection output)
                {
                    output.ReceiveSignal(signal);
                    continue;
                }
                Connection.SendSignalIntoConnection(signal, connector.Connection);
            }
        }
    }

    internal class CircuitBoxOutputConnection : CircuitBoxConnection
    {
        /// <summary>
        /// Circuit box output connection is classified as an input because it behaves like an input inside the circuit box.
        /// As in you can connect it to a output pin of a component.
        /// </summary>
        public override bool IsOutput => false;

        public CircuitBoxOutputConnection(Vector2 position, Connection connection, CircuitBox circuitBox) : base(position, connection, circuitBox) { }

        public override void ReceiveSignal(Signal signal) => CircuitBox.Item.SendSignal(signal, Connection);
    }

    internal class CircuitBoxNodeConnection : CircuitBoxConnection
    {
        public override bool IsOutput => Connection.IsOutput;

        public CircuitBoxComponent Component;

        public bool HasAvailableSlots => Connection.WireSlotsAvailable();

        public CircuitBoxNodeConnection(Vector2 position, CircuitBoxComponent component, Connection connection, CircuitBox circuitBox) : base(position, connection, circuitBox)
        {
            Component = component;
        }

        public override void ReceiveSignal(Signal signal) => Connection.SendSignalIntoConnection(signal, Connection);
    }

    internal abstract partial class CircuitBoxConnection
    {
        public readonly Connection Connection;

        public abstract bool IsOutput { get; }

        public RectangleF Rect;

        private Vector2 position;

        public readonly List<CircuitBoxConnection> ExternallyConnectedFrom = new();

        public static readonly float Size = CircuitBoxSizes.ConnectorSize;

        public Vector2 Position
        {
            get => position;
            set
            {
                Rect.X = value.X - Rect.Width / 2f;
                Rect.Y = value.Y - Rect.Height / 2f;
                position = value;
            }
        }

        public float Length { get; private set; }

        public Vector2 AnchorPoint
            => new Vector2(IsOutput ? Rect.Right + CircuitBoxSizes.AnchorOffset : Rect.Left - CircuitBoxSizes.AnchorOffset, Rect.Center.Y);

        public readonly CircuitBox CircuitBox;

        protected CircuitBoxConnection(Vector2 position, Connection connection, CircuitBox circuitBox)
        {
            Connection = connection;
            Rect.Width = Rect.Height = Size;
            Position = position;
            CircuitBox = circuitBox;
            InitProjSpecific(circuitBox);
        }

        private partial void InitProjSpecific(CircuitBox circuitBox);

        public abstract void ReceiveSignal(Signal signal);

        public bool Contains(Vector2 pos)
        {
            float x = Rect.X,
                  y = -(Rect.Y + Rect.Height),
                  width = Rect.Width,
                  height = Rect.Height;

            RectangleF rect = new(x, y, width, height);

            return rect.Contains(pos);
        }
    }
}