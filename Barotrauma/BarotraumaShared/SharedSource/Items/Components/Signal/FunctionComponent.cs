using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class FunctionComponent : ItemComponent
    {
        public enum FunctionType
        {
            Round,
            Ceil,
            Floor,
            Factorial,
            AbsoluteValue,
            SquareRoot
        }

        [Serialize(FunctionType.Round, false, description: "Which kind of function to run the input through.", alwaysUseInstanceValues: true)]
        public FunctionType Function
        {
            get; set;
        }

        public FunctionComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1)
        {
            if (connection.Name != "signal_in") return;
            if (!float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) { return; }
            switch (Function)
            {
                case FunctionType.Round:
                    item.SendSignal(stepsTaken, Math.Round(value).ToString("G", CultureInfo.InvariantCulture), "signal_out", sender, source: source);
                    break;
                case FunctionType.Ceil:
                    item.SendSignal(stepsTaken, Math.Ceiling(value).ToString("G", CultureInfo.InvariantCulture), "signal_out", sender, source: source);
                    break;
                case FunctionType.Floor:
                    item.SendSignal(stepsTaken, Math.Floor(value).ToString("G", CultureInfo.InvariantCulture), "signal_out", sender, source: source);
                    break;
                case FunctionType.Factorial:
                    int intVal = (int)Math.Min(value, 20);
                    ulong factorial = 1;
                    for (int i = intVal; i > 0; i--)
                    {
                        factorial *= (ulong)i;
                    }
                    item.SendSignal(stepsTaken, factorial.ToString(), "signal_out", sender, source: source);
                    break;
                case FunctionType.AbsoluteValue:
                    item.SendSignal(stepsTaken, Math.Abs(value).ToString("G", CultureInfo.InvariantCulture), "signal_out", sender, source: source);
                    break;
                case FunctionType.SquareRoot:
                    if (value > 0)
                    {
                        item.SendSignal(stepsTaken, Math.Sqrt(value).ToString("G", CultureInfo.InvariantCulture), "signal_out", sender, source: source);
                    }
                    break;
                default:
                    throw new NotImplementedException($"Function {Function} has not been implemented.");
            }
        }
    }
}
