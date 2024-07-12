#nullable enable
using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Check whether a specific entity is visible from the perspective of another entity.
    /// </summary>
    class CheckVisibilityAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the entity to do the visibility check from.")]
        public Identifier EntityTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Entities that also have this tag are excluded.")]
        public Identifier ExcludedEntityTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the entity to do the visibility check to.")]
        public Identifier TargetTag { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Does the entity need to be facing the target? Only valid if the entity is a character.")]
        public bool CheckFacing { get; set; }

        [Serialize(1000.0f, IsPropertySaveable.Yes, description: "Maximum distance between the targets.")]
        public float MaxDistance { get; set; }

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
                if (!ExcludedEntityTag.IsEmpty)
                {
                    if (ParentEvent.GetTargets(ExcludedEntityTag).Contains(entity)) { continue; }
                }

                foreach (var target in ParentEvent.GetTargets(TargetTag))
                {
                    if (!AllowSameEntity && entity == target) { continue; }
                    if (Vector2.DistanceSquared(target.WorldPosition, entity.WorldPosition) > MaxDistance * MaxDistance) { continue; }
                    if (Character.IsTargetVisible(target, entity, seeThroughWindows: true, CheckFacing)) 
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