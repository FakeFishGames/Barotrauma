using System.Diagnostics.CodeAnalysis;
using Barotrauma;

namespace HomeAndHarbor
{
    public enum TileType
    {
        /// <summary>
        /// Default, unallocated tile type.
        /// </summary>
        Empty,
        /// <summary>
        /// A tile that is adjacent to a prefab.
        /// The UI draw position of this tile can be
        /// determined by the nearby prefab tile.
        /// </summary>
        Path,
        /// <summary>
        /// A tile where a prefab is placed in the UI.
        /// </summary>
        Prefab
    }

    public readonly struct Tile
    {
        public readonly TileType Type;
        private readonly TechnologyPrefab? prefab;

        private Tile(TileType type, TechnologyPrefab? prefab)
        {
            this.prefab = prefab;
            Type = type;
        }

        public static Tile CreatePath()
            => new Tile(TileType.Path, prefab: null);

        public static Tile CreatePrefab(TechnologyPrefab prefab)
            => new Tile(TileType.Prefab, prefab);

        public bool TryGetPrefab([NotNullWhen(true)] out TechnologyPrefab? value)
        {
            value = prefab;
            return Type is TileType.Prefab;
        }
    }

    /// <summary>
    /// Constructs a layout for the technology UI
    /// </summary>
    public class TechnologyUIPrefabLayout
    {
        /// <summary>
        /// The size of tiers, used to pre-allocate enough space in the technology UI.
        /// </summary>
        public readonly Dictionary<TechnologyTier, int> MaxTierCounts;

        /// <summary>
        /// Layout that <see cref="TechnologyUI"/> uses to construct the UI.
        /// </summary>
        public readonly Dictionary<(TechnologyTier, TechnologyCategory), Tile[]> LayoutTiles;

        /// <summary>
        /// A 'world' constructed from the UI that <see cref="TechUILineManager"/> uses
        /// to resolve paths for dependencies.
        /// </summary>
        public readonly Tile[,] PathTiles;

        public TechnologyUIPrefabLayout(TechnologyTreePrefab tree)
            => ConstructTiles(tree, out LayoutTiles, out PathTiles, out MaxTierCounts);

        /// <summary>
        /// The 'Path Finding World' is 3 times larger than the
        /// constructed layout due to having 8 path tiles around
        /// every prefab tile. This is why some indices and lengths
        /// are multiplied by 3. For example, if the layout row has
        /// indices 0 and 2 with the length of 3:
        /// [0][ ][2]
        /// the path finding world will look like:
        /// 3 * 3 = length of 9
        /// 0 * 3 + 1 = index 1
        /// 2 + 3 + 1 = index 7
        /// [ ][1][ ][ ][ ][ ][ ][7][ ]
        /// </summary>
        private const int PathMapScale = 3;

        private static void ConstructTiles(TechnologyTreePrefab tree,
                                           out Dictionary<(TechnologyTier, TechnologyCategory), Tile[]> layoutTiles,
                                           out Tile[,] pathTiles,
                                           out Dictionary<TechnologyTier, int> maxTierCounts)
        {
            layoutTiles = [];
            maxTierCounts = [];

            Dictionary<TechnologyCategory, List<Tile>> pathTileBuilder = [];

            foreach (TechnologyCategory category in tree.Categories)
            {
                pathTileBuilder[category] = [];
            }

            int prefabsInPreviousTiers = 0;
            foreach (TechnologyTier tier in tree.Tiers)
            {
                Dictionary<Identifier, TechnologyPrefab[]> prefabsInTier = TechnologyPrefab.Prefabs
                                                                                           .Where(tree.Belongs)
                                                                                           .Where(p => p.Tier == tier.Identifier)
                                                                                           .GroupBy(static prefab => prefab.Category)
                                                                                           .ToDictionary(static g => g.Key, static g => g.ToArray());

                int rowsInThisTier = prefabsInTier.SelectMany(static p => p.Value).Max(p => tier.GetRow(p));
                maxTierCounts[tier]  = rowsInThisTier + 1;
                foreach (var (categoryIdentifier, prefabs) in prefabsInTier)
                {
                    TechnologyCategory category = tree.Categories.First(c => c.Identifier == categoryIdentifier);

                    Tile[] tiles = new Tile[rowsInThisTier + 1];
                    foreach (TechnologyPrefab prefab in prefabs)
                    {
                        int row = tier.GetRow(prefab);
                        Tile tile = Tile.CreatePrefab(prefab);
                        tiles[row] = tile;
                        int index = prefabsInPreviousTiers + row;
                        List<Tile> categoryTiles = pathTileBuilder[category];
                        if (index >= categoryTiles.Count)
                        {
                            int toAdd = index - (categoryTiles.Count);
                            for (int i = 0; i < toAdd; i++)
                            {
                                categoryTiles.Add(default);
                            }
                        }

                        if (index == categoryTiles.Count)
                        {
                            categoryTiles.Add(tile);
                        }
                        else
                        {
                            categoryTiles[index] = tile;
                        }
                    }

                    layoutTiles[(tier, category)] = tiles;
                }

                prefabsInPreviousTiers += rowsInThisTier + 1;
            }

            pathTiles = new Tile[tree.Categories.Length * PathMapScale, pathTileBuilder.Values.Max(static l => l.Count) * PathMapScale];

            for (int x = 0; x < tree.Categories.Length; x++)
            {
                for (int y = 0; y < pathTileBuilder[tree.Categories[x]].Count; y++)
                {
                    int xIndex = x * PathMapScale + 1,
                        yIndex = y * PathMapScale + 1;

                    Tile tile = pathTileBuilder[tree.Categories[x]][y];
                    if (pathTiles[xIndex, yIndex].Type is TileType.Empty)
                    {
                        pathTiles[xIndex, yIndex] = tile;
                    }
                    else
                    {
                        if (tile.Type is TileType.Prefab)
                        {
                            throw new InvalidOperationException();
                        }
                    }

                    if (tile.Type is TileType.Prefab)
                    {
                        for (int xOffset = -1; xOffset <= 1; xOffset++)
                        {
                            for (int yOffset = -1; yOffset <= 1; yOffset++)
                            {
                                if (xOffset is 0 && yOffset is 0) { continue; }
                                pathTiles[xIndex + xOffset, yIndex + yOffset] = Tile.CreatePath();
                            }
                        }
                    }
                }
            }
        }
    }
}