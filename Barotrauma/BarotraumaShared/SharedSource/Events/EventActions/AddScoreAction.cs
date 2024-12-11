namespace Barotrauma
{
    /// <summary>
    /// Modifies the win score of a team in the PvP mode.
    /// </summary>
    class AddScoreAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of a target (character) whose team the score should be given to.")]
        public Identifier TargetTag { get; set; }

        [Serialize(CharacterTeamType.None, IsPropertySaveable.Yes, description: $"Which team's score to add to? Ignored if {nameof(TargetTag)} is set.")]
        public CharacterTeamType Team { get; set; }

        [Serialize(1, IsPropertySaveable.Yes, description: "How much to add to the score? Can also be negative.")]
        public int Amount { get; set; }

        public AddScoreAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (Amount == 0)
            {
                DebugConsole.ThrowError($"Error in {nameof(AddScoreAction)}, event {parentEvent.Prefab.Identifier}: score set to 0, the action will do nothing.", contentPackage: element.ContentPackage);
            }
            if (TargetTag.IsEmpty && Team == CharacterTeamType.None)
            {
                DebugConsole.ThrowError($"Error in {nameof(AddScoreAction)}, event {parentEvent.Prefab.Identifier}: neither {nameof(Team)} or {nameof(TargetTag)} is set.", contentPackage: element.ContentPackage);
            }
        }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            CharacterTeamType targetTeam = CharacterTeamType.None;
            if (TargetTag.IsEmpty)
            {
                targetTeam = Team;
            }
            else
            {
                foreach (var target in ParentEvent.GetTargets(TargetTag))
                {
                    if (target is Character character)
                    {
                        targetTeam = character.TeamID;
                        break;
                    }
                }               
            }
            if (targetTeam == CharacterTeamType.None) { return; }

#if SERVER
            if (GameMain.GameSession?.Missions is { } missions)
            {
                foreach (var mission in missions)
                { 
                    if (mission is CombatMission combatMission)
                    {
                        combatMission.AddToScore(targetTeam, Amount);
                    }
                }                
            }
#endif
            isFinished = true;
        }

        public override string ToDebugString()
        {
            string target = TargetTag.IsEmpty ? $"team: {Team.ColorizeObject()}" : $"target: {TargetTag}";
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(AddScoreAction)} -> ({target}, amount: {Amount.ColorizeObject()})";
        }
    }
}