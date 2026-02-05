using Barotrauma.Extensions;
using System;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    /// <summary>
    /// Makes the item inherit the condition from a linked wall or multiple - or in other words, makes it essentially treat the health of the wall as its own health.
    /// The wall section with the most damage determines the condition (i.e. the item will be fully broken if there's at least one fully broken wall section).
    /// </summary>
    class InheritConditionFromLinkedWall(Item item, ContentXElement element) : ItemComponent(item, element)
    {
        private readonly List<Structure> linkedWalls = [];

        public override void OnMapLoaded()
        {
            foreach (var linkedTo in item.linkedTo)
            {
                if (linkedTo is Structure structure && 
                    structure.HasBody)
                {
                    linkedWalls.Add(structure);
                    structure.OnHealthChanged += (_, _) => UpdateCondition();
                }
            }
            if (linkedWalls.None())
            {
                DebugConsole.AddWarning($"The item {item.Name} ({item.Prefab.Identifier}) is not linked to any walls with a physics body. The {nameof(InheritConditionFromLinkedWall)} component will do nothing.");
            }
            
        }

        private void UpdateCondition()
        {
            float lowestHealthPercent = 1.0f;
            foreach (var wall in linkedWalls)
            {
                foreach (var section in wall.Sections)
                {
                    lowestHealthPercent = Math.Min(lowestHealthPercent, 1.0f - section.damage / wall.MaxHealth);
                }
            }
            item.Condition = item.MaxCondition * lowestHealthPercent;
        }
    }
}
