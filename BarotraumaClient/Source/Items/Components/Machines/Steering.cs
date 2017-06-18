using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        private GUITickBox autopilotTickBox, maintainPosTickBox;
        private GUITickBox levelEndTickBox, levelStartTickBox;

        public bool LevelStartSelected
        {
            get { return levelStartTickBox.Selected; }
            set { levelStartTickBox.Selected = value; }
        }

        public bool LevelEndSelected
        {
            get { return levelEndTickBox.Selected; }
            set { levelEndTickBox.Selected = value; }
        }

        public bool MaintainPos
        {
            get { return maintainPosTickBox.Selected; }
            set { maintainPosTickBox.Selected = value; }
        }

        private void InitProjSpecific()
        {
            autopilotTickBox = new GUITickBox(new Rectangle(0, 25, 20, 20), "Autopilot", Alignment.TopLeft, GuiFrame);
            autopilotTickBox.OnSelected = (GUITickBox box) =>
            {
                AutoPilot = box.Selected;
                unsentChanges = true;

                return true;
            };

            maintainPosTickBox = new GUITickBox(new Rectangle(5, 50, 15, 15), "Maintain position", Alignment.TopLeft, GUI.SmallFont, GuiFrame);
            maintainPosTickBox.Enabled = false;
            maintainPosTickBox.OnSelected = ToggleMaintainPosition;

            levelStartTickBox = new GUITickBox(
                new Rectangle(5, 70, 15, 15),
                GameMain.GameSession == null ? "" : ToolBox.LimitString(GameMain.GameSession.StartLocation.Name, 20),
                Alignment.TopLeft, GUI.SmallFont, GuiFrame);
            levelStartTickBox.Enabled = false;
            levelStartTickBox.OnSelected = SelectDestination;

            levelEndTickBox = new GUITickBox(
                new Rectangle(5, 90, 15, 15),
                GameMain.GameSession == null ? "" : ToolBox.LimitString(GameMain.GameSession.EndLocation.Name, 20),
                Alignment.TopLeft, GUI.SmallFont, GuiFrame);
            levelEndTickBox.Enabled = false;
            levelEndTickBox.OnSelected = SelectDestination;
        }

        private bool ToggleMaintainPosition(GUITickBox tickBox)
        {
            unsentChanges = true;

            levelStartTickBox.Selected = false;
            levelEndTickBox.Selected = false;

            if (item.Submarine == null)
            {
                posToMaintain = null;
            }
            else
            {
                posToMaintain = item.Submarine.WorldPosition;
            }

            tickBox.Selected = true;

            return true;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            //if (voltage < minVoltage) return;

            int width = GuiFrame.Rect.Width, height = GuiFrame.Rect.Height;
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            if (voltage < minVoltage && powerConsumption > 0.0f) return;

            Rectangle velRect = new Rectangle(x + 20, y + 20, width - 40, height - 40);
            //GUI.DrawRectangle(spriteBatch, velRect, Color.White, false);

            if (item.Submarine != null && Level.Loaded != null)
            {
                Vector2 realWorldVelocity = ConvertUnits.ToDisplayUnits(item.Submarine.Velocity * Physics.DisplayToRealWorldRatio) * 3.6f;
                float realWorldDepth = Math.Abs(item.Submarine.Position.Y - Level.Loaded.Size.Y) * Physics.DisplayToRealWorldRatio;
                GUI.DrawString(spriteBatch, new Vector2(x + 20, y + height - 65),
                    "Velocity: " + (int)realWorldVelocity.X + " km/h", Color.LightGreen, null, 0, GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(x + 20, y + height - 50),
                    "Descent velocity: " + -(int)realWorldVelocity.Y + " km/h", Color.LightGreen, null, 0, GUI.SmallFont);

                GUI.DrawString(spriteBatch, new Vector2(x + 20, y + height - 30),
                    "Depth: " + (int)realWorldDepth + " m", Color.LightGreen, null, 0, GUI.SmallFont);
            }

            GUI.DrawLine(spriteBatch,
                new Vector2(velRect.Center.X, velRect.Center.Y),
                new Vector2(velRect.Center.X + currVelocity.X, velRect.Center.Y - currVelocity.Y),
                Color.Gray);

            Vector2 targetVelPos = new Vector2(velRect.Center.X + targetVelocity.X, velRect.Center.Y - targetVelocity.Y);

            GUI.DrawLine(spriteBatch,
                new Vector2(velRect.Center.X, velRect.Center.Y),
                targetVelPos,
                Color.LightGray);

            GUI.DrawRectangle(spriteBatch, new Rectangle((int)targetVelPos.X - 5, (int)targetVelPos.Y - 5, 10, 10), Color.White);

            if (Vector2.Distance(PlayerInput.MousePosition, new Vector2(velRect.Center.X, velRect.Center.Y)) < 200.0f)
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)targetVelPos.X - 10, (int)targetVelPos.Y - 10, 20, 20), Color.Red);
            }
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update(1.0f / 60.0f);

            if (Vector2.Distance(PlayerInput.MousePosition, new Vector2(GuiFrame.Rect.Center.X, GuiFrame.Rect.Center.Y)) < 200.0f)
            {
                if (PlayerInput.LeftButtonHeld())
                {
                    TargetVelocity = PlayerInput.MousePosition - new Vector2(GuiFrame.Rect.Center.X, GuiFrame.Rect.Center.Y);
                    targetVelocity.Y = -targetVelocity.Y;

                    unsentChanges = true;
                }
            }
        }

        public void SetDestinationLevelStart()
        {
            AutoPilot = true;

            MaintainPos = false;
            posToMaintain = null;

            levelEndTickBox.Selected = false;

            if (!levelStartTickBox.Selected)
            {
                levelStartTickBox.Selected = true;
                UpdatePath();
            }
        }

        public void SetDestinationLevelEnd()
        {
            AutoPilot = false;

            MaintainPos = false;
            posToMaintain = null;

            levelStartTickBox.Selected = false;

            if (!levelEndTickBox.Selected)
            {
                levelEndTickBox.Selected = true;
                UpdatePath();
            }
        }


        private bool SelectDestination(GUITickBox tickBox)
        {
            unsentChanges = true;

            if (tickBox == levelStartTickBox)
            {
                levelEndTickBox.Selected = false;
            }
            else
            {
                levelStartTickBox.Selected = false;
            }

            maintainPosTickBox.Selected = false;
            posToMaintain = null;
            tickBox.Selected = true;

            UpdatePath();

            return true;
        }

        public void ClientWrite(Lidgren.Network.NetBuffer msg, object[] extraData = null)
        {
            msg.Write(autoPilot);

            if (!autoPilot)
            {
                //no need to write steering info if autopilot is controlling
                msg.Write(targetVelocity.X);
                msg.Write(targetVelocity.Y);
            }
            else
            {
                msg.Write(posToMaintain != null);
                if (posToMaintain != null)
                {
                    msg.Write(((Vector2)posToMaintain).X);
                    msg.Write(((Vector2)posToMaintain).Y);
                }
                else
                {
                    msg.Write(LevelStartSelected);
                }
            }
        }
        
        public void ClientRead(ServerNetObject type, Lidgren.Network.NetBuffer msg, float sendingTime)
        {
            long msgStartPos = msg.Position;

            bool autoPilot = msg.ReadBoolean();
            Vector2 newTargetVelocity = targetVelocity;
            bool maintainPos = false;
            Vector2? newPosToMaintain = null;
            bool headingToStart = false;

            if (autoPilot)
            {
                maintainPos = msg.ReadBoolean();
                if (maintainPos)
                {
                    newPosToMaintain = new Vector2(
                        msg.ReadFloat(),
                        msg.ReadFloat());
                }
                else
                {
                    headingToStart = msg.ReadBoolean();
                }
            }
            else
            {
                newTargetVelocity = new Vector2(msg.ReadFloat(), msg.ReadFloat());
            }

            if (correctionTimer > 0.0f)
            {
                int msgLength = (int)(msg.Position - msgStartPos);
                msg.Position = msgStartPos;
                StartDelayedCorrection(type, msg.ExtractBits(msgLength), sendingTime);
                return;
            }

            AutoPilot = autoPilot;

            if (!AutoPilot)
            {
                targetVelocity = newTargetVelocity;
            }
            else
            {

                MaintainPos = newPosToMaintain != null;
                posToMaintain = newPosToMaintain;

                if (posToMaintain == null)
                {
                    LevelStartSelected = headingToStart;
                    LevelEndSelected = !headingToStart;
                    UpdatePath();
                }
                else
                {
                    LevelStartSelected = false;
                    LevelEndSelected = false;
                }
            }
        }
    }
}
