#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    internal class VineSprite
    {
        [Serialize("0,0,0,0", IsPropertySaveable.No)]
        public Rectangle SourceRect { get; private set; }

        [Serialize("0.5,0.5", IsPropertySaveable.No)]
        public Vector2 Origin { get; private set; }

        public Vector2 AbsoluteOrigin;

        public VineSprite(ContentXElement element)
        {
            SerializableProperty.DeserializeProperties(this, element);
            AbsoluteOrigin = new Vector2(SourceRect.Width * Origin.X, SourceRect.Height * Origin.Y);
        }
    }

    internal partial class Growable
    {
        public readonly Dictionary<VineTileType, VineSprite> VineSprites = new Dictionary<VineTileType, VineSprite>();
        public readonly List<Sprite> FlowerSprites = new List<Sprite>();
        public readonly List<Sprite> LeafSprites = new List<Sprite>();

        public Sprite? VineAtlas, DecayAtlas;

        protected override void RemoveComponentSpecific()
        {
            VineAtlas?.Remove();
            DecayAtlas?.Remove();

            foreach (Sprite sprite in FlowerSprites)
            {
                sprite.Remove();
            }

            foreach (Sprite sprite in LeafSprites)
            {
                sprite.Remove();
            }
        }

        public void Draw(SpriteBatch spriteBatch, Planter planter, Vector2 offset, float depth)
        {
            const float zStep = 0.0001f;
            float leafDepth = 0f;

            foreach (VineTile vine in Vines)
            {
                leafDepth += zStep;
                DrawBranch(vine, spriteBatch, planter.Item.DrawPosition + offset, depth, leafDepth);
            }

            if (GameMain.DebugDraw)
            {
                foreach (Rectangle rect in FailedRectangles)
                {
                    Rectangle wRect = rect;
                    wRect.Y = -wRect.Y;
                    wRect.Y -= wRect.Height;
                    GUI.DrawRectangle(spriteBatch, wRect, Color.Red);
                }
            }
        }
        
        private void DrawBranch(VineTile vine, SpriteBatch spriteBatch, Vector2 position, float depth, float leafDepth)
        {
            Vector2 pos = position + vine.Position;
            pos.Y = -pos.Y;

            VineSprite vineSprite = VineSprites[vine.Type];
            Color color = Decayed ? DeadTint : VineTint;

            float layer1 = depth + 0.01f, // flowers
                  layer2 = depth + 0.02f, // decay atlas
                  layer3 = depth + 0.03f; // branches and leaves

            float scale = VineScale * vine.VineStep;

            if (VineAtlas != null && VineAtlas.Loaded)
            {
                spriteBatch.Draw(VineAtlas.Texture, pos + vine.offset, vineSprite.SourceRect, color, 0f, vineSprite.AbsoluteOrigin, scale, SpriteEffects.None, layer3);
            }

            if (DecayAtlas != null && DecayAtlas.Loaded)
            {
                spriteBatch.Draw(DecayAtlas.Texture, pos, vineSprite.SourceRect, vine.HealthColor, 0f, vineSprite.AbsoluteOrigin, scale, SpriteEffects.None, layer2);
            }

            if (vine.FlowerConfig.Variant >= 0 && !Decayed)
            {
                Sprite flowerSprite = FlowerSprites[vine.FlowerConfig.Variant];
                flowerSprite.Draw(spriteBatch, pos, FlowerTint, flowerSprite.Origin, scale: BaseFlowerScale * vine.FlowerConfig.Scale * vine.FlowerStep, rotate: vine.FlowerConfig.Rotation, depth: layer1);
            }

            if (vine.LeafConfig.Variant >= 0)
            {
                Sprite leafSprite = LeafSprites[vine.LeafConfig.Variant];
                leafSprite.Draw(spriteBatch, pos, Decayed ? DeadTint : LeafTint, leafSprite.Origin, scale: BaseLeafScale * vine.LeafConfig.Scale * vine.FlowerStep, rotate: vine.LeafConfig.Rotation, depth: layer3 + leafDepth);
            }
        }

        partial void LoadVines(ContentXElement element)
        {
            ContentPath vineAtlasPath = element.GetAttributeContentPath("vineatlas") ?? ContentPath.Empty;
            ContentPath decayAtlasPath = element.GetAttributeContentPath("decayatlas") ?? ContentPath.Empty;

            if (!vineAtlasPath.IsNullOrEmpty())
            {
                VineAtlas = new Sprite(vineAtlasPath.Value, Rectangle.Empty);
            }

            if (!decayAtlasPath.IsNullOrEmpty())
            {
                DecayAtlas = new Sprite(decayAtlasPath.Value, Rectangle.Empty);
            }

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "vinesprite":
                        VineTileType type = subElement.GetAttributeEnum("type", VineTileType.Stem);
                        VineSprites.Add(type, new VineSprite(subElement));
                        break;
                    case "flowersprite":
                        FlowerSprites.Add(new Sprite(subElement));
                        break;
                    case "leafsprite":
                        LeafSprites.Add(new Sprite(subElement));
                        break;
                }

                flowerVariants = FlowerSprites.Count;
                leafVariants = LeafSprites.Count;
            }

            foreach (VineTileType type in Enum.GetValues(typeof(VineTileType)).Cast<VineTileType>())
            {
                if (!VineSprites.ContainsKey(type))
                {
                    DebugConsole.ThrowError($"Vine sprite missing from {item.Prefab.Identifier}: {type}");
                }
            }
        }

        private readonly object mutex = new object();

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            Health = msg.ReadRangedSingle(0, MaxHealth, 8);
            int startOffset = msg.ReadRangedInteger(-1, MaximumVines);
            if (startOffset > -1)
            {
                int vineCount = msg.ReadRangedInteger(0, VineChunkSize);
                List<VineTile> tiles = new List<VineTile>();
                for (int i = 0; i < vineCount; i++)
                {
                    VineTileType vineType = (VineTileType) msg.ReadRangedInteger(0b0000, 0b1111);
                    int flowerConfig = msg.ReadRangedInteger(0, 0xFFF);
                    int leafConfig = msg.ReadRangedInteger(0, 0xFFF);
                    sbyte posX = (sbyte) msg.ReadByte(), posY = (sbyte) msg.ReadByte();
                    Vector2 pos = new Vector2(posX * VineTile.Size, posY * VineTile.Size);

                    tiles.Add(new VineTile(this, pos, vineType, FoliageConfig.Deserialize(flowerConfig), FoliageConfig.Deserialize(leafConfig)));
                }

                // is this even needed??
                lock (mutex)
                {
                    for (var i = 0; i < vineCount; i++)
                    {
                        int index = i + startOffset;
                        if (index >= Vines.Count)
                        {
                            Vines.Add(tiles[i]);
                            continue;
                        }

                        VineTile oldVine = Vines[index];
                        VineTile newVine = tiles[i];
                        newVine.GrowthStep = oldVine.GrowthStep;
                        Vines[index] = newVine;
                    }
                }
            }

            UpdateBranchHealth();
            ResetPlanterSize();
        }

        private void ResetPlanterSize()
        {
            if (item.ParentInventory is ItemInventory itemInventory && itemInventory.Owner is Item parentItem)
            {
                if (parentItem.GetComponent<Planter>() is { } planter)
                {
                    planter.Item.ResetCachedVisibleSize();
                }
            }
        }

#if DEBUG
        private int seed;

        // Huge bowl of spaghetti
        public void CreateDebugHUD(Planter planter, PlantSlot slot)
        {
            Vector2 relativeSize = new Vector2(0.3f, 0.6f);
            GUIMessageBox msgBox = new GUIMessageBox(item.Name, "", new[] { TextManager.Get("applysettingsbutton") }, relativeSize);

            GUILayoutGroup content = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.85f), msgBox.Content.RectTransform)) { Stretch = true };
            GUINumberInput seedInput = CreateIntEntry("Random Seed", seed, content.RectTransform);
            GUINumberInput vineTileSizeInput = CreateIntEntry("Vine Tile Size (Global)", VineTile.Size, content.RectTransform);
            GUINumberInput[] leafScaleRangeInput = CreateMinMaxEntry("Leaf Scale Range (Global)", new []{ MinLeafScale, MaxLeafScale }, 1.5f, content.RectTransform);
            GUINumberInput[] flowerScaleRangeInput = CreateMinMaxEntry("Flower Scale Range (Global)", new []{ MinFlowerScale, MaxFlowerScale }, 1.5f, content.RectTransform);
            GUINumberInput vineCountInput = CreateIntEntry("Vine Count", MaximumVines, content.RectTransform);
            GUINumberInput vineScaleInput = CreateFloatEntry("Vine Scale", VineScale, content.RectTransform);
            GUINumberInput flowerInput = CreateIntEntry("Flower Quantity", FlowerQuantity, content.RectTransform);
            GUINumberInput flowerScaleInput = CreateFloatEntry("Flower Scale", BaseFlowerScale, content.RectTransform);
            GUINumberInput leafScaleInput = CreateFloatEntry("Leaf Scale", BaseLeafScale, content.RectTransform);
            GUINumberInput leafProbabilityInput = CreateFloatEntry("Leaf Probability", LeafProbability, content.RectTransform);
            GUINumberInput[] leafTintInputs = CreateMinMaxEntry("Leaf Tint", new []{ LeafTint.R / 255f, LeafTint.G / 255f, LeafTint.B / 255f }, 1.0f, content.RectTransform);
            GUINumberInput[] flowerTintInputs = CreateMinMaxEntry("Flower Tint", new []{ FlowerTint.R / 255f, FlowerTint.G / 255f, FlowerTint.B / 255f }, 1.0f, content.RectTransform);
            GUINumberInput[] vineTintInputs = CreateMinMaxEntry("Branch Tint", new []{ VineTint.R / 255f, VineTint.G / 255f, VineTint.B / 255f }, 1.0f, content.RectTransform);

            // Apply
            msgBox.Buttons[0].OnClicked = (button, o) =>
            {
                seed = seedInput.IntValue;
                MaximumVines = vineCountInput.IntValue;
                FlowerQuantity = flowerInput.IntValue;
                BaseFlowerScale = flowerScaleInput.FloatValue;
                VineScale = vineScaleInput.FloatValue;
                BaseLeafScale = leafScaleInput.FloatValue;
                LeafProbability = leafProbabilityInput.FloatValue;
                VineTile.Size = vineTileSizeInput.IntValue;

                MinFlowerScale = flowerScaleRangeInput[0].FloatValue;
                MaxFlowerScale = flowerScaleRangeInput[1].FloatValue;
                MinLeafScale = leafScaleRangeInput[0].FloatValue;
                MaxLeafScale = leafScaleRangeInput[1].FloatValue;

                LeafTint = new Color(leafTintInputs[0].FloatValue, leafTintInputs[1].FloatValue, leafTintInputs[2].FloatValue);
                FlowerTint = new Color(flowerTintInputs[0].FloatValue, flowerTintInputs[1].FloatValue, flowerTintInputs[2].FloatValue);
                VineTint = new Color(vineTintInputs[0].FloatValue, vineTintInputs[1].FloatValue, vineTintInputs[2].FloatValue);

                if (FlowerQuantity >= MaximumVines - 1)
                {
                    vineCountInput.Flash(Color.Red);
                    flowerInput.Flash(Color.Red);
                    return false;
                }

                if (MinFlowerScale > MaxFlowerScale)
                {
                    foreach (GUINumberInput input in flowerScaleRangeInput)
                    {
                        input.Flash(Color.Red);
                    }

                    return false;
                }

                if (MinLeafScale > MaxLeafScale)
                {
                    foreach (GUINumberInput input in leafScaleRangeInput)
                    {
                        input.Flash(Color.Red);
                    }

                    return false;
                }

                msgBox.Close();

                Random random = new Random(seed);
                Random flowerRandom = new Random(seed);
                Vines.Clear();
                GenerateFlowerTiles(flowerRandom);
                GenerateStem();

                Decayed = false;
                FullyGrown = false;
                while (MaximumVines > Vines.Count)
                {
                    if (!CanGrowMore())
                    {
                        Decayed = true;
                        break;
                    }

                    TryGenerateBranches(planter, slot, random, flowerRandom);
                }

                if (!Decayed)
                {
                    FullyGrown = true;
                }

                foreach (VineTile vineTile in Vines)
                {
                    vineTile.GrowthStep = 2.0f;
                }

                return true;
            };
        }

        private static GUINumberInput CreateIntEntry(string label, int defaultValue, RectTransform parent)
        {
            GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f), parent), isHorizontal: true);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), layout.RectTransform), label);
            GUINumberInput input = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1f), layout.RectTransform), GUINumberInput.NumberType.Int) { IntValue = defaultValue };
            return input;
        }

        private static GUINumberInput CreateFloatEntry(string label, float defaultValue, RectTransform parent)
        {
            GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f), parent), isHorizontal: true);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), layout.RectTransform), label);
            GUINumberInput input = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1f), layout.RectTransform), GUINumberInput.NumberType.Float) { FloatValue = defaultValue, DecimalsToDisplay = 2 };
            return input;
        }

        private static GUINumberInput[] CreateMinMaxEntry(string label, float[] values, float max, RectTransform parent, float min = 0f)
        {
            GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f), parent), isHorizontal: true);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), layout.RectTransform), label);
            GUINumberInput[] inputs = new GUINumberInput[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                float value = values[i];
                GUINumberInput input = new GUINumberInput(new RectTransform(new Vector2(0.5f / values.Length, 1f), layout.RectTransform), GUINumberInput.NumberType.Float)
                {
                    FloatValue = value, DecimalsToDisplay = 2,
                    MinValueFloat = min,
                    MaxValueFloat = max
                };
                inputs[i] = input;
            }

            return inputs;
        }
#endif
    }
}