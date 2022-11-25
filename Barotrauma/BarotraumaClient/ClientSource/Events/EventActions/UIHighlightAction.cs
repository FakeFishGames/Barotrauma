using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma;

partial class UIHighlightAction : EventAction
{
    private static readonly Color highlightColor = Color.Orange;

    partial void UpdateProjSpecific()
    {
        bool useCircularFlash = false;
        GUIComponent component = null;

        if (Id != ElementId.None)
        {
            component = GUI.GetAdditions().FirstOrDefault(c => Equals(Id, c.UserData));
        }
        else if (!EntityIdentifier.IsEmpty)
        {
            component = GUI.GetAdditions().FirstOrDefault(c => 
                c.UserData is MapEntityPrefab mep && mep.Identifier == EntityIdentifier || c.UserData is MapEntity me && me.Prefab.Identifier == EntityIdentifier);
        }
        else if (!OrderIdentifier.IsEmpty)
        {
            useCircularFlash = true;
            if (!OrderTargetTag.IsEmpty)
            {
                component =
                    GUI.GetAdditions().FirstOrDefault(c =>
                        c.UserData is CrewManager.MinimapNodeData nodeData && nodeData.Order is Order order &&
                        order.Identifier == OrderIdentifier && order.Option == OrderOption && order.TargetEntity is Item item && item.HasTag(OrderTargetTag));
            }
            component ??=
                GUI.GetAdditions().FirstOrDefault(c => c.UserData is Order order && order.Identifier == OrderIdentifier && order.Option == OrderOption) ??
                GUI.GetAdditions().FirstOrDefault(c => c.UserData is Order order && order.Identifier == OrderIdentifier) ?? 
                GUI.GetAdditions().FirstOrDefault(c => Equals(OrderCategory, c.UserData));
        }

        if (component != null && component.FlashTimer <= 0.0f)
        {
            component.Flash(highlightColor, useCircularFlash: useCircularFlash);
            component.Bounce |= Bounce;
        }
    }
}