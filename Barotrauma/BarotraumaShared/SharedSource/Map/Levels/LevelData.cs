using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class LevelData
    {
        public enum LevelType
        {
            LocationConnection,
            Outpost
        }

        public readonly LevelType Type;

        public readonly string Seed;

        public float Difficulty;

        public readonly Biome Biome;

        public readonly LevelGenerationParams GenerationParams;

        public OutpostGenerationParams ForceOutpostGenerationParams;

        public readonly Point Size;

        public readonly List<EventPrefab> EventHistory = new List<EventPrefab>();
        public readonly List<EventPrefab> NonRepeatableEvents = new List<EventPrefab>();

        public LevelData(string seed, float difficulty, float sizeFactor, LevelGenerationParams generationParams, Biome biome)
        {
            Seed = seed ?? throw new ArgumentException("Seed was null");
            Biome = biome ?? throw new ArgumentException("Biome was null");
            GenerationParams = generationParams ?? throw new ArgumentException("Level generation parameters were null");
            Type = GenerationParams.Type;
            Difficulty = difficulty;

            sizeFactor = MathHelper.Clamp(sizeFactor, 0.0f, 1.0f);
            int width = (int)MathHelper.Lerp(generationParams.MinWidth, generationParams.MaxWidth, sizeFactor);

            Size = new Point(
                (int)MathUtils.Round(width, Level.GridCellSize),
                (int)MathUtils.Round(generationParams.Height, Level.GridCellSize));
        }

        public LevelData(XElement element)
        {
            Seed = element.GetAttributeString("seed", "");
            Difficulty = element.GetAttributeFloat("difficulty", 0.0f);
            Size = element.GetAttributePoint("size", new Point(1000));
            Enum.TryParse(element.GetAttributeString("type", "LocationConnection"), out Type);

            string generationParamsId = element.GetAttributeString("generationparams", "");
            GenerationParams = LevelGenerationParams.LevelParams.Find(l => l.Identifier == generationParamsId);
            if (GenerationParams == null)
            {
                DebugConsole.ThrowError($"Error while loading a level. Could not find level generation params with the ID \"{generationParamsId}\".");
                GenerationParams = LevelGenerationParams.LevelParams.FirstOrDefault(l => l.Type == Type);
                if (GenerationParams == null)
                {
                    GenerationParams = LevelGenerationParams.LevelParams.First();
                }
            }

            string biomeIdentifier = element.GetAttributeString("biome", "");
            Biome = LevelGenerationParams.GetBiomes().FirstOrDefault(b => b.Identifier == biomeIdentifier);
            if (Biome == null)
            {
                DebugConsole.ThrowError($"Error in level data: could not find the biome \"{biomeIdentifier}\".");
                Biome = LevelGenerationParams.GetBiomes().First();
            }

            string[] prefabNames = element.GetAttributeStringArray("eventhistory", new string[] { });
            EventHistory.AddRange(EventSet.PrefabList.Where(p => prefabNames.Any(n => p.Identifier.Equals(n, StringComparison.InvariantCultureIgnoreCase))));

            string[] nonRepeatablePrefabNames = element.GetAttributeStringArray("nonrepeatableevents", new string[] { });
            NonRepeatableEvents.AddRange(EventSet.PrefabList.Where(p => prefabNames.Any(n => p.Identifier.Equals(n, StringComparison.InvariantCultureIgnoreCase))));

        }


        /// <summary>
        /// Instantiates level data using the properties of the connection (seed, size, difficulty)
        /// </summary>
        public LevelData(LocationConnection locationConnection)
        {
            Seed = locationConnection.Locations[0].BaseName + locationConnection.Locations[1].BaseName;
            Biome = locationConnection.Biome;
            Type = LevelType.LocationConnection;
            GenerationParams = LevelGenerationParams.GetRandom(Seed, LevelType.LocationConnection, Biome);
            Difficulty = locationConnection.Difficulty;

            float sizeFactor = MathUtils.InverseLerp(
                MapGenerationParams.Instance.SmallLevelConnectionLength,
                MapGenerationParams.Instance.LargeLevelConnectionLength,
                locationConnection.Length);
            int width = (int)MathHelper.Lerp(GenerationParams.MinWidth, GenerationParams.MaxWidth, sizeFactor);
            Size = new Point(
                (int)MathUtils.Round(width, Level.GridCellSize),
                (int)MathUtils.Round(GenerationParams.Height, Level.GridCellSize));
        }

        /// <summary>
        /// Instantiates level data using the properties of the location
        /// </summary>
        public LevelData(Location location)
        {
            Seed = location.BaseName;
            Biome = location.Biome;
            Type = LevelType.Outpost;
            GenerationParams = LevelGenerationParams.GetRandom(Seed, LevelType.Outpost, Biome);
            Difficulty = 0.0f;

            var rand = new MTRandom(ToolBox.StringToInt(Seed));
            int width = (int)MathHelper.Lerp(GenerationParams.MinWidth, GenerationParams.MaxWidth, (float)rand.NextDouble());
            Size = new Point(
                (int)MathUtils.Round(width, Level.GridCellSize),
                (int)MathUtils.Round(GenerationParams.Height, Level.GridCellSize));
        }

        public static LevelData CreateRandom(string seed = "", float? difficulty = null, LevelGenerationParams generationParams = null)
        {
            if (string.IsNullOrEmpty(seed))
            {
                seed = Rand.Range(0, int.MaxValue, Rand.RandSync.Server).ToString();
            }

            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            LevelType type = generationParams == null ? LevelData.LevelType.LocationConnection : generationParams.Type;

            if (generationParams == null) { generationParams = LevelGenerationParams.GetRandom(seed, type); }
            var biome = 
                LevelGenerationParams.GetBiomes().FirstOrDefault(b => generationParams.AllowedBiomes.Contains(b)) ?? 
                LevelGenerationParams.GetBiomes().GetRandom(Rand.RandSync.Server);

            return new LevelData(
                seed,
                difficulty ?? Rand.Range(30.0f, 80.0f, Rand.RandSync.Server),
                Rand.Range(0.0f, 1.0f, Rand.RandSync.Server),
                generationParams,
                biome);
        }

        public void Save(XElement parentElement)
        {
            var newElement = new XElement("Level",
                    new XAttribute("seed", Seed),
                    new XAttribute("biome", Biome.Identifier),
                    new XAttribute("type", Type.ToString()),
                    new XAttribute("difficulty", Difficulty.ToString("G", CultureInfo.InvariantCulture)),
                    new XAttribute("size", XMLExtensions.PointToString(Size)),
                    new XAttribute("generationparams", GenerationParams.Identifier));

            if (Type == LevelType.Outpost)
            {
                if (EventHistory.Any())
                {
                    newElement.Add(new XAttribute("eventhistory", string.Join(',', EventHistory.Select(p => p.Identifier))));
                }
                if (NonRepeatableEvents.Any())
                {
                    newElement.Add(new XAttribute("nonrepeatableevents", string.Join(',', NonRepeatableEvents.Select(p => p.Identifier))));
                }
            }
            parentElement.Add(newElement);
        }
    }
}