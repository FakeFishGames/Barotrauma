using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components;

/// <summary>
/// A component with one input and multiple outputs. Can be used to choose which output the signal should be passed to.
/// </summary>
sealed class DemultiplexerComponent : ConnectionSelectorComponent
{
    public DemultiplexerComponent(Item item, ContentXElement element)
        : base(item, element)
    {

    }

    protected override string InputNameSetConnection => "set_output";

    protected override string InputNameMoveInput => "move_output";

    public override void OnItemLoaded()
    {
        base.OnItemLoaded();
        IsActive = item.Connections != null && item.Connections.Any(c => c.Name == "selected_output_out");
    }

    public override void ReceiveSignal(Signal signal, Connection connection)
    {
        if (connection.Name == "signal_in")
        {
            item.SendSignal(signal, selectedConnectionName);
        }
        else
        {
            base.ReceiveSignal(signal, connection);
        }
    }

    public override void Update(float deltaTime, Camera cam)
    {
        item.SendSignal(selectedConnectionIndexStr, "selected_output_out");
    }

    protected override string GetConnectionName(int connectionIndex)
    {
        return "signal_out" + connectionIndex;
    }

    protected override IEnumerable<Connection> GetConnections()
    {
        if (item.GetComponent<ConnectionPanel>() is { } connectionPanel)
        {
            return connectionPanel.Connections.Where(c => c.IsOutput && c.Name.StartsWith("signal_out"));
        }
        return Enumerable.Empty<Connection>();
    }
}
