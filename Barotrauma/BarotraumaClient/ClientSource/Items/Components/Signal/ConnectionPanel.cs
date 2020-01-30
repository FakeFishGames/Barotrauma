using Barotrauma.Networking;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ConnectionPanel : ItemComponent, IServerSerializable, IClientSerializable
    {
        //how long the rewiring sound plays after doing changes to the wiring
        const float RewireSoundDuration = 5.0f;

        public static Wire HighlightedWire;

        private SoundChannel rewireSoundChannel;
        private float rewireSoundTimer;

        public float Scale
        {
            get { return GuiFrame.Rect.Width / 400.0f; }
        }

        partial void InitProjSpecific(XElement element)
        {
            if (GuiFrame == null) { return; }
            new GUICustomComponent(new RectTransform(Vector2.One, GuiFrame.RectTransform), DrawConnections, null)
            {
                UserData = this
            };
        }

        public void TriggerRewiringSound()
        {
            rewireSoundTimer = RewireSoundDuration;
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            foreach (Wire wire in DisconnectedWires)
            {
                if (Rand.Range(0.0f, 500.0f) < 1.0f)
                {
                    SoundPlayer.PlaySound("zap", item.WorldPosition, hullGuess: item.CurrentHull);
                    Vector2 baseVel = new Vector2(0.0f, -100.0f);
                    for (int i = 0; i < 5; i++)
                    {
                        var particle = GameMain.ParticleManager.CreateParticle("spark", item.WorldPosition,
                            baseVel + Rand.Vector(100.0f), 0.0f, item.CurrentHull);
                        if (particle != null) { particle.Size *= Rand.Range(0.5f, 1.0f); }
                    }
                }
            }

            rewireSoundTimer -= deltaTime;
            if (user != null && user.SelectedConstruction == item && rewireSoundTimer > 0.0f)
            {
                if (rewireSoundChannel == null || !rewireSoundChannel.IsPlaying)
                {
                    rewireSoundChannel = SoundPlayer.PlaySound("rewire", item.WorldPosition, hullGuess: item.CurrentHull);
                }
            }
            else
            {
                rewireSoundChannel?.FadeOutAndDispose();
                rewireSoundChannel = null;
                rewireSoundTimer = 0.0f;
            }
        }

        public override void Move(Vector2 amount)
        {
            if (item.Submarine == null || item.Submarine.Loading || Screen.Selected != GameMain.SubEditorScreen) { return; }
            MoveConnectedWires(amount);
        }
        
        public override bool ShouldDrawHUD(Character character)
        {
            return character == Character.Controlled && character == user && character.SelectedConstruction == item;
        }
        
        public override void AddToGUIUpdateList()
        {
            GuiFrame?.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (character != Character.Controlled || character != user || character.SelectedConstruction != item) { return; }
            
            if (HighlightedWire != null)
            {
                HighlightedWire.Item.IsHighlighted = true;
                if (HighlightedWire.Connections[0] != null && HighlightedWire.Connections[0].Item != null) HighlightedWire.Connections[0].Item.IsHighlighted = true;
                if (HighlightedWire.Connections[1] != null && HighlightedWire.Connections[1].Item != null) HighlightedWire.Connections[1].Item.IsHighlighted = true;
            }
        }

        private void DrawConnections(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (user != Character.Controlled || user == null) { return; }

            HighlightedWire = null;
            Connection.DrawConnections(spriteBatch, this, user);

            foreach (UISprite sprite in GUI.Style.GetComponentStyle("ConnectionPanelFront").Sprites[GUIComponent.ComponentState.None])
            {
                sprite.Draw(spriteBatch, GuiFrame.Rect, Color.White, SpriteEffects.None);
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            if (GameMain.Client.MidRoundSyncing)
            {
                //delay reading the state until midround syncing is done
                //because some of the wires connected to the panel may not exist yet
                long msgStartPos = msg.BitPosition;
                msg.ReadUInt16(); //user ID
                foreach (Connection connection in Connections)
                {
                    for (int i = 0; i < Connection.MaxLinked; i++)
                    {
                        msg.ReadUInt16();
                    }
                }
                ushort disconnectedWireCount = msg.ReadUInt16();
                for (int i = 0; i < disconnectedWireCount; i++)
                {
                    msg.ReadUInt16();
                }
                int msgLength = (int)(msg.BitPosition - msgStartPos);
                msg.BitPosition = (int)msgStartPos;
                StartDelayedCorrection(type, msg.ExtractBits(msgLength), sendingTime, waitForMidRoundSync: true);
            }
            else
            {
                //don't trigger rewiring sounds if the rewiring is being done by the local user (in that case we'll trigger it locally)
                if (Character.Controlled == null || user != Character.Controlled) { TriggerRewiringSound(); }
                ApplyRemoteState(msg);
            }
        }

        private void ApplyRemoteState(IReadMessage msg)
        {
            List<Wire> prevWires = Connections.SelectMany(c => c.Wires.Where(w => w != null)).ToList();
            List<Wire> newWires = new List<Wire>();

            ushort userID = msg.ReadUInt16();

            if (userID == 0)
            {
                user = null;
            }
            else
            {
                user = Entity.FindEntityByID(userID) as Character;
                base.IsActive = true;
            }

            foreach (Connection connection in Connections)
            {
                connection.ClearConnections();
            }

            foreach (Connection connection in Connections)
            {
                for (int i = 0; i < Connection.MaxLinked; i++)
                {
                    ushort wireId = msg.ReadUInt16();

                    if (!(Entity.FindEntityByID(wireId) is Item wireItem)) { continue; }
                    Wire wireComponent = wireItem.GetComponent<Wire>();
                    if (wireComponent == null) { continue; }

                    newWires.Add(wireComponent);

                    connection.SetWire(i, wireComponent);
                    wireComponent.Connect(connection, false);
                }
            }

            List<Wire> previousDisconnectedWires = new List<Wire>(DisconnectedWires);
            DisconnectedWires.Clear();
            ushort disconnectedWireCount = msg.ReadUInt16();
            for (int i = 0; i < disconnectedWireCount; i++)
            {
                ushort wireId = msg.ReadUInt16();
                if (!(Entity.FindEntityByID(wireId) is Item wireItem)) { continue; }
                Wire wireComponent = wireItem.GetComponent<Wire>();
                if (wireComponent == null) { continue; }
                DisconnectedWires.Add(wireComponent);
                base.IsActive = true;
            }

            foreach (Wire wire in prevWires)
            {
                bool connected = wire.Connections[0] != null || wire.Connections[1] != null;
                if (!connected)
                {
                    foreach (Item item in Item.ItemList)
                    {
                        var connectionPanel = item.GetComponent<ConnectionPanel>();
                        if (connectionPanel != null && connectionPanel.DisconnectedWires.Contains(wire))
                        {
                            connected = true;
                            break;
                        }
                    }
                }
                if (wire.Item.ParentInventory == null && !connected)
                {
                    wire.Item.Drop(null);
                }
            }

            foreach (Wire disconnectedWire in previousDisconnectedWires)
            {
                if (disconnectedWire.Connections[0] == null &&
                    disconnectedWire.Connections[1] == null &&
                    !DisconnectedWires.Contains(disconnectedWire))
                {
                    disconnectedWire.Item.Drop(dropper: null);
                }
            }
        }
    }
}
