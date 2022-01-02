using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class SonarTransducer : Powered
    {
        const float SendSignalInterval = 0.5f;

        private float sendSignalTimer;

        public Sonar ConnectedSonar;

        public SonarTransducer(Item item, XElement element) : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateOnActiveEffects(deltaTime);

            if (Voltage >= MinVoltage)
            {
                sendSignalTimer += deltaTime;
                if (sendSignalTimer > SendSignalInterval)
                {
                    item.SendSignal("0101101101101011010", "data_out");
                    sendSignalTimer = SendSignalInterval;
                }
            }
        }

        public override float ConnCurrConsumption(Connection conn = null)
        {
            if (conn != powerIn || !IsActive)
            {
                return 0;
            }

            return PowerConsumption * (ConnectedSonar?.CurrentMode == Sonar.Mode.Active ? 1.0f : Sonar.PassivePowerConsumption);
        }
    }
}
