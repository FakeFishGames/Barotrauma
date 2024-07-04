#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    /// <summary>
    /// This class handles a couple things:
    /// - Figuring out which components should be moved when dragging a certain part of the UI.
    /// - Finding components, connectors and wires under cursor.
    /// - Determines whether the user is dragging something.
    /// </summary>
    internal sealed class CircuitBoxMouseDragSnapshotHandler
    {
        public IEnumerable<CircuitBoxNode> Nodes
        {
            get
            {
                var cb = circuitBoxUi.CircuitBox;

                foreach (var label in cb.Labels) { yield return label; }
                foreach (var component in cb.Components) { yield return component; }
                foreach (var node in cb.InputOutputNodes) { yield return node; }
            }
        }

        private IReadOnlyList<CircuitBoxWire> Wires => circuitBoxUi.CircuitBox.Wires;

        // List of all connections in the circuit box
        private ImmutableArray<CircuitBoxConnection> connections = ImmutableArray<CircuitBoxConnection>.Empty;

                                                 // Nodes that were under cursor when dragging started
        private ImmutableHashSet<CircuitBoxNode> lastNodesUnderCursor = ImmutableHashSet<CircuitBoxNode>.Empty,
                                                 // Nodes that were selected when dragging started
                                                 lastSelectedComponents = ImmutableHashSet<CircuitBoxNode>.Empty,
                                                 // Nodes that should be moved when dragging
                                                 moveAffectedComponents = ImmutableHashSet<CircuitBoxNode>.Empty;

        public Option<(CircuitBoxResizeDirection, CircuitBoxNode)> LastResizeAffectedNode = Option.None;

        public ImmutableHashSet<CircuitBoxNode> GetLastComponentsUnderCursor() => lastNodesUnderCursor;
        public ImmutableHashSet<CircuitBoxNode> GetMoveAffectedComponents() => moveAffectedComponents;

        public Option<CircuitBoxConnection> LastConnectorUnderCursor = Option.None;
        public Option<CircuitBoxWire> LastWireUnderCursor = Option.None;

        /// <summary>
        /// If the user is currently dragging a node
        /// </summary>
        public bool IsDragging { get; private set; }

        /// <summary>
        /// If the user is currently dragging a wire
        /// </summary>
        public bool IsWiring { get; private set; }

        /// <summary>
        /// If the user grabbed a side of a node and is resizing a node
        /// </summary>
        public bool IsResizing { get; private set; }

        private Vector2 startClick = Vector2.Zero;
        private readonly CircuitBoxUI circuitBoxUi;

        /// <summary>
        /// How far the user has to drag the mouse while holding down the button before dragging starts
        /// </summary>
        private const float dragTreshold = 16f;

        public CircuitBoxMouseDragSnapshotHandler(CircuitBoxUI ui)
        {
            circuitBoxUi = ui;
        }

        /// <summary>
        /// Called when the user holds down the mouse button
        /// </summary>
        public void StartDragging()
        {
            Vector2 cursorPos = circuitBoxUi.GetCursorPosition();
            SnapshotNodesUnderCursor(cursorPos);
            SnapshotSelectedNodes();
            SnapshotMoveAffectedNodes();
            startClick = cursorPos;
        }
        
        public void ClearSnapshot()
        {
            lastNodesUnderCursor = ImmutableHashSet<CircuitBoxNode>.Empty;
            lastSelectedComponents = ImmutableHashSet<CircuitBoxNode>.Empty;
            moveAffectedComponents = ImmutableHashSet<CircuitBoxNode>.Empty;
            LastConnectorUnderCursor = Option.None;
            LastWireUnderCursor = Option.None;
            LastResizeAffectedNode = Option.None;
        }

        /// <summary>
        /// Finds all connections and gathers them into a single list for easier iteration.
        /// </summary>
        public void UpdateConnections()
        {
            var builder = ImmutableArray.CreateBuilder<CircuitBoxConnection>();

            builder.AddRange(circuitBoxUi.CircuitBox.Inputs);
            builder.AddRange(circuitBoxUi.CircuitBox.Outputs);

            foreach (var node in Nodes)
            {
                builder.AddRange(node.Connectors);
            }

            connections = builder.ToImmutable();
        }

        /// <summary>
        /// Finds a possible connector under the cursor.
        /// </summary>
        public Option<CircuitBoxConnection> FindConnectorUnderCursor(Vector2 cursorPos)
        {
            foreach (var connection in connections)
            {
                if (connection.Contains(cursorPos))
                {
                    return Option.Some(connection);
                }
            }

            return Option.None;
        }

        /// <summary>
        /// Finds a possible wire under the cursor.
        /// </summary>
        public Option<CircuitBoxWire> FindWireUnderCursor(Vector2 cursorPos)
        {
            foreach (CircuitBoxWire wire in Wires)
            {
                if (wire is { IsSelected: true, IsSelectedByMe: false }) { continue; }
                if (wire.Renderer.Contains(cursorPos))
                {
                    return Option.Some(wire);
                }
            }

            return Option.None;
        }

        /// <summary>
        /// Find all nodes that are currently under the cursor that are not selected by someone else.
        /// </summary>
        public ImmutableHashSet<CircuitBoxNode> FindNodesUnderCursor(Vector2 cursorPos)
        {
            var builder = ImmutableHashSet.CreateBuilder<CircuitBoxNode>();
            foreach (var node in Nodes)
            {
                if (node is { IsSelected: true, IsSelectedByMe: false }) { continue; }
                if (node.Rect.Contains(cursorPos))
                {
                    builder.Add(node);
                }
            }

            return builder.ToImmutable();
        }

        /// <summary>
        /// Finds and stores all nodes, connectors and wires that are under the cursor when dragging starts.
        /// </summary>
        private void SnapshotNodesUnderCursor(Vector2 cursorPos)
        {
            lastNodesUnderCursor = FindNodesUnderCursor(cursorPos);
            LastConnectorUnderCursor = FindConnectorUnderCursor(cursorPos);
            LastWireUnderCursor = FindWireUnderCursor(cursorPos);
            LastResizeAffectedNode = FindResizeBorderUnderCursor(lastNodesUnderCursor, cursorPos);
        }

        private Option<(CircuitBoxResizeDirection, CircuitBoxNode)> FindResizeBorderUnderCursor(ImmutableHashSet<CircuitBoxNode> nodes, Vector2 cursorPos)
        {
            if (!nodes.Any()) { return Option.None; }

            var node = circuitBoxUi.GetTopmostNode(nodes);
            if (node is null || !node.IsResizable) { return Option.None; }

            const float borderSize = 32f;

            var rect = node.Rect;
            RectangleF bottomBorder = new(rect.X, rect.Top, rect.Width, borderSize);
            RectangleF rightBorder = new(rect.Right - borderSize, rect.Y, borderSize, rect.Height);
            RectangleF leftBorder = new(rect.X, rect.Y, borderSize, rect.Height);

            bool hoverBottom = bottomBorder.Contains(cursorPos),
                 hoverRight = rightBorder.Contains(cursorPos),
                 hoverLeft = leftBorder.Contains(cursorPos);

            var dir = CircuitBoxResizeDirection.None;

            if (hoverBottom) { dir |= CircuitBoxResizeDirection.Down; }
            if (hoverRight) { dir |= CircuitBoxResizeDirection.Right; }
            if (hoverLeft) { dir |= CircuitBoxResizeDirection.Left; }

            if (dir is CircuitBoxResizeDirection.None)
            {
                return Option.None;
            }

            return Option.Some((dir, node));
        }

        /// <summary>
        /// Stores all nodes that are currently selected when dragging starts.
        /// There's no real way to change your selection while dragging so this is kinda pointless
        /// but we snapshot it anyway just in case.
        /// </summary>
        private void SnapshotSelectedNodes()
        {
            lastSelectedComponents = Nodes.Where(static n => n is { IsSelected: true, IsSelectedByMe: true }).ToImmutableHashSet();
        }

        /// <summary>
        /// Stores all nodes that should be moved when dragging starts.
        /// </summary>
        private void SnapshotMoveAffectedNodes()
        {
            bool moveSelection = lastNodesUnderCursor.Any(node => lastSelectedComponents.Contains(node));

            /*
             * If the user is dragging a selection, we should move all selected nodes (true).
             * 
             * But for convenience, if the user is dragging a single node that is not part of the selection,
             * we should move that node only instead and leave the selection alone. (false)
             */
            moveAffectedComponents = moveSelection switch
            {
                true => lastSelectedComponents,
                false => circuitBoxUi.GetTopmostNode(lastNodesUnderCursor) switch
                {
                    null => ImmutableHashSet<CircuitBoxNode>.Empty,
                    var node => ImmutableHashSet.Create(node)
                }
            };
        }

        public Vector2 GetDragAmount(Vector2 mousePos) => mousePos - startClick;

        /// <summary>
        /// Called when the user releases the mouse button
        /// </summary>
        public void EndDragging()
        {
            startClick = Vector2.Zero;
            IsDragging = false;
            IsWiring = false;
            IsResizing = false;
            lastNodesUnderCursor = ImmutableHashSet<CircuitBoxNode>.Empty;
        }

        public void UpdateDrag(Vector2 cursorPos)
        {
            // if there are no connectors under cursor, we can't be wiring anything
            if (LastConnectorUnderCursor.IsNone())
            {
                IsWiring = false;
            }

            // if there are no nodes under cursor, we can't be dragging anything
            if (lastNodesUnderCursor.IsEmpty)
            {
                IsDragging = false;
            }

            if (LastResizeAffectedNode.IsNone())
            {
                IsResizing = false;
            }

            // startClick is set to zero when the user releases the mouse button, so we should be neither dragging nor wiring in this state
            if (startClick == Vector2.Zero)
            {
                IsDragging = false;
                IsWiring = false;
                IsResizing = false;
                return;
            }

            if (circuitBoxUi.Locked) { return; }
            bool isDragThresholdExceeded = Vector2.DistanceSquared(startClick, cursorPos) > dragTreshold * dragTreshold;

            if (LastConnectorUnderCursor.IsSome())
            {
                IsWiring |= isDragThresholdExceeded;
            }
            else if (LastResizeAffectedNode.IsSome())
            {
                IsResizing |= isDragThresholdExceeded;
            }
            else
            {
                IsDragging |= isDragThresholdExceeded;
            }
        }
    }
}