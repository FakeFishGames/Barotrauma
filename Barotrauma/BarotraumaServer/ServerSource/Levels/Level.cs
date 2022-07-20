using System;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class Level : Entity, IServerSerializable
    {
        public interface IEventData : NetEntityEvent.IData
        {
            public EventType EventType { get; }
        }

        public readonly struct SingleLevelWallEventData : IEventData
        {
            public EventType EventType => EventType.SingleDestructibleWall;
            public readonly DestructibleLevelWall Wall;
            
            public SingleLevelWallEventData(DestructibleLevelWall wall)
            {
                Wall = wall;
            }
        }

        public readonly struct GlobalLevelWallEventData : IEventData
        {
            public EventType EventType => EventType.GlobalDestructibleWall;
        }
        
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            if (!(extraData is IEventData eventData)) { throw new Exception($"Malformed level event: expected {nameof(Level)}.{nameof(IEventData)}"); }

            msg.Write((byte)eventData.EventType);
            switch (eventData)
            {
                case SingleLevelWallEventData { Wall: var destructibleWall }:
                    int index = ExtraWalls.IndexOf(destructibleWall);
                    msg.Write((ushort)(index == -1 ? ushort.MaxValue : index));
                    //write health using one byte
                    msg.Write((byte)MathHelper.Clamp((int)(MathUtils.InverseLerp(0.0f, destructibleWall.MaxHealth, destructibleWall.Damage) * 255.0f), 0, 255));
                    break;
                case GlobalLevelWallEventData _:
                    foreach (LevelWall levelWall in ExtraWalls)
                    {
                        if (levelWall.Body.BodyType == BodyType.Static) { continue; }
                        msg.Write(levelWall.Body.Position.X);
                        msg.Write(levelWall.Body.Position.Y);
                        msg.WriteRangedSingle(levelWall.MoveState, 0.0f, MathHelper.TwoPi, 16);                    
                    }
                    break;
                default:
                    throw new Exception($"Malformed level event: did not expect {eventData.GetType().Name}");
            }
        }
    }
}
