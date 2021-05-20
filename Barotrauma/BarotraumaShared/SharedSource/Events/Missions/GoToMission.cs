using Barotrauma.Networking;

namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        public GoToMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            State = 1;
        }

#if CLIENT
        public override void ClientReadInitial(IReadMessage msg)
        {
        }
#elif SERVER

        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
        }
#endif
    }
}
