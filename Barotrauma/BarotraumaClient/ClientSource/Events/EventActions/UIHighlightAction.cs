using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma;

partial class UIHighlightAction : EventAction
{
    private static readonly Color highlightColor = Color.Orange;

    partial void UpdateProjSpecific()
    {
        bool useCircularFlash = false;
        if (Id != ElementId.None)
        {
            FindAndFlashComponents(c => Equals(Id, c.UserData));
        }
        else if (!EntityIdentifier.IsEmpty)
        {
            FindAndFlashComponents(c =>
                c.UserData is MapEntityPrefab mep && mep.Identifier == EntityIdentifier || c.UserData is MapEntity me && me.Prefab.Identifier == EntityIdentifier);
        }
        else if (!OrderIdentifier.IsEmpty)
        {
            useCircularFlash = true;
            bool foundMinimapNode = false;
            if (!OrderTargetTag.IsEmpty)
            {
                foundMinimapNode = FindAndFlashComponents(c =>
                    c.UserData is CrewManager.MinimapNodeData nodeData && nodeData.Order is Order order &&
                    order.Identifier == OrderIdentifier && order.Option == OrderOption && order.TargetEntity is Item item && item.HasTag(OrderTargetTag));
            }
            if (!foundMinimapNode)
            {
                FindAndFlashComponents(c => c.UserData is Order order && order.Identifier == OrderIdentifier && order.Option == OrderOption,
                    c => c.UserData is Order order && order.Identifier == OrderIdentifier,
                    c => Equals(OrderCategory, c.UserData));
            }
        }

        bool FindAndFlashComponents(params Func<GUIComponent, bool>[] predicates)
        {
            foreach (var predicate in predicates)
            {
                if (HighlightMultiple)
                {
                    bool found = false;
                    foreach (var component in GUI.GetAdditions())
                    {
                        if (predicate(component))
                        {
                            Flash(component);
                            found = true;
                        }
                    };
                    return found;
                }
                else if (GUI.GetAdditions().FirstOrDefault(predicate) is GUIComponent component)
                {
                    Flash(component);
                    return true;
                }
            }
            return false;
        }

        void Flash(GUIComponent component)
        {
            if (component.FlashTimer <= 0.0f)
            {
                component.Flash(highlightColor, useCircularFlash: useCircularFlash);
                component.Bounce |= Bounce;
            }
        }
    }
}