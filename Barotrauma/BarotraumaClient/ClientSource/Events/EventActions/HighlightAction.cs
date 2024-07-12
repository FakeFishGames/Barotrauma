#nullable enable
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma;

partial class HighlightAction : EventAction
{
    partial void SetHighlightProjSpecific(Entity entity, IEnumerable<Character>? targetCharacters)
    {
        if (targetCharacters != null && !targetCharacters.Contains(Character.Controlled))
        {
            return;
        }

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