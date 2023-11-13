#nullable enable


namespace Barotrauma
{
    class CheckVisibilityAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the entity to do the visibility check from.")]
        public Identifier EntityTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the entity to do the visibility check to.")]
        public Identifier TargetTag { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Does the entity need to be facing the target? Only valid if the entity is a character.")]
        public bool CheckFacing { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the entity who saw the target when the check succeeds.")]
        public Identifier ApplyTagToEntity { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the entity that was seen when the check succeeds.")]
        public Identifier ApplyTagToTarget { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "If both the seeing entity and the target are the same, does it count as success?")]
        public bool AllowSameEntity { get; set; }

        public CheckVisibilityAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        {
        }

        protected override bool? DetermineSuccess()
        {
            foreach (var entity in ParentEvent.GetTargets(EntityTag))
            {
                foreach (var target in ParentEvent.GetTargets(TargetTag))
                {
                    if (!AllowSameEntity && entity == target) { continue; }
                    if (Character.IsTargetVisible(target, entity, CheckFacing)) 
                    { 
                        if (!ApplyTagToEntity.IsEmpty)
                        {
                            ParentEvent.AddTarget(ApplyTagToEntity, entity);
                        }
                        if (!ApplyTagToTarget.IsEmpty)
                        {
                            ParentEvent.AddTarget(ApplyTagToTarget, target);
                        }
                        return true; 
                    }
                }
            }

            return false;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(HasBeenDetermined())} {nameof(CheckVisibilityAction)} -> (TargetTags: {EntityTag.ColorizeObject()}, {TargetTag.ColorizeObject()})";
        }
    }
}