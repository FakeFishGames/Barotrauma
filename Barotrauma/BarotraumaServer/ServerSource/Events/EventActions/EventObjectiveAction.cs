namespace Barotrauma
{
    partial class EventObjectiveAction : EventAction
    {
        partial void UpdateProjSpecific()
        {
            if (GameMain.Server == null) { return; }
            EventManager.NetEventObjective objective = new EventManager.NetEventObjective(
                Type, 
                Identifier, 
                ObjectiveTag, 
                TextTag,
                ParentObjectiveId,
                CanBeCompleted);

            if (TargetTag.IsEmpty)
            {
                foreach (var client in GameMain.Server.ConnectedClients)
                {
                    if (client.Character == null) { continue; }
                    EventManager.ServerWriteObjective(client, objective);
                }
            }
            else
            {
                foreach (var target in ParentEvent.GetTargets(TargetTag))
                {
                    if (target is not Character character) { continue; }
                    var ownerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                    if (ownerClient == null) { continue; }
                    EventManager.ServerWriteObjective(ownerClient, objective);
                }
            }
        }
    }
}