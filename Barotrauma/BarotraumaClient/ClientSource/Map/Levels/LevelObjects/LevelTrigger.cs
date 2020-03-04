using Barotrauma.Networking;

namespace Barotrauma
{
    partial class LevelTrigger
    {
        public void ClientRead(IReadMessage msg)
        {
            if (ForceFluctuationStrength > 0.0f)
            {
                currentForceFluctuation = msg.ReadRangedSingle(0.0f, 1.0f, 8);
            }
            if (stayTriggeredDelay > 0.0f)
            {
                triggeredTimer = msg.ReadRangedSingle(0.0f, stayTriggeredDelay, 16);
            }
        }
    }
}
