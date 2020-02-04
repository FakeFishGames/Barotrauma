using Barotrauma.Networking;
using System;

namespace Barotrauma.Items.Components
{
    partial class Reactor
    {
        private Client blameOnBroken;

        private float? nextServerLogWriteTime;
        private float lastServerLogWriteTime;

        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            bool autoTemp = msg.ReadBoolean();
            bool powerOn = msg.ReadBoolean();
            float fissionRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            float turbineOutput = msg.ReadRangedSingle(0.0f, 100.0f, 8);

            if (!item.CanClientAccess(c)) return;

            IsActive = true;

            if (!autoTemp && AutoTemp) blameOnBroken = c;
            if (turbineOutput < targetTurbineOutput) blameOnBroken = c;
            if (fissionRate > targetFissionRate) blameOnBroken = c;
            if (!_powerOn && powerOn) blameOnBroken = c;

            AutoTemp = autoTemp;
            _powerOn = powerOn;
            targetFissionRate = fissionRate;
            targetTurbineOutput = turbineOutput;

            LastUser = c.Character;
            if (nextServerLogWriteTime == null)
            {
                nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
            }

            //need to create a server event to notify all clients of the changed state
            unsentChanges = true;
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(autoTemp);
            msg.Write(_powerOn);
            msg.WriteRangedSingle(temperature, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(targetFissionRate, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(targetTurbineOutput, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(degreeOfSuccess, 0.0f, 1.0f, 8);
        }
    }
}
