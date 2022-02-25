#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.MapCreatures.Behavior
{
    partial class BallastFloraBehavior
    {
        public Sprite? branchAtlas, decayAtlas;
        public readonly Dictionary<VineTileType, VineSprite> BranchSprites = new Dictionary<VineTileType, VineSprite>();
        public readonly List<Sprite> FlowerSprites = new List<Sprite>(), DamagedFlowerSprites = new List<Sprite>();
        public readonly List<Sprite> HiddenFlowerSprites = new List<Sprite>();
        public readonly List<Sprite> LeafSprites = new List<Sprite>(), DamagedLeafSprites = new List<Sprite>();

        public readonly List<ParticleEmitter> DamageParticles = new List<ParticleEmitter>();
        public readonly List<ParticleEmitter> DeathParticles = new List<ParticleEmitter>();

        public static bool AlwaysShowBallastFloraSprite = false;

        partial void LoadPrefab(ContentXElement element)
        {
            if (element.GetAttributeContentPath("branchatlas") is { } branchAtlasPath)
            {
                branchAtlas = new Sprite(branchAtlasPath.Value, Rectangle.Empty);
            }

            if (element.GetAttributeContentPath("decayatlas") is { } decayAtlasPath)
            {
                decayAtlas = new Sprite(decayAtlasPath.Value, Rectangle.Empty);
            }

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "branchsprite":
                        var type = subElement.GetAttributeEnum("type", VineTileType.Stem);
                        BranchSprites.Add(type, new VineSprite(subElement));
                        break;
                    case "flowersprite":
                        FlowerSprites.Add(new Sprite(subElement));
                        break;
                    case "damagedflowersprite":
                        DamagedFlowerSprites.Add(new Sprite(subElement));
                        break;
                    case "hiddenflowersprite":
                        HiddenFlowerSprites.Add(new Sprite(subElement));
                        break;
                    case "leafsprite":
                        LeafSprites.Add(new Sprite(subElement));
                        break;
                    case "damagedleafsprite":
                        DamagedLeafSprites.Add(new Sprite(subElement));
                        break;
                    case "damageparticle":
                        DamageParticles.Add(new ParticleEmitter(subElement));
                        break;
                    case "deathparticle":
                        DeathParticles.Add(new ParticleEmitter(subElement));
                        break;
                    case "targets":
                        LoadTargets(subElement);
                        break;
                }

                flowerVariants = FlowerSprites.Count;
                leafVariants = LeafSprites.Count;
            }
        }

        private void CreateShapnel(Vector2 pos)
        {
            float particleAmount = Rand.Range(16, 32);
            for (int i = 0; i < particleAmount; i++)
            {
                GameMain.ParticleManager.CreateParticle("shrapnel", pos, Rand.Vector(Rand.Range(0f, 250.0f)), Rand.Range(0f, 360.0f));
            }
        }

        partial void UpdateDamage(float deltaTime)
        {  
            foreach (BallastFloraBranch branch in Branches)
            {
                if (branch.AccumulatedDamage > 0)
                {
                    CreateDamageParticle(branch, branch.AccumulatedDamage);

                    if (GameMain.DebugDraw)
                    {
                        var pos = (Parent?.Position ?? Vector2.Zero) + Offset + branch.Position;
                        GUI.AddMessage($"{(int)branch.AccumulatedDamage}", GUIStyle.Red, pos, Vector2.UnitY * 10.0f, 3f, playSound: false, subId: Parent?.Submarine?.ID ?? -1);
                    }
                }
                if (Character.Controlled != null && Character.Controlled.CurrentHull == branch.CurrentHull &&
                    branch.IsRoot && 
                    (branch.AccumulatedDamage > 0.0f || branch.AccumulatedDamage < -0.1f))
                {
                    Character.Controlled.UpdateHUDProgressBar(this, 
                        GetWorldPosition() + branch.Position, 
                        branch.Health / branch.MaxHealth, 
                        emptyColor: GUIStyle.HealthBarColorLow,
                        fullColor: GUIStyle.HealthBarColorHigh,
                        textTag: Prefab.DisplayName.Value);
                }
                branch.AccumulatedDamage = 0f;
                if (branch.DamageVisualizationTimer > 0.0f)
                {
                    branch.DamageVisualizationTimer -= deltaTime;
                    float t1 = (float)Timing.TotalTime * 0.2f + branch.Position.X / 100.0f;
                    float t2 = (float)Timing.TotalTime * 0.5f + branch.Position.Y / 100.0f;
                    branch.ShakeAmount = new Vector2(
                        PerlinNoise.GetPerlin(t1, t2) - 0.5f,
                        PerlinNoise.GetPerlin(t2, t1) - 0.5f) * 10.0f * branch.DamageVisualizationTimer;
                }
            }
        }

        private void CreateDamageParticle(BallastFloraBranch branch, float deltaTime)
        {
            Vector2 pos = GetWorldPosition() + branch.Position;
            foreach (var particleEmitter in DamageParticles)
            {
                particleEmitter.Emit(deltaTime, pos, branch.CurrentHull);
            }            
        }

        private void CreateDeathParticle(BallastFloraBranch branch, float deltaTime)
        {
            Vector2 pos = GetWorldPosition() + branch.Position;
            foreach (var particleEmitter in DeathParticles)
            {
                particleEmitter.Emit(deltaTime, pos, branch.CurrentHull);
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            const float zStep = 0.000001f;
            float leafDepth = zStep;
            float flowerDepth = zStep;

            if (GameMain.DebugDraw)
            {
                foreach (Body body in bodies)
                {
                    Vector2 pos = Parent.Submarine.DrawPosition + ConvertUnits.ToDisplayUnits(body.Position);
                    pos.Y = -pos.Y;
                    GUI.DrawRectangle(spriteBatch, pos, 32f, 32f, 0f, body.UserData is BallastFloraBranch { IsRoot: true } ? Color.Magenta : Color.Cyan, 0.1f, thickness: 1);
                }

                foreach (var (key, steps) in IgnoredTargets)
                {
                    string label = $"Ignored \"{key.Name}\" for {steps} steps";
                    var (sizeX, sizeY) = GUIStyle.SubHeadingFont.MeasureString(label);
                    Vector2 targetPos = key.WorldPosition;
                    targetPos.Y = -targetPos.Y;
                    GUI.DrawString(spriteBatch, targetPos - new Vector2(sizeX / 2f, sizeY), label, GUIStyle.Red, font: GUIStyle.SubHeadingFont);
                }
            }

            foreach (BallastFloraBranch branch in Branches)
            {
                Vector2 pos = Parent.DrawPosition + Offset + branch.Position + branch.ShakeAmount;
                pos.Y = -pos.Y;

                float depth = branch.IsRootGrowth ? 0.2f : BranchDepth;

                float layer1 = depth + 0.01f,
                      layer2 = depth + 0.02f,
                      layer3 = depth + 0.03f;

                VineSprite branchSprite = BranchSprites[branch.Type];

                Color branchColor = (branch.IsRoot || branch.IsRootGrowth) ? RootColor : Color.White;

                if (GameMain.DebugDraw)
                {
#if DEBUG
                    Vector2 basePos = Parent.WorldPosition;
                    foreach (var (from, to) in debugSearchLines)
                    {
                        Vector2 pos1 = basePos - from;
                        pos1.Y = -pos1.Y;
                        Vector2 pos2 = basePos - to;
                        pos2.Y = -pos2.Y;
                        GUI.DrawLine(spriteBatch, pos1, pos2, GUIStyle.Yellow * 0.8f, width: 4);
                    }
                    if (branch.ParentBranch != null)
                    {
                        Vector2 pos2 = Parent.DrawPosition + Offset + branch.ParentBranch.Position;
                        pos2.Y = -pos2.Y;
                        GUI.DrawLine(spriteBatch, pos, pos2, GUIStyle.Green * 0.8f, width: 3);
                    }
#endif

                    string label = "";

                    if (branch == Branches[^1])
                    {
                        label += $"Current State: {StateMachine.State?.GetType().Name ?? "null!"}\n";
                    }

                    if (StateMachine.State is GrowToTargetState targetState)
                    {
                        if (targetState.TargetBranches.Contains(branch))
                        {
                            GUI.DrawRectangle(spriteBatch, pos, branch.Rect.Width, branch.Rect.Height, 0f, Color.Red, thickness: 4);
                        }

                        if (targetState.TargetBranches[^1] == branch)
                        {
                            label += $"Target: {targetState.Target.Name}\n";

                            Vector2 targetPos = targetState.Target.WorldPosition;
                            targetPos.Y = -targetPos.Y;
                            GUI.DrawLine(spriteBatch, pos, targetPos, Color.Red, width: 4);
                        }
                    }

                    var (sizeX, sizeY) = GUIStyle.SubHeadingFont.MeasureString(label);
                    GUI.DrawString(spriteBatch, pos - new Vector2(sizeX / 2f, branch.Rect.Height + sizeY), label, Color.White, font: GUIStyle.SubHeadingFont);
                }

                bool isDamaged = branch.Health < branch.MaxHealth;

                if (HasBrokenThrough)
                {
                    if (branchAtlas != null && branchAtlas.Loaded)
                    {
                        spriteBatch.Draw(branchAtlas.Texture, pos + branch.offset, branchSprite.SourceRect, branchColor, 0f, branchSprite.AbsoluteOrigin, BaseBranchScale * branch.VineStep, SpriteEffects.None, layer2);
                    }

                    if (decayAtlas != null && isDamaged && decayAtlas.Loaded)
                    {
                        spriteBatch.Draw(decayAtlas.Texture, pos + branch.offset, branchSprite.SourceRect, branch.HealthColor, 0f, branchSprite.AbsoluteOrigin, BaseBranchScale * branch.VineStep, SpriteEffects.None, layer2 - zStep);
                    }
                }

                if (branch.FlowerConfig.Variant >= 0)
                {
                    int variant = branch.FlowerConfig.Variant;
                    Sprite flowerSprite = HasBrokenThrough ? FlowerSprites[variant] : HiddenFlowerSprites[variant];
                    float flowerScale = BaseFlowerScale * branch.FlowerConfig.Scale * branch.FlowerStep;

                    if (HasBrokenThrough) { flowerScale *= branch.Pulse; }

                    flowerSprite.Draw(spriteBatch, pos, branchColor, flowerSprite.Origin, scale: flowerScale, rotate: branch.FlowerConfig.Rotation, depth: layer1 - flowerDepth);
                    if (isDamaged && HasBrokenThrough && DamagedFlowerSprites.Count > variant)
                    {
                        DamagedFlowerSprites[variant].Draw(spriteBatch, pos, branch.HealthColor, flowerSprite.Origin, scale: flowerScale, rotate: branch.FlowerConfig.Rotation, depth: layer1 - flowerDepth - zStep);
                    }
                    flowerDepth -= zStep;
                    if (flowerDepth > 0.01f)
                    {
                        flowerDepth = zStep;
                    }
                }

                if (branch.LeafConfig.Variant >= 0 && HasBrokenThrough)
                {
                    int variant = branch.LeafConfig.Variant;
                    Sprite leafSprite = LeafSprites[variant];
                    leafSprite.Draw(spriteBatch, pos, branchColor, leafSprite.Origin, scale: BaseLeafScale * branch.LeafConfig.Scale * branch.FlowerStep, rotate: branch.LeafConfig.Rotation, depth: layer3 + leafDepth);
                    if (isDamaged && DamagedLeafSprites.Count > variant)
                    {
                        DamagedLeafSprites[variant].Draw(spriteBatch, pos, branch.HealthColor, leafSprite.Origin, scale: BaseLeafScale * branch.LeafConfig.Scale * branch.FlowerStep, rotate: branch.LeafConfig.Rotation, depth: layer3 + leafDepth - zStep);
                    }
                    leafDepth += zStep;
                    if (leafDepth > 0.01f)
                    {
                        flowerDepth = zStep;
                    }
                }
            }
        }


        public void ClientRead(IReadMessage msg, NetworkHeader header)
        {
            switch (header)
            {
                case NetworkHeader.Infect:
                    int infectBranch = -1;
                    ushort itemId = msg.ReadUInt16();
                    bool infect = msg.ReadBoolean();
                    if (infect)
                    {
                        infectBranch = msg.ReadInt32();
                    }

                    Entity? entity = Entity.FindEntityByID(itemId);
                    if (entity is Item item)
                    {
                        if (infect)
                        {
                            ClaimTarget(item, Branches.FirstOrDefault(b => b.ID == infectBranch));
                        }
                        else
                        {
                            RemoveClaim(item);
                        }
                    }
                    else
                    {
                        DebugConsole.AddWarning($"Received Infect.{infect} Network Header with invalid item ID: {itemId}, which belongs to {entity?.ToString() ?? "null!"}");
                    }
                    break;
                case NetworkHeader.BranchCreate:
                    int parentId = msg.ReadInt32();
                    BallastFloraBranch branch = ReadBranch(msg);
                    BallastFloraBranch? parent = Branches.FirstOrDefault(b => b.ID == parentId);

                    if (parent == null)
                    {
                        DebugConsole.AddWarning($"Received BranchCreate with an invalid parent ID: {parentId}, Maximum ID is {Branches.Max(b => b.ID)}");
                    }

                    UpdateConnections(branch, parent);
                    Branches.Add(branch);
                    OnBranchGrowthSuccess(branch);
                    break;
                case NetworkHeader.BranchRemove:

                    int removedBranchId = msg.ReadInt32();
                    BallastFloraBranch? removedBranch = Branches.FirstOrDefault(b => b.ID == removedBranchId);
                    if (removedBranch != null)
                    {
                        RemoveBranch(removedBranch);
                    }
                    else
                    {
                        DebugConsole.AddWarning($"Received BranchRemove for a branch that doesn't exist. ID: {removedBranchId}, Maximum ID is {Branches.Max(b => b.ID)}");
                    }
                    
                    break;
                case NetworkHeader.BranchDamage:
                    int damageBranchId = msg.ReadInt32();
                    float health = msg.ReadSingle();
                    BallastFloraBranch? damagedBranch = Branches.FirstOrDefault(b => b.ID == damageBranchId);
                    if (damagedBranch != null)
                    {
                        damagedBranch.Health = health;
                    }
                    else
                    {
                        DebugConsole.AddWarning($"Received BranchDamage for a branch that doesn't exist. ID: {damageBranchId}, Maximum ID is {Branches.Max(b => b.ID)}");
                    }
                    break;
                case NetworkHeader.Kill:
                    Kill();
                    break;
                case NetworkHeader.Remove:
                    Remove();
                    break;
            }

            PowerConsumptionTimer = msg.ReadSingle();
        }

        private BallastFloraBranch ReadBranch(IReadMessage msg)
        {
            int id = msg.ReadInt32();
            byte type = (byte)msg.ReadRangedInteger(0b0000, 0b1111);
            byte sides = (byte)msg.ReadRangedInteger(0b0000, 0b1111);
            int flowerConfig = msg.ReadRangedInteger(0, 0xFFF);
            int leafConfig = msg.ReadRangedInteger(0, 0xFFF);
            int maxHealth = msg.ReadUInt16();
            int posX = msg.ReadInt32(), posY = msg.ReadInt32();
            int parentBranchIndex = msg.ReadInt32();
            Vector2 pos = new Vector2(posX * VineTile.Size, posY * VineTile.Size);

            BallastFloraBranch? parentBranch = parentBranchIndex < 0 || parentBranchIndex >= Branches.Count ? null : Branches[parentBranchIndex];

            return new BallastFloraBranch(this, parentBranch, pos, (VineTileType)type, FoliageConfig.Deserialize(flowerConfig), FoliageConfig.Deserialize(leafConfig))
            {
                ID = id,
                MaxHealth = maxHealth,
                Sides = (TileSide) sides
            };
        }
    }
}