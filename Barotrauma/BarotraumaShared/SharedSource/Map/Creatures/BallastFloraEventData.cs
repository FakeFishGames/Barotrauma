using Barotrauma.Networking;

namespace Barotrauma.MapCreatures.Behavior
{
    internal partial class BallastFloraBehavior
    {
        public interface IEventData : NetEntityEvent.IData
        {
            public NetworkHeader NetworkHeader { get; }
        }

        public readonly struct SpawnEventData : IEventData
        {
            public NetworkHeader NetworkHeader => NetworkHeader.Spawn;
        }

        private readonly struct KillEventData : IEventData
        {
            public NetworkHeader NetworkHeader => NetworkHeader.Kill;
        }

        private readonly struct BranchCreateEventData : IEventData
        {
            public NetworkHeader NetworkHeader => NetworkHeader.BranchCreate;
            public readonly BallastFloraBranch NewBranch;
            public readonly BallastFloraBranch Parent;
            
            public BranchCreateEventData(BallastFloraBranch newBranch, BallastFloraBranch parent)
            {
                NewBranch = newBranch;
                Parent = parent;
            }
        }
        
        private readonly struct BranchRemoveEventData : IEventData
        {
            public NetworkHeader NetworkHeader => NetworkHeader.BranchRemove;
            public readonly BallastFloraBranch Branch;
            
            public BranchRemoveEventData(BallastFloraBranch branch)
            {
                Branch = branch;
            }
        }
        
        private readonly struct BranchDamageEventData : IEventData
        {
            public NetworkHeader NetworkHeader => NetworkHeader.BranchDamage;
            public readonly BallastFloraBranch Branch;

            public BranchDamageEventData(BallastFloraBranch branch)
            {
                Branch = branch;
            }
        }

        private readonly struct InfectEventData : IEventData
        {
            public enum InfectState { Yes, No }
            
            public NetworkHeader NetworkHeader => NetworkHeader.Infect;
            public readonly Item Item;
            public readonly InfectState Infect;
            public readonly BallastFloraBranch Infector;
            
            public InfectEventData(Item item, InfectState infect, BallastFloraBranch infector)
            {
                Item = item;
                Infect = infect;
                Infector = infector;
            }
        }
    }
}
