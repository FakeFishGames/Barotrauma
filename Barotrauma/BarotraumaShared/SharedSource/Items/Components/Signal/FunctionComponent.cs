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

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (connection.Name != "signal_in") return;
            if (!float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return;
            switch (Function)
            {
                case FunctionType.Round:
                    item.SendSignal(Math.Round(value).ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    break;
                case FunctionType.Ceil:
                    item.SendSignal(Math.Ceiling(value).ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    break;
                case FunctionType.Floor:
                    item.SendSignal(Math.Floor(value).ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    break;
                case FunctionType.Factorial:
                    int intVal = (int)Math.Min(value, 20);
                    ulong factorial = 1;
                    for (int i = intVal; i > 0; i--)
                    {
                        factorial *= (ulong)i;
                    }
                    item.SendSignal(factorial.ToString(), "signal_out");
                    break;
                case FunctionType.AbsoluteValue:
                    item.SendSignal(Math.Abs(value).ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    break;
                case FunctionType.SquareRoot:
                    if (value > 0)
                    {
                        item.SendSignal(Math.Sqrt(value).ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    }
                    break;
                default:
                    throw new NotImplementedException($"Function {Function} has not been implemented.");
            }
        }
    }
}
