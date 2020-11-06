using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
using System.Xml.Linq;

namespace Barotrauma.MapCreatures.Behavior
{
    partial class BallastFloraBehavior
    {
        partial void LoadPrefab(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "branchsprite":
                    case "hiddenflowersprite":
                        break;
                    case "flowersprite":
                        flowerVariants++;
                        break;
                    case "leafsprite":
                        leafVariants++;
                        break;
                    case "targets":
                        LoadTargets(subElement);
                        break;
                }
            }
        }

        
        public void ServerWriteSpawn(IWriteMessage msg)
        {
            msg.Write(Prefab.Identifier);
            msg.Write(Offset.X);
            msg.Write(Offset.Y);
        }
        
        public void ServerWriteBranchGrowth(IWriteMessage msg, BallastFloraBranch branch, int parentId = -1)
        {
            var (x, y) = branch.Position;
            msg.Write(parentId);
            msg.Write((int)branch.ID);
            msg.WriteRangedInteger((byte) branch.Type, 0b0000, 0b1111);
            msg.WriteRangedInteger((byte) branch.Sides, 0b0000, 0b1111);
            msg.WriteRangedInteger(branch.FlowerConfig.Serialize(), 0, 0xFFF);
            msg.WriteRangedInteger(branch.LeafConfig.Serialize(), 0, 0xFFF);
            msg.Write((ushort) branch.MaxHealth);
            msg.Write((int) (x / VineTile.Size));
            msg.Write((int) (y / VineTile.Size));
        }
        
        public void ServerWriteBranchDamage(IWriteMessage msg, BallastFloraBranch branch, float damage)
        {
            msg.Write((int)branch.ID);
            msg.Write(damage);
            msg.Write(branch.Health);
        }
        
        public void ServerWriteInfect(IWriteMessage msg, UInt16 itemID, bool infect)
        {
            msg.Write(itemID);
            msg.Write(infect);
        }

        public void ServerWriteBranchRemove(IWriteMessage msg, BallastFloraBranch branch)
        {
            msg.Write(branch.ID);
        }

        public void SendNetworkMessage(params object[] extraData)
        {
            GameMain.Server.CreateEntityEvent(Parent, extraData);
        }
    }
}