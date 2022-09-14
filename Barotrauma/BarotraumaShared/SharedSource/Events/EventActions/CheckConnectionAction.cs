using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Linq;

namespace Barotrauma;

class CheckConnectionAction : BinaryOptionAction
{
    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier ItemTag { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier ConnectionName { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier ConnectedItemTag { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier OtherConnectionName { get; set; }

    public CheckConnectionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

    protected override bool? DetermineSuccess()
    {
        var connectTargets = !ConnectedItemTag.IsEmpty ? ParentEvent.GetTargets(ConnectedItemTag) : Enumerable.Empty<Entity>();
        foreach (var target in ParentEvent.GetTargets(ItemTag))
        {
            if (target is not Item targetItem) { continue; }
            if (targetItem.GetComponent<ConnectionPanel>() is not ConnectionPanel panel) { continue; }
            if (panel.Connections == null || panel.Connections.None()) { continue; }
            foreach (var connection in panel.Connections)
            {
                if (!IsCorrectConnection(connection, ConnectionName)) { continue; }
                if (ConnectedItemTag.IsEmpty && OtherConnectionName.IsEmpty)
                {
                    if (connection.Wires.Any()) { return true; }
                    continue;
                }
                foreach (var wire in connection.Wires)
                {
                    if (wire.OtherConnection(connection) is not Connection otherConnection) { continue; }
                    if (ConnectedItemTag.IsEmpty)
                    {
                        if (IsCorrectConnection(otherConnection, OtherConnectionName)) { return true; }
                    }
                    else if (OtherConnectionName.IsEmpty)
                    {
                        if (IsCorrectItem()) { return true; }
                    }
                    else
                    {
                        if (!IsCorrectConnection(otherConnection, OtherConnectionName)) { continue; }
                        if (!IsCorrectItem()) { continue; }
                        return true;
                    }

                    bool IsCorrectItem() => connectTargets.Contains(otherConnection.Item);
                }

                bool IsCorrectConnection(Connection connection, Identifier id) => connection.Name.ToIdentifier() == id;
            }
        }
        return false;
    }
}