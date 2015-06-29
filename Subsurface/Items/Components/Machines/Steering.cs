using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Steering : ItemComponent
    {
        Vector2 currVelocity;
        Vector2 targetVelocity;

        public Steering(Item item, XElement element)
            : base(item, element)
        {
            isActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            item.SendSignal(targetVelocity.X.ToString(), "velocity_x_out");
            item.SendSignal((-targetVelocity.Y).ToString(), "velocity_y_out");
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            int width = GuiFrame.Rect.Width, height = GuiFrame.Rect.Height;
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            Rectangle velRect = new Rectangle(x + 20, y + 20, width - 40, height - 40);
            GUI.DrawRectangle(spriteBatch, velRect, Color.White, false);

            GUI.DrawLine(spriteBatch,
                new Vector2(velRect.Center.X,velRect.Center.Y), 
                new Vector2(velRect.Center.X + currVelocity.X, velRect.Center.Y - currVelocity.Y), 
                Color.Gray);

            Vector2 targetVelPos = new Vector2(velRect.Center.X + targetVelocity.X, velRect.Center.Y - targetVelocity.Y);

            GUI.DrawLine(spriteBatch,
                new Vector2(velRect.Center.X, velRect.Center.Y),
                targetVelPos,
                Color.LightGray);

            GUI.DrawRectangle(spriteBatch, new Rectangle((int)targetVelPos.X - 5, (int)targetVelPos.Y - 5, 10, 10), Color.White);

            if (Vector2.Distance(PlayerInput.MousePosition, targetVelPos)<10.0f)
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)targetVelPos.X -10, (int)targetVelPos.Y - 10, 20, 20), Color.Red);

                if (PlayerInput.LeftButtonDown())
                {
                    targetVelocity = PlayerInput.MousePosition - new Vector2(velRect.Center.X, velRect.Center.Y);
                    targetVelocity.Y = -targetVelocity.Y;
                }
            }

            item.NewComponentEvent(this, true);
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power=0.0f)
        {
            if (connection.name == "velocity_in")
            {
                currVelocity = ToolBox.ParseToVector2(signal, false);
            }  
        }

        public override void FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetOutgoingMessage message)
        {
            message.Write(targetVelocity.X);
            message.Write(targetVelocity.Y);
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message)
        {
            targetVelocity.X = message.ReadFloat();
            targetVelocity.Y = message.ReadFloat();
        }
    }
}
