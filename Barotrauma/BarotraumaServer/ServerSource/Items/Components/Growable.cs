using Barotrauma.Networking;
using System;

namespace Barotrauma.Items.Components
{
    public partial class Growable
    {
        private readonly struct EventData : IEventData
        {
            public readonly int Offset;

            /// <summary>
            /// Should the vines added by this event appear at full size immediately (see <see cref="GrowVinesImmediately"/>)?
            /// </summary>
            public readonly bool GrowImmediately;
            
            public EventData(int offset, bool growImmediately = false)
            {
                Offset = offset;
                GrowImmediately = growImmediately;
            }
        }
        
        private const float ServerUpdateDelay = 1.0f;
        private float serverUpdateTimer;

        partial void LoadVines(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "flowersprite":
                        flowerVariants++;
                        break;
                    case "leafsprite":
                        leafVariants++;
                        break;
                }
            }
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteRangedSingle(Health, 0f, MaxHealth, 8);
            
            if (TryExtractEventData(extraData, out EventData eventData))
            {
                int offset = eventData.Offset;
                int amountToSend = Math.Min(Vines.Count - offset, VineChunkSize);
                msg.WriteRangedInteger(offset, -1, MaximumVines);
                msg.WriteRangedInteger(amountToSend, 0, VineChunkSize);
                msg.WriteBoolean(eventData.GrowImmediately);
                for (int i = offset; i < offset + amountToSend; i++)
                {
                    VineTile vine = Vines[i];
                    var (x, y) = vine.Position;
                    msg.WriteRangedInteger((byte)vine.Type, 0b0000, 0b1111);
                    vine.FlowerConfig.WriteFoliageConfig(rotationMin: MinFlowerRotation, rotationMax: MaxFlowerRotation, msg);
                    vine.LeafConfig.WriteFoliageConfig(rotationMin: MinLeafRotation, rotationMax: MaxLeafRotation, msg);
                    msg.WriteByte((byte)(x / VineTile.Size));
                    msg.WriteByte((byte)(y / VineTile.Size));
                }
            }
            else
            {
                msg.WriteRangedInteger(-1, -1, MaximumVines);
            }
        }
    }
}