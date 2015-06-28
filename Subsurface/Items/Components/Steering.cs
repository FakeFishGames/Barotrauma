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

            item.SendSignal(targetVelocity.X.ToString(), "velocity_x_out", item);
            item.SendSignal(targetVelocity.Y.ToString(), "velocity_y_out", item);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            //isActive = true;

            int width = 300, height = 300;
            int x = Game1.GraphicsWidth / 2 - width / 2;
            int y = Game1.GraphicsHeight / 2 - height / 2 - 50;

            GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black, true);

            Rectangle velRect = new Rectangle(x+20, y+20, 100, 100);
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

            //spriteBatch.DrawString(GUI.font, "Force: " + (int)force + " %", new Vector2(x + 30, y + 30), Color.White);

            //if (GUI.DrawButton(spriteBatch, new Rectangle(x + 280, y + 30, 40, 40), "+", true)) targetForce += 1.0f;
            //if (GUI.DrawButton(spriteBatch, new Rectangle(x + 280, y + 80, 40, 40), "-", true)) targetForce -= 1.0f;

            item.NewComponentEvent(this, true);
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender)
        {
            if (connection.name == "velocity_in")
            {
                currVelocity = ToolBox.ParseToVector2(signal, false);
            }  
        }
    }
}
