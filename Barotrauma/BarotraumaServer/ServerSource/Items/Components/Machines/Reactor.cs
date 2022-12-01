using Barotrauma.Networking;
using System;

namespace Barotrauma.Items.Components
{
    partial class Reactor
    {
        const float NetworkUpdateIntervalLow = 10.0f;

        private Client blameOnBroken;

        private float? nextServerLogWriteTime;
        private float lastServerLogWriteTime;

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            bool autoTemp = msg.ReadBoolean();
            bool powerOn = msg.ReadBoolean();
            float fissionRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            float turbineOutput = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            float temperatureBoostAmount = msg.ReadRangedSingle(-TemperatureBoostAmount, TemperatureBoostAmount, 8);

            if (!item.CanClientAccess(c)) { return; }

            IsActive = true;

            if (!autoTemp && AutoTemp) { blameOnBroken = c; }
            if (turbineOutput < TargetTurbineOutput) { blameOnBroken = c; }
            if (fissionRate > TargetFissionRate) { blameOnBroken = c; }
            if (!_powerOn && powerOn) { blameOnBroken = c; }

            AutoTemp = autoTemp;
            _powerOn = powerOn;
            TargetFissionRate = fissionRate;
            TargetTurbineOutput = turbineOutput;
            if (AllowTemperatureBoost) { temperatureBoost = temperatureBoostAmount; }

            LastUser = c.Character;
            if (nextServerLogWriteTime == null)
            {
                nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
            }

            //need to create a server event to notify all clients of the changed state
            unsentChanges = true;
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(autoTemp);
            msg.WriteBoolean(_powerOn);
            msg.WriteRangedSingle(temperature, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(TargetFissionRate, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(TargetTurbineOutput, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(degreeOfSuccess, 0.0f, 1.0f, 8);
            msg.WriteRangedSingle(temperatureBoost, -TemperatureBoostAmount, TemperatureBoostAmount, 8);
        }
    }
}
