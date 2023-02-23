using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class SonarTransducer : Powered
    {
        const float SendSignalInterval = 0.5f;

        private float sendSignalTimer;

        public Sonar ConnectedSonar;

        public SonarTransducer(Item item, ContentXElement element) : base(item, element)
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

        public override float GetCurrentPowerConsumption(Connection connection = null)
        {
            if (connection != powerIn || !IsActive)
            {
                return 0;
            }

            return ConnectedSonar?.CurrentMode == Sonar.Mode.Active ? PowerConsumption : PowerConsumption * ConnectedSonar.PassivePowerConsumptionMultiplier;
        }
    }
}
