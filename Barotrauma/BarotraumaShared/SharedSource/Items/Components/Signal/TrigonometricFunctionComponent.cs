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

        private readonly float[] receivedSignal = new float[2];
        private readonly float[] timeSinceReceived = new float[2];

        protected Character signalSender;

        [Serialize(FunctionType.Sin, IsPropertySaveable.No, description: "Which kind of function to run the input through.", alwaysUseInstanceValues: true)]
        public FunctionType Function
        {
            get; set;
        }


        [InGameEditable, Serialize(false, IsPropertySaveable.Yes, description: "If set to true, the trigonometric function uses radians instead of degrees.", alwaysUseInstanceValues: true)]
        public bool UseRadians
        {
            get; set;
        }


        public TrigonometricFunctionComponent(Item item, ContentXElement element)
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
                    item.SendSignal(new Signal(angle.ToString("G", CultureInfo.InvariantCulture), sender: signalSender), "signal_out");
                }
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
            bool sendOutputImmediately = true;
            signalSender = signal.sender;
            switch (Function)
            {
                case FunctionType.Sin:
                    if (!UseRadians) { value = MathHelper.ToRadians(value); }
                    value = MathF.Sin(value);
                    break;
                case FunctionType.Cos:
                    if (!UseRadians) { value = MathHelper.ToRadians(value); }
                    value = MathF.Cos(value);
                    break;
                case FunctionType.Tan:
                    if (!UseRadians) { value = MathHelper.ToRadians(value); }
                    //tan is undefined if the value is (π / 2) + πk, where k is any integer
                    if (!MathUtils.NearlyEqual(value % MathHelper.Pi, MathHelper.PiOver2))
                    {
                        value = MathF.Tan(value);
                    }
                    break;
                case FunctionType.Asin:
                    //asin is only defined in the range [-1,1]
                    if (value >= -1.0f && value <= 1.0f)
                    {
                        float angle = MathF.Asin(value);
                        if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                        value = angle;
                    }
                    break;
                case FunctionType.Acos:
                    //acos is only defined in the range [-1,1]
                    if (value >= -1.0f && value <= 1.0f)
                    {
                        float angle = MathF.Acos(value);
                        if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                        value = angle;
                    }
                    break;
                case FunctionType.Atan:                    
                    if (connection.Name == "signal_in_x")
                    {
                        timeSinceReceived[0] = 0.0f;
                        float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[0]);
                        sendOutputImmediately = false;
                    }
                    else if (connection.Name == "signal_in_y")
                    {
                        timeSinceReceived[1] = 0.0f;
                        float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[1]);
                        sendOutputImmediately = false;
                    }
                    else
                    {
                        float angle = MathF.Atan(value);
                        if (!UseRadians) { angle = MathHelper.ToDegrees(angle); }
                        value = angle;
                    }
                    break;
                default:
                    throw new NotImplementedException($"Function {Function} has not been implemented.");
            }
            if (sendOutputImmediately)
            {
                signal.value = value.ToString("G", CultureInfo.InvariantCulture);
                item.SendSignal(signal, "signal_out");
            }
        }
    }
}
