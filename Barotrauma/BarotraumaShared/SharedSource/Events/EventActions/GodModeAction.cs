namespace Barotrauma
{
    /// <summary>
    /// Makes a specific character invulnerable to damage and unable to die.
    /// </summary>
    class GodModeAction : EventAction
    {
        [Serialize(true, IsPropertySaveable.Yes, description: "Should the godmode be enabled or disabled?")]
        public bool Enabled { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the character's active afflictions be updated (e.g. applying visual effects of the afflictions)")]
        public bool UpdateAfflictions { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character whose godmode to enable/disable.")]
        public Identifier TargetTag { get; set; }

        public GodModeAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

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
            var targets = ParentEvent.GetTargets(TargetTag);
            foreach (var target in targets)
            {
                if (target != null && target is Character character)
                {
                    if (UpdateAfflictions)
                    {
                        character.CharacterHealth.Unkillable = Enabled;
                    }
                    else
                    {
                        character.GodMode = Enabled;
                    }
                }
            }
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(GodModeAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   (Enabled ? "Enable godmode" : "Disable godmode");
        }
    }
}