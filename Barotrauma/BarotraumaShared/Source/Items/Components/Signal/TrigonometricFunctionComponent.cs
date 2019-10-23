using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class TrigonometricFunctionComponent : ItemComponent
    {
        public enum FunctionType
        {
            Sin,
            Cos,
            Tan,
            Asin,
            Acos,
            Atan,
        }

        [Serialize(FunctionType.Sin, false, description: "Which kind of function to run the input through.")]
        public FunctionType Function
        {
            get; set;
        }


        [InGameEditable, Serialize(false, true, description: "If set to true, the trigonometric function uses radians instead of degrees.")]
        public bool UseRadians
        {
            get; set;
        }


        public TrigonometricFunctionComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1)
        {
            float.TryParse(signal, out float value);
            switch (Function)
            {
                case FunctionType.Sin:
                    if (!UseRadians) { value = MathHelper.ToRadians(value); }
                    item.SendSignal(0, ((float)Math.Sin(value)).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    break;
                case FunctionType.Cos:
                    if (!UseRadians) { value = MathHelper.ToRadians(value); }
                    item.SendSignal(0, ((float)Math.Cos(value)).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    break;
                case FunctionType.Tan:
                    if (!UseRadians) { value = MathHelper.ToRadians(value); }
                    item.SendSignal(0, ((float)Math.Tan(value)).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    break;
                case FunctionType.Asin:
                    {
                        float angle = (float)Math.Asin(value);
                        if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                        item.SendSignal(0, angle.ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    }
                    break;
                case FunctionType.Acos:
                    {
                        float angle = (float)Math.Acos(value);
                        if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                        item.SendSignal(0, angle.ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    }
                    break;
                case FunctionType.Atan:
                    {
                        float angle;
                        if (signal.Contains(","))
                        {
                            Vector2 vectorValue = XMLExtensions.ParseVector2(signal, errorMessages: false);
                            angle = (float)Math.Atan2(vectorValue.Y, vectorValue.X);
                        }
                        else
                        {
                            angle = (float)Math.Atan(value);
                        }
                        if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                        item.SendSignal(0, angle.ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    }
                    break;
                default:
                    throw new NotImplementedException($"Function {Function} has not been implemented.");
            }
        }
    }
}
