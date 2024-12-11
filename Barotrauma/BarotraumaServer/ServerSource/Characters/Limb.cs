using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Limb : ISerializableEntity, ISpatialEntity
    {
        /// <summary>
        /// An invisible "ghost body" used for doing lag compensation server side by allowing clients' shots to hit bodies at the positions where 
        /// they "used to be" back when the client fired a weapon.
        /// </summary>
        public PhysicsBody LagCompensatedBody { get; private set; }

        /// <summary>
        /// A queue of past positions of the limb.
        /// </summary>
        public Queue<PosInfo> MemState { get; } = new Queue<PosInfo>();

        partial void InitProjSpecific(ContentXElement element)
        {
            LagCompensatedBody = new PhysicsBody(Params, findNewContacts: false)
            {
                BodyType = FarseerPhysics.BodyType.Static,
                CollisionCategories = Physics.CollisionLagCompensationBody,
                CollidesWith = Physics.CollisionNone,
                UserData = this
            };
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (GameMain.Server == null) { return; }

            MemState.Enqueue(new PosInfo(body.SimPosition, body.Rotation, body.LinearVelocity, body.AngularVelocity, (float)Timing.TotalTime));

            //clear old states
            while (
                MemState.Any() &&
                MemState.Peek().Timestamp < Timing.TotalTime - GameMain.Server.ServerSettings.MaxLagCompensationSeconds)
            {
                MemState.Dequeue();
            }
        }

        public static void SetLagCompensatedBodyPositions(Client client)
        {
            if (GameMain.Server == null) { return; }
            //convert from milliseconds to seconds, assume latency is symmetrical (time from client to server is half of the roundtrip time / ping)
            float latency = client.Ping / 1000.0f / 2;
            float time = (float)Timing.TotalTime - MathUtils.Min(latency, GameMain.Server.ServerSettings.MaxLagCompensationSeconds);

            foreach (var character in Character.CharacterList)
            {
                foreach (var limb in character.AnimController.Limbs)
                {
                    if (limb.body.Enabled == false || limb.IgnoreCollisions) { continue; }
                    var matchingState = limb.MemState.FirstOrDefault(l => l.Timestamp <= time);
                    if (matchingState == null) { continue; }
                    limb.LagCompensatedBody.SetTransformIgnoreContacts(matchingState.Position, matchingState.Rotation ?? 0.0f);
                }
            }
        }

        partial void RemoveProjSpecific()
        {
            LagCompensatedBody.Remove();
        }
    }
}
