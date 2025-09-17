using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class WifiComponent
    {
        private readonly int[] networkReceivedChannelMemory = new int[ChannelMemorySize];

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            SharedEventWrite(msg);
        }

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            int newChannel = msg.ReadRangedInteger(MinChannel, MaxChannel);
            for (int i = 0; i < ChannelMemorySize; i++)
            {
                networkReceivedChannelMemory[i] = msg.ReadRangedInteger(MinChannel, MaxChannel);
            }

            if (item.CanClientAccess(c))
            {
                Channel = newChannel;
                for (int i = 0; i < ChannelMemorySize; i++)
                {
                    channelMemory[i] = networkReceivedChannelMemory[i];
                }
            }

            // Create an event to notify other clients about the changes
            item.CreateServerEvent(this);
        }
    }
}
