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
            if (connection.Name != "signal_in") { return; }
            if (!float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) { return; }
            switch (Function)
            {
                case FunctionType.Round:
                    value = MathF.Round(value);
                    if (value == -0)
                    {
                        value = 0;
                    }
                    break;
                case FunctionType.Ceil:
                    value = MathF.Ceiling(value);
                    if (value == -0)
                    {
                        value = 0;
                    }
                    break;
                case FunctionType.Floor:
                    value = MathF.Floor(value);
                    break;
                case FunctionType.Factorial:
                    int intVal = (int)Math.Min(value, 20);
                    ulong factorial = 1;
                    for (int i = intVal; i > 0; i--)
                    {
                        factorial *= (ulong)i;
                    }
                    value = factorial;
                    break;
                case FunctionType.AbsoluteValue:
                    value = MathF.Abs(value);
                    break;
                case FunctionType.SquareRoot:
                    if (value < 0)
                    {
                        return;
                    }
                    value = MathF.Sqrt(value);
                    break;
                default:
                    throw new NotImplementedException($"Function {Function} has not been implemented.");
            }

            signal.value = value.ToString("G", CultureInfo.InvariantCulture);
            item.SendSignal(signal, "signal_out");
        }
    }
}
