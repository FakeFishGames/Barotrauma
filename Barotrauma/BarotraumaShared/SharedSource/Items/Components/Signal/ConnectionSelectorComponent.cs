using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components;

/// <summary>
/// Base class for signal components that can select between input/output connections (e.g. multiplexer and demultiplexer components)
/// </summary>
abstract class ConnectionSelectorComponent : ItemComponent
{
    protected int selectedConnectionIndex;
    protected string selectedConnectionIndexStr;
    protected string selectedConnectionName;

    private int connectionCount = -1;

    [InGameEditable,
        Serialize(0, IsPropertySaveable.Yes, description: "The index of the selected connection.", alwaysUseInstanceValues: true)]
    public int SelectedConnection
    {
        get { return selectedConnectionIndex; }
        set
        {
            selectedConnectionIndex = Math.Max(0, value);
            //don't clamp until we've determined how many connections the item has
            //(can't be done until the connection panel component has been loaded too)
            if (connectionCount > -1)
            {
                selectedConnectionIndex = Math.Min(selectedConnectionIndex, connectionCount - 1);
            }
            selectedConnectionName = GetConnectionName(selectedConnectionIndex);
            selectedConnectionIndexStr = selectedConnectionIndex.ToString();
        }
    }

    [InGameEditable,
        Serialize(true, IsPropertySaveable.Yes, description: "Should the selected connection go back to the first one when moving past the last one?", alwaysUseInstanceValues: true)]
    public bool WrapAround
    {
        get;
        set;
    }

    [InGameEditable,
        Serialize(true, IsPropertySaveable.Yes, description: "Should empty connections (connections with no wires in them) be skipped over when moving the selection?", alwaysUseInstanceValues: true)]
    public bool SkipEmptyConnections
    {
        get;
        set;
    }

    public ConnectionSelectorComponent(Item item, ContentXElement element)
        : base(item, element)
    {
    }

    protected abstract string GetConnectionName(int connectionIndex);

    /// <summary>
    /// Name of the input connection that sets the selected connection.
    /// </summary>
    protected abstract string InputNameSetConnection { get; }

    /// <summary>
    /// Name of the input connection that moves the selected connection.
    /// </summary>
    protected abstract string InputNameMoveInput { get; }

    protected abstract IEnumerable<Connection> GetConnections();

    public override void OnItemLoaded()
    {
        connectionCount = GetConnections().Count();        
    }

    public override void ReceiveSignal(Signal signal, Connection connection)
    {
        if (connection.Name == InputNameSetConnection)
        {
            if (int.TryParse(signal.value, out int newInput))
            {
                SelectedConnection = newInput;
            }

        }
        else if (connection.Name == InputNameMoveInput)
        {
            if (int.TryParse(signal.value, out int moveAmount))
            {
                if (SkipEmptyConnections)
                {
                    for (int i = 0; i < connectionCount; i++)
                    {
                        moveInput(moveAmount);
                        if (item.Connections.Any(c => 
                            c.Name == selectedConnectionName && 
                            (c.Wires.Any() || c.CircuitBoxConnections.Any())))
                        {
                            break;
                        }
                    }
                }
                else
                {
                    moveInput(moveAmount);
                }
            }
        }
        void moveInput(int moveAmount)
        {
            if (WrapAround)
            {
                SelectedConnection = MathUtils.PositiveModulo(selectedConnectionIndex + moveAmount, connectionCount);
            }
            else
            {
                SelectedConnection += moveAmount;
            }
        }
    }
}
