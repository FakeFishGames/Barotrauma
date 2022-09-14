using Microsoft.Xna.Framework;

namespace Barotrauma;

partial class TutorialHighlightAction : EventAction
{
    private static readonly Color highlightColor = Color.OrangeRed;

    partial void UpdateProjSpecific()
    {
        if (GameMain.GameSession?.GameMode is not TutorialMode) { return; }
        foreach (var target in ParentEvent.GetTargets(TargetTag))
        {
            SetHighlight(target);
        }
    }

    private void SetHighlight(Entity entity)
    {
        if (entity is Item i)
        {
            SetHighlight(i);
        }
        else if (entity is Structure s)
        {
            SetHighlight(s);
        }
        else if (entity is Character c)
        {
            SetHighlight(c);
        }
    }

    private void SetHighlight(Item item)
    {
        if (item.ExternalHighlight == State) { return; } 
        item.SpriteColor = (State) ? highlightColor : Color.White;
        item.ExternalHighlight = State;
    }

    private void SetHighlight(Structure structure)
    {
        structure.SpriteColor = (State) ? highlightColor : Color.White;
        structure.ExternalHighlight = State;
    }

    private void SetHighlight(Character character)
    {
        character.ExternalHighlight = State;
    }
}