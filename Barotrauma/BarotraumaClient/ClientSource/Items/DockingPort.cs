﻿using Barotrauma.Lights;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Collision;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class DockingPort : ItemComponent, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        private GUIMessageBox autodockingVerification;

        private readonly ConvexHull[] convexHulls = new ConvexHull[2];

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            if (dockingState == 0.0f) return;

            if (overlaySprite != null)
            {
                Vector2 drawPos = item.DrawPosition;
                drawPos.Y = -drawPos.Y;

                var rect = overlaySprite.SourceRect;
                if (IsHorizontal)
                {
                    drawPos.Y -= rect.Height / 2;

                    if (DockingDir == 1)
                    {
                        spriteBatch.Draw(overlaySprite.Texture,
                            drawPos,
                            new Rectangle(
                                rect.Center.X + (int)(rect.Width / 2 * (1.0f - dockingState)), rect.Y,
                                (int)(rect.Width / 2 * dockingState), rect.Height), Color.White);

                    }
                    else
                    {
                        spriteBatch.Draw(overlaySprite.Texture,
                            drawPos - Vector2.UnitX * (rect.Width / 2 * dockingState),
                            new Rectangle(
                                rect.X, rect.Y,
                                (int)(rect.Width / 2 * dockingState), rect.Height), Color.White);
                    }
                }
                else
                {
                    drawPos.X -= rect.Width / 2;

                    if (DockingDir == 1)
                    {
                        spriteBatch.Draw(overlaySprite.Texture,
                            drawPos - Vector2.UnitY * (rect.Height / 2 * dockingState),
                            new Rectangle(
                                rect.X, rect.Y,
                                rect.Width, (int)(rect.Height / 2 * dockingState)), Color.White);
                    }
                    else
                    {
                        spriteBatch.Draw(overlaySprite.Texture,
                            drawPos,
                            new Rectangle(
                                rect.X, rect.Y + rect.Height / 2 + (int)(rect.Height / 2 * (1.0f - dockingState)),
                                rect.Width, (int)(rect.Height / 2 * dockingState)), Color.White);
                    }
                }
            }

            if (!GameMain.DebugDraw) { return; }

            if (bodies != null)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    var body = bodies[i];
                    if (body == null) continue;

                    body.FixtureList[0].GetAABB(out AABB aabb, 0);

                    Vector2 bodyDrawPos = ConvertUnits.ToDisplayUnits(new Vector2(aabb.LowerBound.X, aabb.UpperBound.Y));
                    if ((i == 1 || i == 3) && DockingTarget?.item?.Submarine != null) bodyDrawPos += DockingTarget.item.Submarine.Position;
                    if ((i == 0 || i == 2) && item.Submarine != null) bodyDrawPos += item.Submarine.Position;
                    bodyDrawPos.Y = -bodyDrawPos.Y;

                    GUI.DrawRectangle(spriteBatch,
                        bodyDrawPos,
                        ConvertUnits.ToDisplayUnits(aabb.Extents * 2),
                        Color.Gray, false, 0.0f, 4);
                }
            }

            if (doorBody != null && doorBody.Enabled)
            {
                doorBody.FixtureList[0].GetAABB(out AABB aabb, 0);

                Vector2 bodyDrawPos = ConvertUnits.ToDisplayUnits(new Vector2(aabb.LowerBound.X, aabb.UpperBound.Y));
                if (item?.Submarine != null) bodyDrawPos += item.Submarine.Position;
                bodyDrawPos.Y = -bodyDrawPos.Y;

                GUI.DrawRectangle(spriteBatch,
                    bodyDrawPos,
                    ConvertUnits.ToDisplayUnits(aabb.Extents * 2),
                    Color.Gray, false, 0, 8);
            }
        }

        partial void RemoveConvexHulls()
        {
            for (int i = 0; i < convexHulls.Length; i++)
            {
                convexHulls[i]?.Remove();
                convexHulls[i] = null;
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            bool isDocked = msg.ReadBoolean();

            for (int i = 0; i < 2; i++)
            {
                if (hulls[i] == null) continue;
                item.linkedTo.Remove(hulls[i]);
                hulls[i].Remove();
                hulls[i] = null;
            }

            if (gap != null)
            {
                item.linkedTo.Remove(gap);
                gap.Remove();
                gap = null;
            }

            if (isDocked)
            {
                ushort dockingTargetID = msg.ReadUInt16();

                bool isLocked = msg.ReadBoolean();

                Entity targetEntity = Entity.FindEntityByID(dockingTargetID);
                if (targetEntity == null || !(targetEntity is Item))
                {
                    DebugConsole.ThrowError("Invalid docking port network event (can't dock to " + (targetEntity?.ToString() ?? "null") + ")");
                    return;
                }

                DockingTarget = (targetEntity as Item).GetComponent<DockingPort>();
                if (DockingTarget == null)
                {
                    DebugConsole.ThrowError("Invalid docking port network event (" + targetEntity + " doesn't have a docking port component)");
                    return;
                }

                Dock(DockingTarget);
                if (joint == null)
                {
                    string errorMsg = "Error while reading a docking port network event (Dock method did not create a joint between the ports)." +
                        " Submarine: " + (item.Submarine?.Info.Name ?? "null") +
                        ", target submarine: " + (DockingTarget.item.Submarine?.Info.Name ?? "null");
                    if (item.Submarine?.ConnectedDockingPorts.ContainsKey(DockingTarget.item.Submarine) ?? false)
                    {
                        errorMsg += "\nAlready docked.";
                    }
                    if (item.Submarine == DockingTarget.item.Submarine)
                    {
                        errorMsg += "\nTrying to dock the submarine to itself.";
                    }
                    GameAnalyticsManager.AddErrorEventOnce("DockingPort.ClientRead:JointNotCreated", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                }

                if (isLocked)
                {
                    if (DockingTarget.joint != null)
                    {
                        DockingTarget.Lock(isNetworkMessage: true);
                    }
                    else
                    {
                        Lock(isNetworkMessage: true);
                    }
                }
            }
            else
            {
                Undock();
            }
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            msg.WriteByte((byte)allowOutpostAutoDocking);
        }
    }
}
