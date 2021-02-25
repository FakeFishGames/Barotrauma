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

        private float[] receivedSignal = new float[2];
        private float[] timeSinceReceived = new float[2];

        [Serialize(FunctionType.Sin, false, description: "Which kind of function to run the input through.", alwaysUseInstanceValues: true)]
        public FunctionType Function
        {
            get; set;
        }


        [InGameEditable, Serialize(false, true, description: "If set to true, the trigonometric function uses radians instead of degrees.", alwaysUseInstanceValues: true)]
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
            if (Function == FunctionType.Atan)
            {
                for (int i = 0; i < 2; i++)
                {
                    timeSinceReceived[i] += deltaTime;
                    if (timeSinceReceived[i] > 0.1f)
                    {
                        receivedSignal[i] = float.NaN;
                    }
                }
                if (!float.IsNaN(receivedSignal[0]) && !float.IsNaN(receivedSignal[1]))
                {
                    float angle = (float)Math.Atan2(receivedSignal[1], receivedSignal[0]);
                    if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                    item.SendSignal(angle.ToString("G", CultureInfo.InvariantCulture), "signal_out");
                }
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
            switch (Function)
            {
                case FunctionType.Sin:
                    if (!UseRadians) { value = MathHelper.ToRadians(value); }
                    item.SendSignal(((float)Math.Sin(value)).ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    break;
                case FunctionType.Cos:
                    if (!UseRadians) { value = MathHelper.ToRadians(value); }
                    item.SendSignal(((float)Math.Cos(value)).ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    break;
                case FunctionType.Tan:
                    if (!UseRadians) { value = MathHelper.ToRadians(value); }
                    //tan is undefined if the value is (π / 2) + πk, where k is any integer
                    if (!MathUtils.NearlyEqual(value % MathHelper.Pi, MathHelper.PiOver2))
                    {
                        item.SendSignal(((float)Math.Tan(value)).ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    }
                    break;
                case FunctionType.Asin:
                    //asin is only defined in the range [-1,1]
                    if (value >= -1.0f && value <= 1.0f)
                    {
                        float angle = (float)Math.Asin(value);
                        if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                        item.SendSignal(angle.ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    }
                    break;
                case FunctionType.Acos:
                    //acos is only defined in the range [-1,1]
                    if (value >= -1.0f && value <= 1.0f)
                    {
                        float angle = (float)Math.Acos(value);
                        if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                        item.SendSignal(angle.ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    }
                    break;
                case FunctionType.Atan:                    
                    if (connection.Name == "signal_in_x")
                    {
                        timeSinceReceived[0] = 0.0f;
                        float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[0]);
                    }
                    else if (connection.Name == "signal_in_y")
                    {
                        timeSinceReceived[1] = 0.0f;
                        float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[1]);
                    }
                    else
                    {
                        float angle = (float)Math.Atan(value);
                        if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                        item.SendSignal(angle.ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    }
                    break;
                default:
                    throw new NotImplementedException($"Function {Function} has not been implemented.");
            }
        }
    }
}
