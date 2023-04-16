using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma.Items.Components
{
    partial class ElectricalDischarger : Powered
    {
        private static SpriteSheet electricitySprite;

        private int frameOffset;

        partial void InitProjSpecific()
        {
            if (electricitySprite == null)
            {
                electricitySprite = new SpriteSheet("Content/Lights/Electricity.png", 4, 4, new Vector2(0.5f, 0.0f));
            }
        }

        partial void DischargeProjSpecific()
        {
            PlaySound(ActionType.OnUse);
            foreach (Node node in nodes)
            {
                GameMain.ParticleManager.CreateParticle("swirlysmoke", node.WorldPosition, Vector2.Zero);

                if (node.ParentIndex > -1)
                {
                    CreateParticlesBetween(nodes[node.ParentIndex].WorldPosition, node.WorldPosition);
                }
            }
            foreach (var character in charactersInRange)
            {
                CreateParticlesBetween(character.character.WorldPosition, character.node.WorldPosition);
            }

            static void CreateParticlesBetween(Vector2 start, Vector2 end)
            {
                const float ParticleInterval = 50.0f;
                Vector2 diff = end - start;
                float dist = diff.Length();
                Vector2 normalizedDiff = MathUtils.NearlyEqual(dist, 0.0f) ? Vector2.Zero : diff / dist;
                for (float x = 0.0f; x < dist; x += ParticleInterval)
                {
                    var spark = GameMain.ParticleManager.CreateParticle("ElectricShock", start + normalizedDiff * x, Vector2.Zero);
                    if (spark != null)
                    {
                        spark.Size *= 0.3f;
                    }
                }
            }
        }

        public void DrawElectricity(SpriteBatch spriteBatch)
        {
            if (timer <= 0.0f && Screen.Selected is { IsEditor: false }) { return; }
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Length <= 1.0f) { continue; }
                var node = nodes[i];
                electricitySprite.Draw(spriteBatch,
                    (i + frameOffset) % electricitySprite.FrameCount,
                    new Vector2(node.WorldPosition.X, -node.WorldPosition.Y),
                    Color.Lerp(Color.LightBlue, Color.White, Rand.Range(0.0f, 1.0f)),
                    electricitySprite.Origin, -node.Angle - MathHelper.PiOver2,
                    new Vector2(
                        Math.Min(node.Length / electricitySprite.FrameSize.X, 1.0f) * Rand.Range(0.5f, 2.0f), 
                        node.Length / electricitySprite.FrameSize.Y) * Rand.Range(1.0f, 1.2f));
            }

            if (GameMain.DebugDraw)
            {
                for (int i = 1; i < nodes.Count; i++)
                {
                    GUI.DrawLine(spriteBatch, 
                        new Vector2(nodes[i].WorldPosition.X, -nodes[i].WorldPosition.Y), 
                        new Vector2(nodes[nodes[i].ParentIndex].WorldPosition.X, -nodes[nodes[i].ParentIndex].WorldPosition.Y),
                         Color.LightCyan,
                         width: 3);

                    if (nodes[i].Length <= 1.0f) { continue; }
                    GUI.DrawRectangle(spriteBatch, new Vector2(nodes[i].WorldPosition.X, -nodes[i].WorldPosition.Y), Vector2.One * 10, Color.LightCyan, isFilled: true);
                }
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            CurrPowerConsumption = powerConsumption;
            charging = true;
            timer = Duration;
            IsActive = true;
        }
    }
}
