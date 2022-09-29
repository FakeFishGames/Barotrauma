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

    [Serialize(1, IsPropertySaveable.Yes)]
    public int MinAmount { get; set; }

    public CheckConnectionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

    protected override bool? DetermineSuccess()
    {
        int amount = 0;
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
                    amount += connection.Wires.Count();
                    if (amount >= MinAmount) { return true; }
                    continue;
                }
                foreach (var wire in connection.Wires)
                {
                    if (wire.OtherConnection(connection) is not Connection otherConnection) { continue; }
                    if (!ConnectedItemTag.IsEmpty && !IsCorrectConnection(otherConnection, OtherConnectionName)) { continue; }
                    if (!ConnectedItemTag.IsEmpty && !IsCorrectItem()) { continue; }
                    amount++;
                    if (amount >= MinAmount) { return true; }
                    bool IsCorrectItem() => connectTargets.Contains(otherConnection.Item);
                }

                bool IsCorrectConnection(Connection connection, Identifier id) => connection.Name.ToIdentifier() == id;
            }
        }
        return false;
    }
}