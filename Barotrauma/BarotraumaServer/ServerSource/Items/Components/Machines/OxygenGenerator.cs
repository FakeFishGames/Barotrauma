using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    internal partial class OxygenGenerator : IServerSerializable, IClientSerializable
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteRangedInteger(MathUtils.RoundToInt(generationRatio * 10), 0, 10);
        }
        
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            float newGenerationRatio = msg.ReadRangedInteger(0, 10) / 10f;

            if (item.CanClientAccess(c))
            {
                GenerationRatio = newGenerationRatio;
                GameServer.Log(GameServer.CharacterLogName(c.Character) + " set the oxygen generation amount of " + item.Name + " to " + MathUtils.RoundToInt(generationRatio * 100f) + " %", ServerLog.MessageType.ItemInteraction);
            }

            item.CreateServerEvent(this);
        }
    }
}