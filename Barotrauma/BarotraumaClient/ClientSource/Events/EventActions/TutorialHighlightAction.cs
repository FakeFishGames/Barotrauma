using Microsoft.Xna.Framework;

namespace Barotrauma;

partial class TutorialHighlightAction : EventAction
{
    private static readonly Color highlightColor = Color.Orange;

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
            SetItemHighlight(i);
        }
        else if (entity is Structure s)
        {
            SetStructureHighlight(s);
        }
        else if (entity is Character c)
        {
            SetCharacterHighlight(c);
        }
    }

    private void SetItemHighlight(Item item)
    {
        if (item.ExternalHighlight == State) { return; } 
        item.HighlightColor = State ? highlightColor : null;
        item.ExternalHighlight = State;
    }

    private void SetStructureHighlight(Structure structure)
    {
        structure.SpriteColor = State ? highlightColor : Color.White;
        structure.ExternalHighlight = State;
    }

    private void SetCharacterHighlight(Character character)
    {
        character.ExternalHighlight = State;
    }
}