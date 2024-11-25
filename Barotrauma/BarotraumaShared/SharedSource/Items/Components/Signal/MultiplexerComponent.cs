using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components;

/// <summary>
/// A component with multiple inputs and one output. Can be used to choose which input the component passes signals to the output from.
/// </summary>
sealed class MultiplexerComponent : ConnectionSelectorComponent
{
    public MultiplexerComponent(Item item, ContentXElement element)
        : base(item, element)
    {
    }

    protected override string InputNameSetConnection => "set_input";

    protected override string InputNameMoveInput => "move_input";

    public override void OnItemLoaded()
    {
        base.OnItemLoaded();
        IsActive = item.Connections != null && item.Connections.Any(c => c.Name == "selected_input_out");
    }

    public override void Update(float deltaTime, Camera cam)
    {
        item.SendSignal(selectedConnectionIndexStr, "selected_input_out");
    }
    public override void ReceiveSignal(Signal signal, Connection connection)
    {
        if (connection.Name.StartsWith("signal_in"))
        {
            if (connection.Name == selectedConnectionName)
            {
                item.SendSignal(signal, "signal_out");
            }
        }
        else
        {
            base.ReceiveSignal(signal, connection);
        }
    }

    protected override string GetConnectionName(int connectionIndex)
    {
        return "signal_in" + connectionIndex;
    }

    protected override IEnumerable<Connection> GetConnections()
    {
        if (item.GetComponent<ConnectionPanel>() is { } connectionPanel)
        {
            return connectionPanel.Connections.Where(c => !c.IsOutput && c.Name.StartsWith("signal_in"));
        }
        return Enumerable.Empty<Connection>();
    }
}
