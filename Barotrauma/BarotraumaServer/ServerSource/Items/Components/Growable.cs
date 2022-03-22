using Barotrauma.Networking;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    internal partial class Growable
    {
        private const int serverHealthUpdateDelay = 10;
        private int serverHealthUpdateTimer;

        partial void LoadVines(XElement element)
        {
            foreach (XElement subElement in element.Elements())
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

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.WriteRangedSingle(Health, 0f, (float) MaxHealth, 8);
            if (extraData != null && extraData.Length >= 3 && extraData[2] is int offset)
            {
                int amountToSend = Math.Min(Vines.Count - offset, VineChunkSize);
                msg.WriteRangedInteger(offset, -1, MaximumVines);
                msg.WriteRangedInteger(amountToSend, 0, VineChunkSize);
                for (int i = offset; i < offset + amountToSend; i++)
                {
                    VineTile vine = Vines[i];
                    var (x, y) = vine.Position;
                    msg.WriteRangedInteger((byte) vine.Type, 0b0000, 0b1111);
                    msg.WriteRangedInteger(vine.FlowerConfig.Serialize(), 0, 0xFFF);
                    msg.WriteRangedInteger(vine.LeafConfig.Serialize(), 0, 0xFFF);
                    msg.Write((byte) (x / VineTile.Size));
                    msg.Write((byte) (y / VineTile.Size));
                }
            }
            else
            {
                msg.WriteRangedInteger(-1, -1, MaximumVines);
            }
        }
    }
}