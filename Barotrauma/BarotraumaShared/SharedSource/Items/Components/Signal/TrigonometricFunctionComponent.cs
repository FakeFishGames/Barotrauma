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

        protected float[] receivedSignal = new float[2];

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

        public override void Update(float deltaTime, Camera cam)
        {
            //reset received signals
            receivedSignal[0] = float.NaN;
            receivedSignal[1] = float.NaN;
        }


        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1)
        {
            float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
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
                    if (connection.Name == "signal_in_x")
                    {
                        float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[0]);
                    }
                    else if (connection.Name == "signal_in_y")
                    {
                        float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[1]);   
                        if (!float.IsNaN(receivedSignal[0]) && !float.IsNaN(receivedSignal[1]))
                        {
                            float angle = (float)Math.Atan2(receivedSignal[1], receivedSignal[0]);
                            if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                            item.SendSignal(0, angle.ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                        }
                    }
                    else
                    {
                        float angle = (float)Math.Atan(value);
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
