using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class ScanMission : Mission
    {
        public override IEnumerable<Entity> HudIconTargets
        {
            get
            {
                if (State == 0)
                {
                    return scanTargets.Where(kvp => !kvp.Value).Select(kvp => kvp.Key);
                }
                else
                {
                    return Enumerable.Empty<Entity>();
                }
            }
        }

        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
            startingItems.Clear();
            ushort itemCount = msg.ReadUInt16();
            for (int i = 0; i < itemCount; i++)
            {
                startingItems.Add(Item.ReadSpawnData(msg));
            }
            if (startingItems.Contains(null))
            {
                throw new Exception($"Error in ScanMission.ClientReadInitial: item list contains null (mission: {Prefab.Identifier})");
            }
            if (startingItems.Count != itemCount)
            {
                throw new Exception($"Error in ScanMission.ClientReadInitial: item count does not match the server count ({itemCount} != {startingItems.Count}, mission: {Prefab.Identifier})");
            }
            scanners.Clear();
            GetScanners();
            ClientReadScanTargetStatus(msg);
        }

        public override void ClientRead(IReadMessage msg)
        {
            base.ClientRead(msg);
            ClientReadScanTargetStatus(msg);
        }

        private void ClientReadScanTargetStatus(IReadMessage msg)
        {
            scanTargets.Clear();
            byte targetsToScan = msg.ReadByte();
            for (int i = 0; i < targetsToScan; i++)
            {
                ushort id = msg.ReadUInt16();
                bool scanned = msg.ReadBoolean();
                Entity entity = Entity.FindEntityByID(id);
                if (!(entity is WayPoint wayPoint))
                {
                    string errorMsg = $"Failed to find a waypoint in ScanMission.ClientReadScanTargetStatus. Entity {id} was {(entity?.ToString() ?? null)}";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("ScanMission.ClientReadScanTargetStatus", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                }
                else
                {
                    scanTargets.Add(wayPoint, scanned);
                }
            }
        }
    }
}