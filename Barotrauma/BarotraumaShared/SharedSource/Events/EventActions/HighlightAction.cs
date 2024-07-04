#nullable enable
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma;

/// <summary>
/// Highlights a specific entity.
/// </summary>
partial class HighlightAction : EventAction
{
    private static readonly Color highlightColor = Color.Orange;

    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the entity to highlight.")]
    public Identifier TargetTag { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Only the player controlling this character will see the highlight. If empty, all players will see it.")]
    public Identifier TargetCharacter { get; set; }

    [Serialize(true, IsPropertySaveable.Yes, description: "Should the highlight be turned on or off?")]
    public bool State { get; set; }

    private bool isFinished;

    public HighlightAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
    {
    }

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }
        var targetCharacters = TargetCharacter.IsEmpty ? null : ParentEvent.GetTargets(TargetCharacter).OfType<Character>();
        foreach (var target in ParentEvent.GetTargets(TargetTag))
        {
            SetHighlightProjSpecific(target, targetCharacters);
        }
        isFinished = true;
    }

    partial void SetHighlightProjSpecific(Entity entity, IEnumerable<Character>? targetCharacters);

    public override bool IsFinished(ref string goToLabel) => isFinished;

    public override void Reset() => isFinished = false;
}