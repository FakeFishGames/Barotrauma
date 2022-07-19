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
            }
        }

        public void DrawElectricity(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Length <= 1.0f) continue;
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
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Length <= 1.0f) continue;
                    GUI.DrawRectangle(spriteBatch, new Vector2(nodes[i].WorldPosition.X, -nodes[i].WorldPosition.Y), Vector2.One * 5, Color.LightCyan, isFilled: true);
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
