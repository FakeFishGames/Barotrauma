using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Item : MapEntity, IDamageable, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        public override Sprite Sprite
        {
            get { return prefab.sprite; }
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!Visible) return;
            Color color = (IsSelected && editing) ? color = Color.Red : spriteColor;
            if (isHighlighted) color = Color.Orange;

            SpriteEffects oldEffects = prefab.sprite.effects;
            prefab.sprite.effects ^= SpriteEffects;

            if (prefab.sprite != null)
            {
                float depth = GetDrawDepth();

                if (body == null)
                {
                    if (prefab.ResizeHorizontal || prefab.ResizeVertical || SpriteEffects.HasFlag(SpriteEffects.FlipHorizontally) || SpriteEffects.HasFlag(SpriteEffects.FlipVertically))
                    {
                        prefab.sprite.DrawTiled(spriteBatch, new Vector2(DrawPosition.X - rect.Width / 2, -(DrawPosition.Y + rect.Height / 2)), new Vector2(rect.Width, rect.Height), color);
                    }
                    else
                    {
                        prefab.sprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y), color, 0.0f, 1.0f, SpriteEffects.None, depth);
                    }

                }
                else if (body.Enabled)
                {
                    var holdable = GetComponent<Holdable>();
                    if (holdable != null && holdable.Picker?.AnimController != null)
                    {
                        if (holdable.Picker.SelectedItems[0] == this)
                        {
                            depth = holdable.Picker.AnimController.GetLimb(LimbType.RightHand).sprite.Depth + 0.000001f;
                        }
                        else if (holdable.Picker.SelectedItems[1] == this)
                        {
                            depth = holdable.Picker.AnimController.GetLimb(LimbType.LeftArm).sprite.Depth - 0.000001f;
                        }

                        body.Draw(spriteBatch, prefab.sprite, color, depth);
                    }
                    else
                    {
                        body.Draw(spriteBatch, prefab.sprite, color, depth);
                    }
                }
            }

            prefab.sprite.effects = oldEffects;

            List<IDrawableComponent> staticDrawableComponents = new List<IDrawableComponent>(drawableComponents); //static list to compensate for drawable toggling
            for (int i = 0; i < staticDrawableComponents.Count; i++)
            {
                staticDrawableComponents[i].Draw(spriteBatch, editing);
            }

            if (GameMain.DebugDraw && aiTarget != null) aiTarget.Draw(spriteBatch);

            if (!editing || (body != null && !body.Enabled))
            {
                return;
            }

            if (IsSelected || isHighlighted)
            {
                GUI.DrawRectangle(spriteBatch, new Vector2(DrawPosition.X - rect.Width / 2, -(DrawPosition.Y + rect.Height / 2)), new Vector2(rect.Width, rect.Height), Color.Green, false, 0, (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));

                foreach (Rectangle t in prefab.Triggers)
                {
                    Rectangle transformedTrigger = TransformTrigger(t);

                    Vector2 rectWorldPos = new Vector2(transformedTrigger.X, transformedTrigger.Y);
                    if (Submarine != null) rectWorldPos += Submarine.Position;
                    rectWorldPos.Y = -rectWorldPos.Y;

                    GUI.DrawRectangle(spriteBatch,
                        rectWorldPos,
                        new Vector2(transformedTrigger.Width, transformedTrigger.Height),
                        Color.Green,
                        false,
                        0,
                        (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
                }
            }

            if (!ShowLinks) return;

            foreach (MapEntity e in linkedTo)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(WorldPosition.X, -WorldPosition.Y),
                     new Vector2(e.WorldPosition.X, -e.WorldPosition.Y),
                    Color.Red * 0.3f);
            }
        }

        public override void UpdateEditing(Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData as Item != this)
            {
                editingHUD = CreateEditingHUD(Screen.Selected != GameMain.SubEditorScreen);
            }

            editingHUD.Update((float)Timing.Step);

            if (Screen.Selected != GameMain.SubEditorScreen) return;

            if (!Linkable) return;

            if (!PlayerInput.KeyDown(Keys.Space)) return;
            bool lClick = PlayerInput.LeftButtonClicked();
            bool rClick = PlayerInput.RightButtonClicked();
            if (!lClick && !rClick) return;

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
            if (lClick)
            {
                foreach (MapEntity entity in mapEntityList)
                {
                    if (entity == this || !entity.IsHighlighted) continue;
                    if (linkedTo.Contains(entity)) continue;
                    if (!entity.IsMouseOn(position)) continue;

                    linkedTo.Add(entity);
                    if (entity.Linkable && entity.linkedTo != null) entity.linkedTo.Add(this);
                }
            }
            else
            {
                foreach (MapEntity entity in mapEntityList)
                {
                    if (entity == this || !entity.IsHighlighted) continue;
                    if (!linkedTo.Contains(entity)) continue;
                    if (!entity.IsMouseOn(position)) continue;

                    linkedTo.Remove(entity);
                    if (entity.linkedTo != null && entity.linkedTo.Contains(this)) entity.linkedTo.Remove(this);
                }
            }
        }

        public override void DrawEditing(SpriteBatch spriteBatch, Camera cam)
        {
            if (editingHUD != null && editingHUD.UserData == this) editingHUD.Draw(spriteBatch);
        }

        private GUIComponent CreateEditingHUD(bool inGame = false)
        {
            int width = 450;
            int height = 150;
            int x = GameMain.GraphicsWidth / 2 - width / 2, y = 30;

            editingHUD = new GUIListBox(new Rectangle(x, y, width, height), "");
            editingHUD.UserData = this;
            GUIListBox listBox = (GUIListBox)editingHUD;
            listBox.Spacing = 5;
            
            var itemEditor = new SerializableEntityEditor(this, inGame, editingHUD, true);

            if (!inGame && Linkable)
            {
                itemEditor.AddCustomContent(new GUITextBlock(new Rectangle(0, 0, 0, 20), "Hold space to link to another item", "", null, GUI.SmallFont), 1);
            }            

            foreach (ItemComponent ic in components)
            {
                if (ic.requiredItems.Count == 0)
                {
                    if (inGame)
                    {
                        if (SerializableProperty.GetProperties<InGameEditable>(ic).Count == 0) continue;
                    }
                    else
                    {
                        if (SerializableProperty.GetProperties<Editable>(ic).Count == 0) continue;
                    }
                }

                var componentEditor = new SerializableEntityEditor(ic, inGame, editingHUD, !inGame);

                if (inGame) continue;

                foreach (RelatedItem relatedItem in ic.requiredItems)
                {
                    var textBlock = new GUITextBlock(new Rectangle(0, 0, 0, 20), relatedItem.Type.ToString() + " required", "", Alignment.TopLeft, Alignment.CenterLeft, null, false, GUI.SmallFont);
                    textBlock.Padding = new Vector4(10.0f, 0.0f, 10.0f, 0.0f);
                    componentEditor.AddCustomContent(textBlock, 1);

                    GUITextBox namesBox = new GUITextBox(new Rectangle(0, 0, 180, 20), Alignment.Right, "", textBlock);
                    namesBox.Font = GUI.SmallFont;
                    namesBox.Text = relatedItem.JoinedNames;

                    namesBox.OnDeselected += (textBox, key) =>
                    {
                        relatedItem.JoinedNames = textBox.Text;
                        textBox.Text = relatedItem.JoinedNames;
                    };

                    namesBox.OnEnterPressed += (textBox, text) =>
                    {
                        relatedItem.JoinedNames = text;
                        textBox.Text = relatedItem.JoinedNames;
                        return true;
                    };

                    y += 20;
                }
            }

            int contentHeight = (int)(editingHUD.children.Sum(c => c.Rect.Height) + (listBox.children.Count - 1) * listBox.Spacing + listBox.Padding.Y + listBox.Padding.W);

            editingHUD.SetDimensions(new Point(editingHUD.Rect.Width, MathHelper.Clamp(contentHeight, 50, editingHUD.Rect.Height)));

            return editingHUD;
        }

        public virtual void UpdateHUD(Camera cam, Character character)
        {
            if (condition <= 0.0f)
            {
                FixRequirement.UpdateHud(this, character);
                return;
            }

            if (HasInGameEditableProperties)
            {
                UpdateEditing(cam);
            }

            foreach (ItemComponent ic in components)
            {
                if (ic.CanBeSelected) ic.UpdateHUD(character);
            }
        }

        public virtual void DrawHUD(SpriteBatch spriteBatch, Camera cam, Character character)
        {
            if (condition <= 0.0f)
            {
                FixRequirement.DrawHud(spriteBatch, this, character);
                return;
            }

            if (HasInGameEditableProperties)
            {
                DrawEditing(spriteBatch, cam);
            }

            foreach (ItemComponent ic in components)
            {
                if (ic.CanBeSelected) ic.DrawHUD(spriteBatch, character);
            }
        }

        public override void AddToGUIUpdateList()
        {
            if (Screen.Selected is SubEditorScreen)
            {
                if (editingHUD != null) editingHUD.AddToGUIUpdateList();
            }
            else
            {
                if (HasInGameEditableProperties)
                {
                    if (editingHUD != null) editingHUD.AddToGUIUpdateList();
                }
            }

            if (Character.Controlled != null && Character.Controlled.SelectedConstruction == this)
            {
                if (condition <= 0.0f)
                {
                    FixRequirement.AddToGUIUpdateList();
                    return;
                }

                foreach (ItemComponent ic in components)
                {
                    if (ic.CanBeSelected) ic.AddToGUIUpdateList();
                }
            }
        }

        private bool EnterProperty(GUITickBox tickBox)
        {
            var property = tickBox.UserData as SerializableProperty;
            if (property == null) return false;

            property.TrySetValue(tickBox.Selected);

            return true;
        }

        private bool EnterProperty(GUITextBox textBox, string text)
        {
            textBox.Color = Color.DarkGreen;

            var property = textBox.UserData as SerializableProperty;
            if (property == null) return false;

            object prevValue = property.GetValue();

            textBox.Deselect();

            if (property.TrySetValue(text))
            {
                textBox.Text = text;

                if (GameMain.Server != null)
                {
                    GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ChangeProperty, property });
                }
                else if (GameMain.Client != null)
                {
                    GameMain.Client.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ChangeProperty, property });
                }

                return true;
            }
            else
            {
                if (prevValue != null)
                {
                    textBox.Text = prevValue.ToString();
                }
                return false;
            }
        }

        private bool PropertyChanged(GUITextBox textBox, string text)
        {
            textBox.Color = Color.Red;

            return true;
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            if (type == ServerNetObject.ENTITY_POSITION)
            {
                ClientReadPosition(type, msg, sendingTime);
                return;
            }

            NetEntityEvent.Type eventType =
                (NetEntityEvent.Type)msg.ReadRangedInteger(0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1);

            switch (eventType)
            {
                case NetEntityEvent.Type.ComponentState:
                    int componentIndex = msg.ReadRangedInteger(0, components.Count - 1);
                    (components[componentIndex] as IServerSerializable).ClientRead(type, msg, sendingTime);
                    break;
                case NetEntityEvent.Type.InventoryState:
                    ownInventory.ClientRead(type, msg, sendingTime);
                    break;
                case NetEntityEvent.Type.Status:
                    condition = msg.ReadRangedSingle(0.0f, prefab.Health, 8);

                    if (FixRequirements.Count > 0)
                    {
                        if (Condition <= 0.0f)
                        {
                            for (int i = 0; i < FixRequirements.Count; i++)
                                FixRequirements[i].Fixed = msg.ReadBoolean();
                        }
                        else
                        {
                            for (int i = 0; i < FixRequirements.Count; i++)
                                FixRequirements[i].Fixed = true;
                        }
                    }
                    break;
                case NetEntityEvent.Type.ApplyStatusEffect:
                    ActionType actionType = (ActionType)msg.ReadRangedInteger(0, Enum.GetValues(typeof(ActionType)).Length - 1);
                    ushort targetID = msg.ReadUInt16();

                    Character target = FindEntityByID(targetID) as Character;
                    ApplyStatusEffects(actionType, (float)Timing.Step, target, true);
                    break;
                case NetEntityEvent.Type.ChangeProperty:
                    ReadPropertyChange(msg);
                    break;
            }
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            if (extraData == null || extraData.Length == 0 || !(extraData[0] is NetEntityEvent.Type))
            {
                return;
            }

            NetEntityEvent.Type eventType = (NetEntityEvent.Type)extraData[0];
            msg.WriteRangedInteger(0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1, (int)eventType);
            switch (eventType)
            {
                case NetEntityEvent.Type.ComponentState:
                    int componentIndex = (int)extraData[1];
                    msg.WriteRangedInteger(0, components.Count - 1, componentIndex);

                    (components[componentIndex] as IClientSerializable).ClientWrite(msg, extraData);
                    break;
                case NetEntityEvent.Type.InventoryState:
                    ownInventory.ClientWrite(msg, extraData);
                    break;
                case NetEntityEvent.Type.Repair:
                    if (FixRequirements.Count > 0)
                    {
                        int requirementIndex = (int)extraData[1];
                        msg.WriteRangedInteger(0, FixRequirements.Count - 1, requirementIndex);
                    }
                    break;
                case NetEntityEvent.Type.ApplyStatusEffect:
                    //no further data needed, the server applies the effect
                    //on the character of the client who sent the message                    
                    break;
                case NetEntityEvent.Type.ChangeProperty:
                    WritePropertyChange(msg, extraData);
                    break;
            }
            msg.WritePadBits();
        }
        
        public void ClientReadPosition(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            Vector2 newPosition = new Vector2(msg.ReadFloat(), msg.ReadFloat());
            float newRotation = msg.ReadRangedSingle(0.0f, MathHelper.TwoPi, 7);
            bool awake = msg.ReadBoolean();
            Vector2 newVelocity = Vector2.Zero;

            if (awake)
            {
                newVelocity = new Vector2(
                    msg.ReadRangedSingle(-MaxVel, MaxVel, 12),
                    msg.ReadRangedSingle(-MaxVel, MaxVel, 12));
            }

            if (body == null)
            {
                DebugConsole.ThrowError("Received a position update for an item with no physics body (" + Name + ")");
                return;
            }

            body.FarseerBody.Awake = awake;
            if (body.FarseerBody.Awake)
            {
                if ((newVelocity - body.LinearVelocity).Length() > 8.0f) body.LinearVelocity = newVelocity;
            }
            else
            {
                body.FarseerBody.Enabled = false;
            }

            if ((newPosition - SimPosition).Length() > body.LinearVelocity.Length() * 2.0f)
            {
                body.SetTransform(newPosition, newRotation);

                Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);
                rect.X = (int)(displayPos.X - rect.Width / 2.0f);
                rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);
            }
        }

        public void CreateClientEvent<T>(T ic) where T : ItemComponent, IClientSerializable
        {
            if (GameMain.Client == null) return;

            int index = components.IndexOf(ic);
            if (index == -1) return;

            GameMain.Client.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ComponentState, index });
        }
    }
}