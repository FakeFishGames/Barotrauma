using Barotrauma.Networking;
using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Projectile : ItemComponent
    {
        private readonly List<ParticleEmitter> particleEmitters = new List<ParticleEmitter>();

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            bool launch = msg.ReadBoolean();
            if (launch)
            {
                ushort userId = msg.ReadUInt16();
                User = Entity.FindEntityByID(userId) as Character;
                Vector2 simPosition = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                float rotation = msg.ReadSingle();
                spreadIndex = msg.ReadByte();
                ushort submarineID = msg.ReadUInt16();
                if (User != null)
                {
                    Shoot(User, simPosition, simPosition, rotation, ignoredBodies: User.AnimController.Limbs.Where(l => !l.IsSevered).Select(l => l.body.FarseerBody).ToList(), createNetworkEvent: false);
                    item.Submarine = Entity.FindEntityByID(submarineID) as Submarine;
                }
                else
                {
                    Launch(User, simPosition, rotation);
                }
            }

            bool isStuck = msg.ReadBoolean();
            if (isStuck)
            {
                ushort submarineID = msg.ReadUInt16();
                ushort hullID = msg.ReadUInt16();
                Vector2 simPosition = new Vector2(
                    msg.ReadSingle(), 
                    msg.ReadSingle());
                Vector2 axis = new Vector2(
                    msg.ReadSingle(),
                    msg.ReadSingle());
                StickTargetType targetType = (StickTargetType)msg.ReadByte();

                Submarine submarine = Entity.FindEntityByID(submarineID) as Submarine;
                Hull hull = Entity.FindEntityByID(hullID) as Hull;
                item.Submarine = submarine;
                item.CurrentHull = hull;
                item.body.SetTransform(simPosition, item.body.Rotation);

                switch (targetType)
                {
                    case StickTargetType.Structure:
                        UInt16 structureId = msg.ReadUInt16();
                        byte bodyIndex = msg.ReadByte();
                        if (Entity.FindEntityByID(structureId) is Structure structure)
                        {
                            if (bodyIndex == 255) { bodyIndex = 0; }
                            if (bodyIndex >= structure.Bodies.Count)
                            {
                                DebugConsole.ThrowError($"Failed to read a projectile update from the server. Structure body index out of bounds ({bodyIndex}, structure: {structure})");
                                return;
                            }
                            var body = structure.Bodies[bodyIndex];
                            StickToTarget(body, axis);
                        }
                        else
                        {
                            DebugConsole.AddWarning($"\"{item.Prefab.Identifier}\" failed to stick to a structure. Could not find a structure with the ID {structureId}");
                        }
                        break;
                    case StickTargetType.Limb:
                        UInt16 characterId = msg.ReadUInt16();
                        byte limbIndex = msg.ReadByte();
                        if (Entity.FindEntityByID(characterId) is Character character)
                        {
                            if (limbIndex >= character.AnimController.Limbs.Length)
                            {
                                DebugConsole.ThrowError($"Failed to read a projectile update from the server. Limb index out of bounds ({limbIndex}, character: {character})");
                                return;
                            }
                            if (character.Removed) { return; }
                            var limb = character.AnimController.Limbs[limbIndex];
                            StickToTarget(limb.body.FarseerBody, axis);
                        }
                        else
                        {
                            DebugConsole.AddWarning($"\"{this.item.Prefab.Identifier}\" failed to stick to a limb. Could not find a character with the ID {characterId}");
                        }
                        break;
                    case StickTargetType.Item:
                        UInt16 itemID = msg.ReadUInt16();
                        if (Entity.FindEntityByID(itemID) is Item targetItem)
                        {
                            if (targetItem.Removed) { return; }
                            var door = targetItem.GetComponent<Door>();
                            if (door != null)
                            {
                                StickToTarget(door.Body.FarseerBody, axis);
                            }
                            else if (targetItem.body != null)
                            {
                                StickToTarget(targetItem.body.FarseerBody, axis);
                            }
                        }
                        else
                        {
                            DebugConsole.AddWarning($"\"{this.item.Prefab.Identifier}\" failed to stick to an item. Could not find n item with the ID {itemID}");
                        }
                        break;
                    case StickTargetType.Submarine:
                        UInt16 targetSubmarineId = msg.ReadUInt16();
                        if (Entity.FindEntityByID(targetSubmarineId) is Submarine targetSub)
                        {
                            StickToTarget(targetSub.PhysicsBody.FarseerBody, axis);
                        }
                        else
                        {
                            DebugConsole.AddWarning($"\"{item.Prefab.Identifier}\" failed to stick to a submarine. Could not find a structure with the ID {targetSubmarineId}");
                        }
                        break;
                    case StickTargetType.LevelWall:
                        int levelWallIndex = msg.ReadInt32();
                        var allCells = Level.Loaded.GetAllCells();
                        if (levelWallIndex >= 0 && levelWallIndex < allCells.Count)
                        {
                            StickToTarget(allCells[levelWallIndex].Body, axis);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Failed to read a projectile update from the server. Level wall index out of bounds ({levelWallIndex}, wall count: {allCells.Count})");
                        }
                        break;
                }         
            }
            else
            {
                Unstick();
            }
        }

        partial void LaunchProjSpecific(Vector2 startLocation, Vector2 endLocation)
        {
            Vector2 particlePos = item.WorldPosition;
            float rotation = -item.body.Rotation;
            if (item.body.Dir < 0.0f) { rotation += MathHelper.Pi; }

            //if the position is in a sub's local coordinates, convert to world coordinates
            particlePos = ConvertToWorldCoordinates(particlePos);
            //if the start location is in a sub's local coordinates, convert to world coordinates
            startLocation = ConvertToWorldCoordinates(startLocation);
            //same for end location
            endLocation = ConvertToWorldCoordinates(endLocation);

            Tuple<Vector2, Vector2> tracerPoints = new Tuple<Vector2, Vector2>(startLocation, endLocation);
            foreach (ParticleEmitter emitter in particleEmitters)
            {
                emitter.Emit(1.0f, particlePos, hullGuess: null, angle: rotation, particleRotation: rotation, colorMultiplier: emitter.Prefab.Properties.ColorMultiplier, tracerPoints: tracerPoints);
            }

            static Vector2 ConvertToWorldCoordinates(Vector2 position)
            {
                Submarine containing = Submarine.FindContainingInLocalCoordinates(position);
                if (containing != null)
                {
                    position += containing.Position;
                }
                return position;
            }
        }

        partial void InitProjSpecific(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
                        break;
                }
            }
        }
    }
}
