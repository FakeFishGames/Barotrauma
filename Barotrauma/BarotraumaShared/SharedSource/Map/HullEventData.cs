using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.MapCreatures.Behavior;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Hull
    {
        [Flags]
        public enum EventType
        {
            Status = 0,
            Decal = 1,
            BackgroundSections = 2,
            BallastFlora = 3,
            
            MinValue = 0,
            MaxValue = 3
        }

        public interface IEventData : NetEntityEvent.IData
        {
            public EventType EventType { get; }
        }

        private readonly struct StatusEventData : IEventData
        {
            public EventType EventType => EventType.Status;
        }
        
        private readonly struct DecalEventData : IEventData
        {
            public EventType EventType => EventType.Decal;
            public readonly Decal Decal;
            
            public DecalEventData(Decal decal)
            {
                Decal = decal;
            }
        }
        
        private readonly struct BackgroundSectionsEventData : IEventData
        {
            public EventType EventType => EventType.BackgroundSections;
            public readonly int SectorStartIndex;
            
            public BackgroundSectionsEventData(int sectorStartIndex)
            {
                SectorStartIndex = sectorStartIndex;
            }
        }
        
        public readonly struct BallastFloraEventData : IEventData
        {
            public EventType EventType => EventType.BallastFlora;
            public readonly BallastFloraBehavior Behavior;
            public readonly BallastFloraBehavior.IEventData SubEventData;

            public BallastFloraEventData(BallastFloraBehavior behavior, BallastFloraBehavior.IEventData subEventData)
            {
                Behavior = behavior;
                SubEventData = subEventData;
            }
        }
    }
}
