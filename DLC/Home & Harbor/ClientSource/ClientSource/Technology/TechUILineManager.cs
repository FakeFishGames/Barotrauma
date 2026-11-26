using System.Collections.Immutable;
using Barotrauma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor
{
    /// <summary>
    /// Resolves and draws dependency lines in the technology UI
    /// </summary>
    internal class TechUILineManager
    {
        private readonly record struct Coordinate(int X, int Y)
        {
            public static readonly Coordinate Zero = new Coordinate(0, 0),
                                              Invalid = new Coordinate(-1, -1);

            public bool IsValid
                => X > 0 && Y > 0;

            public static Vector2 operator *(Coordinate v1, float v2)
                => new Vector2(v1.X * v2, v1.Y * v2);

            public override int GetHashCode()
                => HashCode.Combine(X, Y);

            public static float DistanceSquared(Coordinate value1, Coordinate value2)
            {
                float v1 = value1.X - value2.X, v2 = value1.Y - value2.Y;
                return (v1 * v1) + (v2 * v2);
            }

            public override string ToString()
                => $"{nameof(Coordinate)}(X: {X}, Y: {Y})";
        }

        private readonly record struct ResolvedPath(ImmutableArray<Coordinate> Path, TechnologyPrefab From, TechnologyPrefab To);

        private readonly TechnologyTreePrefab tree;
        private readonly TechnologyUIPrefabLayout layout;
        private readonly TechnologyUI techUi;

        private Tile[,] TileMap
            => layout.PathTiles;

        private readonly ImmutableArray<ResolvedPath> resolvedPaths;
        private ImmutableArray<Rectangle> groupRects = ImmutableArray<Rectangle>.Empty;
        private ImmutableDictionary<TechnologyPrefab, Rectangle> groupRectsByPrefab = ImmutableDictionary<TechnologyPrefab, Rectangle>.Empty;
        private readonly ImmutableDictionary<TechnologyPrefab, (TechnologyPrefab From, TechnologyPrefab To)> groupRedirects;

        private readonly Dictionary<Coordinate, Vector2> cachedDrawPositions = [];

        /// <summary>
        /// Lines (identified by the prefabs they're drawn between) that are highlighted and their colors, e.g. the ones leading from the prerequisites of the selected technology.
        /// </summary>
        private ImmutableDictionary<(TechnologyPrefab From, TechnologyPrefab To), Color> highlightedLines = ImmutableDictionary<(TechnologyPrefab From, TechnologyPrefab To), Color>.Empty;

        private static float RectOffset
            => 24f;

        private static float LineWidth
            => 4f;

        private static readonly Color LineColor = Color.Gray * 0.3f;

        public TechUILineManager(TechnologyUIPrefabLayout layout,
                                 TechnologyTreePrefab tree,
                                 TechnologyUI techUi)
        {
            this.tree = tree;
            this.techUi = techUi;
            this.layout = layout;
            var pathBuilder = ImmutableArray.CreateBuilder<ResolvedPath>();
            var mergeRedirectBuilder = ImmutableDictionary.CreateBuilder<TechnologyPrefab, (TechnologyPrefab, TechnologyPrefab)>();

            foreach (TechnologyPrefabGroup group in tree.Groups)
            {
                if (group.Prefabs.IsEmpty)
                {
                    DebugConsole.ThrowError($"{nameof(TechnologyPrefabGroup)} in tree \"{tree.Identifier}\" is invalid. (no prefabs defined)");
                    continue;
                }

                Dictionary<Coordinate, TechnologyPrefab> matchingPrefabs = [];
                foreach (var prefab in this.techUi.PrefabComponents.Keys)
                {
                    if (group.Prefabs.Contains(prefab.Identifier))
                    {
                        matchingPrefabs.Add(Find(prefab), prefab);
                    }
                }

                // Find the top-leftmost and bottom-rightmost prefab in this region to anchor dependencies into
                TechnologyPrefab? topLeftmost = null, bottomRightmost = null;

                float maxTopLeftDistance = float.MaxValue,
                      maxBottomRightDistance = float.MaxValue;

                Coordinate bottomRightCorner = new Coordinate(TileMap.GetLength(0) - 1, TileMap.GetLength(1) - 1);

                foreach (var (cord, prefab) in matchingPrefabs)
                {
                    float topLeftDistance = Coordinate.DistanceSquared(Coordinate.Zero, cord),
                          bottomRightDistance = Coordinate.DistanceSquared(bottomRightCorner, cord);

                    if (topLeftDistance < maxTopLeftDistance)
                    {
                        topLeftmost = prefab;
                        maxTopLeftDistance = topLeftDistance;
                    }

                    if (bottomRightDistance < maxBottomRightDistance)
                    {
                        bottomRightmost = prefab;
                        maxBottomRightDistance = bottomRightDistance;
                    }
                }

                if (bottomRightmost != null && topLeftmost != null)
                {
                    foreach (TechnologyPrefab prefab in matchingPrefabs.Values)
                    {
                        mergeRedirectBuilder.Add(prefab, (bottomRightmost, topLeftmost));
                    }
                }
                else
                {
                    DebugConsole.ThrowError("Unable to find the bottom-rightmost or top-leftmost prefab, group is invalid.");
                }
            }

            groupRedirects = mergeRedirectBuilder.ToImmutable();

            foreach (TechnologyPrefab prefab in TechnologyPrefab.Prefabs.Where(tree.Belongs))
            {
                foreach (TechnologyPrefab.TechnologyPrerequisite prerequisite in prefab.Prerequisites)
                {
                    foreach (Identifier prereqIdentifier in prerequisite.Identifiers)
                    {
                        TechnologyPrefab prereqPrefab = TechnologyPrefab.Prefabs[prereqIdentifier];

                        var (from, to) = GetLineEnds(prereqPrefab, prefab);

                        // Check if an identical line has been resolved already
                        if (pathBuilder.Any(path => path.From == from && path.To == to)) { continue; }

                        bool preferRight = tree.PreferRightLines.Contains(GetLineIdentifier(from, to));
                        ImmutableArray<Coordinate> path = CleanUpPath(Resolve(from, to, preferRight));
                        pathBuilder.Add(new ResolvedPath(path, from, to));
                    }
                }
            }

            resolvedPaths = pathBuilder.ToImmutable();
            Recalculate();
        }

        // Removes unnecessary nodes in a straight line
        private static ImmutableArray<Coordinate> CleanUpPath(List<Coordinate> nodes)
        {
            bool removed;
            do
            {
                removed = false;
                for (int i = nodes.Count - 2; i > 0; i--)
                {
                    if (Math.Abs(nodes[i - 1].X - nodes[i].X) < 1
                        && Math.Abs(nodes[i + 1].X - nodes[i].X) < 1
                        && Math.Sign(nodes[i - 1].Y - nodes[i].Y) != Math.Sign(nodes[i + 1].Y - nodes[i].Y)
                        || Math.Abs(nodes[i - 1].Y - nodes[i].Y) < 1
                        && Math.Abs(nodes[i + 1].Y - nodes[i].Y) < 1
                        && Math.Sign(nodes[i - 1].X - nodes[i].X) != Math.Sign(nodes[i + 1].X - nodes[i].X))
                    {
                        nodes.RemoveAt(i);
                        removed = true;
                    }
                }
            } while (removed);

            return [..nodes];
        }

        private static Identifier GetLineIdentifier(TechnologyPrefab from, TechnologyPrefab to)
            => $"{from.Identifier}->{to.Identifier}".ToIdentifier();

        /// <summary>
        /// Returns the prefabs the line from a prerequisite to the technology that requires it is drawn between.
        /// Grouped prefabs are redirected to the group's anchor prefabs.
        /// </summary>
        private (TechnologyPrefab From, TechnologyPrefab To) GetLineEnds(TechnologyPrefab prerequisite, TechnologyPrefab dependent)
            => (groupRedirects.TryGetValue(prerequisite, out var value1) ? value1.From : prerequisite,
                groupRedirects.TryGetValue(dependent, out var value2) ? value2.To : dependent);

        /// <summary>
        /// Highlights the lines leading from the given prerequisites to the technology that requires them, in the given colors. Replaces any previous highlights.
        /// If multiple prerequisites share a line (grouped prefabs), the last one's color is used.
        /// </summary>
        public void HighlightLines(TechnologyPrefab dependent, IEnumerable<(TechnologyPrefab Prerequisite, Color Color)> prerequisites)
        {
            var builder = ImmutableDictionary.CreateBuilder<(TechnologyPrefab From, TechnologyPrefab To), Color>();
            foreach (var (prerequisite, color) in prerequisites)
            {
                builder[GetLineEnds(prerequisite, dependent)] = color;
            }
            highlightedLines = builder.ToImmutable();
        }

        public void ClearHighlightedLines()
        {
            highlightedLines = highlightedLines.Clear();
        }

        private List<Coordinate> Resolve(TechnologyPrefab from, TechnologyPrefab to, bool preferRight)
        {
            Coordinate start = Find(from),
                       dest = Find(to);

            List<Coordinate> path = [];
            if (start.IsValid && dest.IsValid)
            {
                path = FindShortestPath(TileMap, start with { Y = start.Y + 1 }, dest with { Y = dest.Y - 1 }, preferRight);
            }

            path.Insert(0, dest);
            path.Add(start);
            return path;

            static List<Coordinate> FindShortestPath(Tile[,] tiles, Coordinate start, Coordinate end, bool preferRight)
            {
                BFS(tiles, start, end, preferRight,
                    out Dictionary<Coordinate, Coordinate> parents,
                    out HashSet<Coordinate> visited);

                // should never happen now
                if (!visited.Contains(end))
                {
                    DebugConsole.ThrowError($"Unable to pathfind from {start} to {end}. Path is blocked.");
                    return [];
                }

                // Finds the shortest path in the graph generated by BFS
                List<Coordinate> builder = [];
                Coordinate current = end;
                builder.Add(end);

                while (parents.ContainsKey(current))
                {
                    builder.Add(parents[current]);
                    current = parents[current];
                }

                return builder;
            }

            // Breadth-First Search algorithm. Generates a graph of valid paths from searchStart to searchEnd
            static void BFS(Tile[,] tiles, Coordinate searchStart, Coordinate searchEnd, bool preferRight,
                            out Dictionary<Coordinate, Coordinate> parents,
                            out HashSet<Coordinate> visited)
            {
                parents = [];
                visited = [];
                Queue<Coordinate> queue = [];

                visited.Add(searchStart);
                queue.Enqueue(searchStart);

                while (queue.TryDequeue(out Coordinate current))
                {
                    foreach (Coordinate neighbor in GetNeighbors(tiles, current, preferRight))
                    {
                        if (!visited.Contains(neighbor))
                        {
                            parents[neighbor] = current;
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                return;

                // The order of the neighbors determines which route is picked out of equally short ones:
                // the neighbors that are checked first get visited first. By default left is preferred.
                static IEnumerable<Coordinate> GetNeighbors(Tile[,] tiles, Coordinate cord, bool preferRight)
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        int x = preferRight ? -i : i;
                        for (int y = -1; y <= 1; y++)
                        {
                            // no diagonals
                            if (Math.Abs(x) + Math.Abs(y) > 1) { continue; }

                            if (x is 0 && y is 0) { continue; }

                            if (cord.X + x < 0 || cord.X + x >= tiles.GetLength(0)) { continue; }

                            if (cord.Y + y < 0 || cord.Y + y >= tiles.GetLength(1)) { continue; }

                            Coordinate newCoord = new Coordinate(x + cord.X, y + cord.Y);

                            Tile tile = tiles[newCoord.X, newCoord.Y];

                            if (tile.Type is not TileType.Prefab)
                            {
                                yield return new Coordinate(cord.X + x, cord.Y + y);
                            }
                        }
                    }
                }
            }
        }

        // Finds a coordinate where a prefab lies
        private Coordinate Find(TechnologyPrefab prefab)
        {
            for (int x = 0; x < TileMap.GetLength(0); x++)
            {
                for (int y = 0; y < TileMap.GetLength(1); y++)
                {
                    Tile tile = TileMap[x, y];
                    if (tile.TryGetPrefab(out TechnologyPrefab? value) && value == prefab)
                    {
                        return new Coordinate(x, y);
                    }
                }
            }

            return Coordinate.Invalid;
        }

        // Draws a view of the pathfinding 'world', used for debugging.
        public void DrawDebug(SpriteBatch spriteBatch, Vector2 location, float size)
        {
            float spacing = size / 10f;
            for (int x = 0; x < TileMap.GetLength(0); x++)
            {
                for (int y = 0; y < TileMap.GetLength(1); y++)
                {
                    Tile tile = TileMap[x, y];
                    Vector2 drawPos = new Vector2(location.X + (x * (size + spacing)), location.Y + (y * (size + spacing)));
                    Color color = tile.Type switch
                    {
                        TileType.Path    => GUIStyle.Blue,
                        TileType.Prefab  => GUIStyle.Green,
                        TileType.Empty   => GUIStyle.Red,
                        _                => throw new InvalidOperationException()
                    };

                    spriteBatch.DrawPoint(drawPos, color, size);
                }
            }

            foreach (ResolvedPath r in resolvedPaths)
            {
                Coordinate first = r.Path.First();
                foreach (Coordinate pathPoint in r.Path.Skip(1))
                {
                    GUI.DrawLine(spriteBatch, location + (first * (size + spacing)), location + (pathPoint * (size + spacing)), Color.Red, width: 4f);
                    first = pathPoint;
                }
            }
        }

        public void Recalculate()
        {
            cachedDrawPositions.Clear();

            var groupRectBuilder = ImmutableArray.CreateBuilder<Rectangle>();
            var groupRectByPrefabBuilder = ImmutableDictionary.CreateBuilder<TechnologyPrefab, Rectangle>();
            foreach (TechnologyPrefabGroup group in tree.Groups)
            {
                if (group.Prefabs.IsEmpty)
                {
                    DebugConsole.ThrowError($"{nameof(TechnologyPrefabGroup)} in tree \"{tree.Identifier}\" is invalid. (no prefabs defined)");
                    continue;
                }

                List<Rectangle> rects = [];
                List<TechnologyPrefab> groupPrefabs = [];
                foreach (var (prefab, button) in techUi.PrefabComponents)
                {
                    if (group.Prefabs.Contains(prefab.Identifier))
                    {
                        rects.Add(new Rectangle(button.Rect.Location - techUi.PrefabParents.Rect.Location, button.Rect.Size));
                        groupPrefabs.Add(prefab);
                    }
                }

                Rectangle unionRect = rects.Aggregate(Rectangle.Union);
                unionRect.Inflate(RectOffset / 2f, RectOffset / 2f);

                groupRectBuilder.Add(unionRect);
                foreach (TechnologyPrefab prefab in groupPrefabs)
                {
                    groupRectByPrefabBuilder[prefab] = unionRect;
                }
            }

            for (int x = 0; x < TileMap.GetLength(0); x++)
            {
                for (int y = 0; y < TileMap.GetLength(1); y++)
                {
                    Coordinate cord = new Coordinate(x, y);
                    cachedDrawPositions.Add(cord, GetDrawPos(cord) - this.techUi.PrefabParents.Rect.Location.ToVector2());
                }
            }

            groupRects = groupRectBuilder.ToImmutable();
            groupRectsByPrefab = groupRectByPrefabBuilder.ToImmutable();
        }

        private Vector2 GetDrawPos(Coordinate cord, bool resolveBlocked = true)
        {
            Tile tile = TileMap[cord.X, cord.Y];

            // If the tile is a prefab, the position is the center of the matching UI element
            if (tile.TryGetPrefab(out TechnologyPrefab? prefab))
            {
                if (techUi.PrefabComponents.TryGetValue(prefab, out GUIButton? value))
                {
                    Vector2 center = value.Rect.Center.ToVector2();

                    // if the prefab is grouped, anchor the line onto the group rectangle instead
                    if (groupRedirects.TryGetValue(prefab, out var pair))
                    {
                        if (prefab == pair.From)
                        {
                            return new Vector2(center.X, value.Rect.Bottom + (RectOffset / 2f + LineWidth / 2f));
                        }
                        else if (prefab == pair.To)
                        {
                            return new Vector2(center.X, value.Rect.Top - (RectOffset / 2f + LineWidth / 2f));
                        }
                    }

                    return center;
                }
            }
            // If the tile is a path, find the nearby prefab tile and use the appropriate side of its UI element to determine the position,
            // For example, if there's a prefab tile top right of this tile, then the draw position is bottom left of the UI element
            else if (tile.Type is TileType.Path)
            {
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        if (x is 0 && y is 0) { continue; }

                        int xPos = cord.X + x,
                            yPos = cord.Y + y;
                        if (xPos < 0 || xPos >= TileMap.GetLength(0) || yPos < 0 || yPos >= TileMap.GetLength(1)) { continue; }

                        Tile nearTile = TileMap[xPos, yPos];

                        if (nearTile.TryGetPrefab(out TechnologyPrefab? value) && techUi.PrefabComponents.TryGetValue(value, out GUIButton? button))
                        {
                            float offset = RectOffset;
                            RectangleF rect = button.Rect;

                            const int left = -1,
                                      right = +1,
                                      top = +1,
                                      bottom = -1,
                                      center = 0;

                            return (x, y) switch
                            {
                                (left, bottom)   => (rect.Right + offset, rect.Bottom + offset),
                                (left, center)   => (rect.Right + offset, rect.Center.Y),
                                (left, top)      => (rect.Right + offset, rect.Top - offset),
                                (right, bottom)  => (rect.Left - offset, rect.Bottom + offset),
                                (right, center)  => (rect.Left - offset, rect.Center.Y),
                                (right, top)     => (rect.Left - offset, rect.Top - offset),
                                (center, bottom) => (rect.Center.X, rect.Bottom + offset),
                                (center, top)    => (rect.Center.X, rect.Top - offset),
                                _                => rect.Center
                            };
                        }
                    }
                }
            }
            // if the tile is blocked, we have to find path or prefab tiles on the same row and colum to determine the X and Y draw position
            else if (resolveBlocked)
            {
                float xPos = 0,
                      yPos = 0;

                // Find the X position based on a tile on the same row
                for (int x = 0; x < TileMap.GetLength(0); x++)
                {
                    if (GetDrawPos(new Coordinate(x, cord.Y), resolveBlocked: false) is { X: not 0, Y: not 0 } found)
                    {
                        yPos = found.Y;
                    }
                }

                // Find the Y position based on a tile on the same column
                for (int y = 0; y < TileMap.GetLength(1); y++)
                {
                    if (GetDrawPos(new Coordinate(cord.X, y), resolveBlocked: false) is { X: not 0, Y: not 0 } found)
                    {
                        xPos = found.X;
                    }
                }

                return new Vector2(xPos, yPos);
            }

            return Vector2.Zero;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Vector2 parentOffset = techUi.PrefabParents.Rect.Location.ToVector2();

            int lineWidth = (int)(LineWidth * GUI.Scale);
            //ensure the line width is an even number so we can offset
            //by whole pixels when we're offseting them by half the width below
            if (lineWidth % 2 == 1)
            {
                lineWidth++;
            }

            foreach (Rectangle rect in groupRects)
            {
                GUI.DrawRectangle(spriteBatch, GetGroupRectDrawArea(rect), LineColor, isFilled: false, thickness: lineWidth);
            }

            foreach (ResolvedPath r in resolvedPaths)
            {
                if (highlightedLines.ContainsKey((r.From, r.To))) { continue; }
                DrawPath(r, LineColor);
            }
            //draw the highlighted lines last so overlapping normal lines don't cover them
            foreach (ResolvedPath r in resolvedPaths)
            {
                if (!highlightedLines.TryGetValue((r.From, r.To), out Color highlightColor)) { continue; }
                DrawPath(r, highlightColor);
            }

            return;

            void DrawPath(ResolvedPath r, Color color)
            {
                Vector2 fineTuneOffset =tree.FineTuneLineOffsets.GetValueOrDefault(GetLineIdentifier(r.From, r.To)) * GUI.Scale;
                // Round to whole multiples of the line width to keep the lines aligned with the snapped positions.
                // Fractional offsets make DrawLine truncate the start positions and lengths inconsistently, causing 1px gaps.
                fineTuneOffset = new Vector2(MathF.Round(fineTuneOffset.X / lineWidth), MathF.Round(fineTuneOffset.Y / lineWidth)) * lineWidth;

                for (int i = 1; i < r.Path.Length; i++)
                {
                    Coordinate from = r.Path[i - 1];
                    Coordinate to = r.Path[i];

                    Vector2 fromDrawPos = cachedDrawPositions.GetValueOrDefault(from) + parentOffset;
                    Vector2 toDrawPos = cachedDrawPositions.GetValueOrDefault(to) + parentOffset;

                    bool isVertical = Math.Abs(fromDrawPos.X - toDrawPos.X) < 1f;
                    float offset = lineWidth / 2f;

                    fromDrawPos = new Vector2((int)(fromDrawPos.X / lineWidth) * lineWidth, (int)(fromDrawPos.Y / lineWidth) * lineWidth);
                    toDrawPos = new Vector2((int)(toDrawPos.X / lineWidth) * lineWidth, (int)(toDrawPos.Y / lineWidth) * lineWidth);

                    // Extends the line by the line width to eliminate gaps in the corners
                    if (isVertical)
                    {
                        if (fromDrawPos.Y > toDrawPos.Y)
                        {
                            offset *= -1f;
                        }

                        fromDrawPos.Y -= offset;
                        toDrawPos.Y += offset;
                    }
                    else
                    {
                        if (fromDrawPos.X > toDrawPos.X)
                        {
                            offset *= -1f;
                        }

                        fromDrawPos.X += offset;
                        toDrawPos.X -= offset;
                    }

                    // Snapping can leave a gap between the line and a group rectangle, make the lines connected to groups end exactly at the rectangle's edge.
                    // The path starts from the "To" prefab and ends at the "From" prefab.
                    if (isVertical)
                    {
                        if (i == 1 && TryGetGroupEdgeY(r.To, fromDrawPos.Y, out float fromEdgeY))
                        {
                            fromDrawPos.Y = fromEdgeY - fineTuneOffset.Y;
                        }
                        if (i == r.Path.Length - 1 && TryGetGroupEdgeY(r.From, toDrawPos.Y, out float toEdgeY))
                        {
                            toDrawPos.Y = toEdgeY - fineTuneOffset.Y;
                        }
                    }

                    GUI.DrawLine(spriteBatch, fromDrawPos + fineTuneOffset, toDrawPos + fineTuneOffset, color, width: lineWidth);
                }
            }
        }

        private Rectangle GetGroupRectDrawArea(Rectangle rect)
            => rect with { Location = rect.Location + techUi.PrefabParents.Rect.Location - new Point(0, 1) };

        /// <summary>
        /// If the prefab is in a group, returns the Y position of the group rectangle's top or bottom edge (whichever is closer to the end of the line).
        /// </summary>
        private bool TryGetGroupEdgeY(TechnologyPrefab prefab, float lineEndY, out float edgeY)
        {
            edgeY = 0f;
            if (!groupRectsByPrefab.TryGetValue(prefab, out Rectangle rect)) { return false; }

            Rectangle drawArea = GetGroupRectDrawArea(rect);
            edgeY = lineEndY < drawArea.Center.Y ? drawArea.Top : drawArea.Bottom;
            return true;
        }
    }
}