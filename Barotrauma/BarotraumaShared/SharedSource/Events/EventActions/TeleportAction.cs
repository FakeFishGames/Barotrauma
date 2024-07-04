namespace Barotrauma;

/// <summary>
/// Teleports a specific entity to a specific spawn point.
/// </summary>
class TeleportAction : EventAction
{
    public enum TeleportPosition { MainSub, Outpost }

    [Serialize(TeleportPosition.MainSub, IsPropertySaveable.Yes, description: "Should the entity be teleported to the main submarine or the outpost?")]
    public TeleportPosition Position { get; set; }

    [Serialize(SpawnType.Human, IsPropertySaveable.Yes, description: "The type of the spawnpoint to teleport the character to.")]
    public SpawnType SpawnType { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Optional tag of the spawnpoint.")]
    public string SpawnPointTag { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the target(s) to teleport.")]
    public Identifier TargetTag { get; set; }

    private bool isFinished;

    public TeleportAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }
        Submarine sub = Position switch
        {
            TeleportPosition.MainSub => Submarine.MainSub,
            TeleportPosition.Outpost => GameMain.GameSession?.Level?.StartOutpost,
            _ => null
        };
        if (WayPoint.GetRandom(spawnType: SpawnType, sub: sub, spawnPointTag: SpawnPointTag) is WayPoint wp)
        {
            foreach (var target in ParentEvent.GetTargets(TargetTag))
            {
                if (target is Character c)
                {
                    c.TeleportTo(wp.WorldPosition);
                }
            }
        }
        isFinished = true;
    }

    public override bool IsFinished(ref string goToLabel) => isFinished;

    public override void Reset() => isFinished = false;
}