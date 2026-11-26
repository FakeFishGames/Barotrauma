#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    public class VineSprite
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

    public partial class Growable
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
            float branchDepth = 0f;

            offset += DrawOffset;

            foreach (VineTile vine in Vines)
            {
                //newer leaves render in front of older ones
                branchDepth -= zStep;
                DrawBranch(vine, spriteBatch, planter.Item.DrawPosition + offset, depth, branchDepth);
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

        /// <param name="depth">"Base depth" of the sprites; modified to get different parts of the branch to render in front</param>
        /// <param name="perBranchDepth">Small per-branch offset to prevent z-fighting between different branches</param>
        private void DrawBranch(VineTile vine, SpriteBatch spriteBatch, Vector2 position, float depth, float perBranchDepth)
        {
            Vector2 pos = position + vine.Position;
            pos.Y = -pos.Y;

            VineSprite vineSprite = VineSprites[vine.Type];
            Color color = Decayed ? DeadTint : VineTint;

            float layer1 = depth + 0.01f, // flowers
                  layer2 = depth + 0.02f, // decay atlas
                  layer3 = depth + 0.03f, // leaves
                  layer4 = depth + 0.04f; // branches

            float scale = VineScale * vine.VineStep;

            if (VineAtlas != null && VineAtlas.Loaded)
            {
                spriteBatch.Draw(VineAtlas.Texture, pos + vine.offset, vineSprite.SourceRect, color, 0f, vineSprite.AbsoluteOrigin, scale, SpriteEffects.None, layer4 + perBranchDepth);
            }

            if (DecayAtlas != null && DecayAtlas.Loaded)
            {
                spriteBatch.Draw(DecayAtlas.Texture, pos, vineSprite.SourceRect, vine.HealthColor, 0f, vineSprite.AbsoluteOrigin, scale, SpriteEffects.None, layer2 + perBranchDepth);
            }

            if (vine.FlowerConfig.Variant >= 0 && !Decayed && FlowerSprites.Any())
            {
                Sprite flowerSprite = FlowerSprites[vine.FlowerConfig.Variant];
                flowerSprite.Draw(spriteBatch, pos, FlowerTint, flowerSprite.Origin, scale: BaseFlowerScale * vine.FlowerConfig.Scale * vine.FlowerStep, rotate: vine.FlowerConfig.RotationRad, depth: layer1 + perBranchDepth);
            }

            if (vine.LeafConfig.Variant >= 0 && LeafSprites.Any())
            {
                Sprite leafSprite = LeafSprites[vine.LeafConfig.Variant];
                leafSprite.Draw(spriteBatch, pos, Decayed ? DeadTint : LeafTint, leafSprite.Origin, scale: BaseLeafScale * vine.LeafConfig.Scale * vine.FlowerStep, rotate: vine.LeafConfig.RotationRad, depth: layer3 + perBranchDepth);
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
                //ok to have just the stem sprite if there's no more than one vine
                if (type != VineTileType.Stem && MaximumVines <= 1) { continue; }
                if (!VineSprites.ContainsKey(type))
                {
                    DebugConsole.ThrowError($"Vine sprite missing from {item.Prefab.Identifier}: {type}");
                }
            }
        }

        private readonly object mutex = new object();

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            Health = msg.ReadRangedSingle(0, MaxHealth, 8);
            
            int startOffset = msg.ReadRangedInteger(-1, MaximumVines);
            if (startOffset > -1)
            {
                int vineCount = msg.ReadRangedInteger(0, VineChunkSize);
                bool growImmediately = msg.ReadBoolean();
                List<VineTile> tiles = new List<VineTile>();
                for (int i = 0; i < vineCount; i++)
                {
                    VineTileType vineType = (VineTileType)msg.ReadRangedInteger(0b0000, 0b1111);
                    FoliageConfig flowerConfig = FoliageConfig.ReadFoliageConfig(rotationMin: MinFlowerRotation, rotationMax: MaxFlowerRotation, msg);
                    FoliageConfig leafConfig = FoliageConfig.ReadFoliageConfig(rotationMin: MinLeafRotation, rotationMax: MaxLeafRotation, msg);
                    sbyte posX = (sbyte)msg.ReadByte(), posY = (sbyte)msg.ReadByte();
                    Vector2 pos = new Vector2(posX * VineTile.Size, posY * VineTile.Size);

                    tiles.Add(new VineTile(this, pos, vineType, flowerConfig, leafConfig));
                }

                // is this even needed??
                lock (mutex)
                {
                    for (var i = 0; i < vineCount; i++)
                    {
                        int index = i + startOffset;
                        if (index >= Vines.Count)
                        {
                            // Only the newly added vines, the existing ones keep their visual growth
                            if (growImmediately) { tiles[i].GrowthStep = VineTile.FullyGrownWithFlowers; }
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

            UpdateMaxHealth();
            UpdateVineHealthVisuals();
            ResetPlanterSize();
        }

        private void ResetPlanterSize()
        {
            GetPlanter()?.Item.ResetCachedVisibleSize();
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
            GUINumberInput vineTileSizeInput = CreateIntEntry("Vine Tile Size", VineTileSize, content.RectTransform);
            GUINumberInput[] leafScaleRangeInput = CreateMinMaxEntry("Leaf Scale Range", new []{ MinLeafScale, MaxLeafScale }, 2.0f, content.RectTransform);
            GUINumberInput[] flowerScaleRangeInput = CreateMinMaxEntry("Flower Scale Range", new []{ MinFlowerScale, MaxFlowerScale }, 2.0f, content.RectTransform);
            GUINumberInput[] leafRotationRangeInput = CreateMinMaxEntry("Leaf Rotation Range", new []{ MinLeafRotation, MaxLeafRotation }, 360f, content.RectTransform, min: -360f);
            GUINumberInput[] flowerRotationRangeInput = CreateMinMaxEntry("Flower Rotation Range", new []{ MinFlowerRotation, MaxFlowerRotation }, max: 360f, content.RectTransform, min: -360f);
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
                VineTileSize = vineTileSizeInput.IntValue;

                MinFlowerScale = flowerScaleRangeInput[0].FloatValue;
                MaxFlowerScale = flowerScaleRangeInput[1].FloatValue;
                MinLeafScale = leafScaleRangeInput[0].FloatValue;
                MaxLeafScale = leafScaleRangeInput[1].FloatValue;
                MinFlowerRotation = flowerRotationRangeInput[0].FloatValue;
                MaxFlowerRotation = flowerRotationRangeInput[1].FloatValue;
                MinLeafRotation = leafRotationRangeInput[0].FloatValue;
                MaxLeafRotation = leafRotationRangeInput[1].FloatValue;

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

                if (MinFlowerRotation > MaxFlowerRotation)
                {
                    foreach (GUINumberInput input in flowerRotationRangeInput)
                    {
                        input.Flash(Color.Red);
                    }

                    return false;
                }

                if (MinLeafRotation > MaxLeafRotation)
                {
                    foreach (GUINumberInput input in leafRotationRangeInput)
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
                    vineTile.GrowthStep = VineTile.FullyGrownWithFlowers;
                }
                //make sure the health reflects the new size of the plant after we've "artificially" modified the size
                RestoreFullHealth();

                return true;
            };
        }

        private static GUINumberInput CreateIntEntry(string label, int defaultValue, RectTransform parent)
        {
            GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f), parent), isHorizontal: true);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), layout.RectTransform), label);
            GUINumberInput input = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1f), layout.RectTransform), NumberType.Int) { IntValue = defaultValue };
            return input;
        }

        private static GUINumberInput CreateFloatEntry(string label, float defaultValue, RectTransform parent)
        {
            GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f), parent), isHorizontal: true);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), layout.RectTransform), label);
            GUINumberInput input = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1f), layout.RectTransform), NumberType.Float) { FloatValue = defaultValue, DecimalsToDisplay = 2 };
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
                GUINumberInput input = new GUINumberInput(new RectTransform(new Vector2(0.5f / values.Length, 1f), layout.RectTransform), NumberType.Float)
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