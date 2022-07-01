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
                if (User != null)
                {
                    Shoot(User, simPosition, simPosition, rotation, ignoredBodies: User.AnimController.Limbs.Where(l => !l.IsSevered).Select(l => l.body.FarseerBody).ToList(), createNetworkEvent: false);
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
                UInt16 entityID = msg.ReadUInt16();

                Entity entity       = Entity.FindEntityByID(entityID);
                Submarine submarine = Entity.FindEntityByID(submarineID) as Submarine;
                Hull hull           = Entity.FindEntityByID(hullID) as Hull;
                item.Submarine = submarine;
                item.CurrentHull = hull;
                item.body.SetTransform(simPosition, item.body.Rotation);
                if (entity is Character character)
                {
                    byte limbIndex = msg.ReadByte();
                    if (limbIndex >= character.AnimController.Limbs.Length)
                    {
                        DebugConsole.ThrowError($"Failed to read a projectile update from the server. Limb index out of bounds ({limbIndex}, character: {character.ToString()})");
                        return;
                    }
                    if (character.Removed) { return; }
                    var limb = character.AnimController.Limbs[limbIndex];
                    StickToTarget(limb.body.FarseerBody, axis);
                }
                else if (entity is Structure structure)
                {
                    byte bodyIndex = msg.ReadByte();
                    if (bodyIndex == 255) { bodyIndex = 0; }
                    if (bodyIndex >= structure.Bodies.Count)
                    {
                        DebugConsole.ThrowError($"Failed to read a projectile update from the server. Structure body index out of bounds ({bodyIndex}, structure: {structure.ToString()})");
                        return;
                    }
                    var body = structure.Bodies[bodyIndex];
                    StickToTarget(body, axis);
                }
                else if (entity is Item item)
                {
                    if (item.Removed) { return; }
                    var door = item.GetComponent<Door>();
                    if (door != null)
                    {
                        StickToTarget(door.Body.FarseerBody, axis);
                    }
                    else if (item.body != null)
                    {
                        StickToTarget(item.body.FarseerBody, axis);
                    }
                }
                else if (entity is  Submarine sub)
                {
                    StickToTarget(sub.PhysicsBody.FarseerBody, axis);
                }
                else
                {
                    DebugConsole.ThrowError($"Failed to read a projectile update from the server. Invalid stick target ({entity?.ToString() ?? "null"}, {entityID})");
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
            Tuple<Vector2, Vector2> tracerPoints = new Tuple<Vector2, Vector2>(startLocation, endLocation);
            foreach (ParticleEmitter emitter in particleEmitters)
            {
                emitter.Emit(1.0f, particlePos, hullGuess: null, angle: rotation, particleRotation: rotation, colorMultiplier: emitter.Prefab.Properties.ColorMultiplier, tracerPoints: tracerPoints);
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
