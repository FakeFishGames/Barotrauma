using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Barotrauma
{
    partial class Item : MapEntity, IDamageable, IPropertyObject, IServerSerializable, IClientSerializable
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
                editingHUD = CreateEditingHUD(Screen.Selected != GameMain.EditMapScreen);
            }

            editingHUD.Update((float)Timing.Step);

            if (Screen.Selected != GameMain.EditMapScreen) return;

            if (!prefab.IsLinkable) return;

            if (!PlayerInput.LeftButtonClicked() || !PlayerInput.KeyDown(Keys.Space)) return;

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

            foreach (MapEntity entity in mapEntityList)
            {
                if (entity == this || !entity.IsHighlighted) continue;
                if (linkedTo.Contains(entity)) continue;
                if (!entity.IsMouseOn(position)) continue;

                linkedTo.Add(entity);
                if (entity.IsLinkable && entity.linkedTo != null) entity.linkedTo.Add(this);
            }
        }

        public override void DrawEditing(SpriteBatch spriteBatch, Camera cam)
        {
            if (editingHUD != null) editingHUD.Draw(spriteBatch);
        }

        private GUIComponent CreateEditingHUD(bool inGame = false)
        {
            List<ObjectProperty> editableProperties = inGame ? GetProperties<InGameEditable>() : GetProperties<Editable>();

            int requiredItemCount = 0;
            if (!inGame)
            {
                foreach (ItemComponent ic in components)
                {
                    requiredItemCount += ic.requiredItems.Count;
                }
            }

            int width = 450;
            int height = 80 + requiredItemCount * 20;
            int x = GameMain.GraphicsWidth / 2 - width / 2, y = 10;
            foreach (var objectProperty in editableProperties)
            {
                var editable = objectProperty.Attributes.OfType<Editable>().FirstOrDefault();
                if (editable != null) height += (int)(Math.Ceiling(editable.MaxLength / 40.0f) * 18.0f) + 5;
            }

            editingHUD = new GUIFrame(new Rectangle(x, y, width, height), "");
            editingHUD.Padding = new Vector4(10, 10, 0, 0);
            editingHUD.UserData = this;

            new GUITextBlock(new Rectangle(0, 0, 100, 20), prefab.Name, "",
                Alignment.TopLeft, Alignment.TopLeft, editingHUD, false, GUI.LargeFont);

            y += 25;

            if (!inGame)
            {
                if (prefab.IsLinkable)
                {
                    new GUITextBlock(new Rectangle(0, 5, 0, 20), "Hold space to link to another item",
                        "", Alignment.TopRight, Alignment.TopRight, editingHUD).Font = GUI.SmallFont;
                }
                foreach (ItemComponent ic in components)
                {
                    foreach (RelatedItem relatedItem in ic.requiredItems)
                    {
                        new GUITextBlock(new Rectangle(0, y, 100, 15), ic.Name + ": " + relatedItem.Type.ToString() + " required", "", Alignment.TopLeft, Alignment.CenterLeft, editingHUD, false, GUI.SmallFont);
                        GUITextBox namesBox = new GUITextBox(new Rectangle(-10, y, 160, 15), Alignment.Right, "", editingHUD);
                        namesBox.Font = GUI.SmallFont;

                        PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(relatedItem);
                        PropertyDescriptor property = properties.Find("JoinedNames", false);

                        namesBox.Text = relatedItem.JoinedNames;
                        namesBox.UserData = new ObjectProperty(property, relatedItem);
                        namesBox.OnEnterPressed = EnterProperty;
                        namesBox.OnTextChanged = PropertyChanged;

                        y += 20;
                    }
                }
                if (requiredItemCount > 0) y += 10;
            }

            foreach (var objectProperty in editableProperties)
            {
                int boxHeight = 18;
                var editable = objectProperty.Attributes.OfType<Editable>().FirstOrDefault();
                if (editable != null) boxHeight = (int)(Math.Ceiling(editable.MaxLength / 40.0f) * 18.0f);

                object value = objectProperty.GetValue();

                if (value is bool)
                {
                    GUITickBox propertyTickBox = new GUITickBox(new Rectangle(10, y, 18, 18), objectProperty.Name,
                        Alignment.Left, editingHUD);
                    propertyTickBox.Font = GUI.SmallFont;

                    propertyTickBox.Selected = (bool)value;

                    propertyTickBox.UserData = objectProperty;
                    propertyTickBox.OnSelected = EnterProperty;
                }
                else
                {
                    new GUITextBlock(new Rectangle(0, y, 100, 18), objectProperty.Name, "", Alignment.TopLeft, Alignment.Left, editingHUD, false, GUI.SmallFont);

                    GUITextBox propertyBox = new GUITextBox(new Rectangle(180, y, 250, boxHeight), "", editingHUD);
                    propertyBox.Font = GUI.SmallFont;
                    if (boxHeight > 18) propertyBox.Wrap = true;

                    if (value != null)
                    {
                        if (value is float)
                        {
                            propertyBox.Text = ((float)value).ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else
                        {

                            propertyBox.Text = value.ToString();
                        }
                    }

                    propertyBox.UserData = objectProperty;
                    propertyBox.OnEnterPressed = EnterProperty;
                    propertyBox.OnTextChanged = PropertyChanged;

                }
                y = y + boxHeight + 5;

            }
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
            if (Screen.Selected is EditMapScreen)
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
            var objectProperty = tickBox.UserData as ObjectProperty;
            if (objectProperty == null) return false;

            objectProperty.TrySetValue(tickBox.Selected);

            return true;
        }

        private bool EnterProperty(GUITextBox textBox, string text)
        {
            textBox.Color = Color.DarkGreen;

            var objectProperty = textBox.UserData as ObjectProperty;
            if (objectProperty == null) return false;

            object prevValue = objectProperty.GetValue();

            textBox.Deselect();

            if (objectProperty.TrySetValue(text))
            {
                textBox.Text = text;

                if (GameMain.Server != null)
                {
                    GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ChangeProperty, objectProperty });
                }
                else if (GameMain.Client != null)
                {
                    GameMain.Client.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ChangeProperty, objectProperty });
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