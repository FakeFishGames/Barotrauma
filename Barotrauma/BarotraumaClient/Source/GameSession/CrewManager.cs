using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CrewManager
    {
        const float ChatMessageFadeTime = 10.0f;

        /// <summary>
        /// How long the previously selected character waits doing nothing when switching to another character. Only affects idling.
        /// </summary>
        const float CharacterWaitOnSwitch = 10.0f;

        private readonly List<CharacterInfo> characterInfos = new List<CharacterInfo>();
        private readonly List<Character> characters = new List<Character>();

        private Point screenResolution;

        #region UI

        private GUIFrame guiFrame;
        private GUIFrame crewArea;
        private GUIListBox characterListBox;

        private GUIComponent reportButtonFrame;

        private GUIButton scrollButtonUp, scrollButtonDown;

        private GUIButton toggleCrewButton;
        private float crewAreaOpenState;
        private bool toggleCrewAreaOpen = true;
        private int characterInfoWidth;

        /// <summary>
        /// Present only in single player games. In multiplayer. The chatbox is found from GameSession.Client.
        /// </summary>
        public ChatBox ChatBox { get; private set; }

        private float prevUIScale;

        private GUIComponent orderTargetFrame, orderTargetFrameShadow;

        public bool AllowCharacterSwitch = true;

        public bool ToggleCrewAreaOpen
        {
            get { return toggleCrewAreaOpen; }
            set
            {
                if (toggleCrewAreaOpen == value) { return; }
                toggleCrewAreaOpen = GameMain.Config.CrewMenuOpen = value;
                foreach (GUIComponent child in toggleCrewButton.Children)
                {
                    child.SpriteEffects = toggleCrewAreaOpen ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                }
            }
        }

        public List<GUIButton> OrderOptionButtons = new List<GUIButton>();

        #endregion

        #region Constructors

        public CrewManager(XElement element, bool isSinglePlayer)
            : this(isSinglePlayer)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "character") continue;

                var characterInfo = new CharacterInfo(subElement);
                characterInfos.Add(characterInfo);
                foreach (XElement invElement in subElement.Elements())
                {
                    if (invElement.Name.ToString().ToLowerInvariant() != "inventory") continue;
                    characterInfo.InventoryData = invElement;
                    break;
                }
            }
        }

        partial void InitProjectSpecific()
        {
            guiFrame = new GUIFrame(new RectTransform(Vector2.One, GUICanvas.Instance), null, Color.Transparent)
            {
                CanBeFocused = false
            };

            Point scrollButtonSize = new Point((int)(200 * GUI.Scale), (int)(30 * GUI.Scale));

            crewArea = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.CrewArea, guiFrame.RectTransform), "", Color.Transparent)
            {
                CanBeFocused = false
            };
            toggleCrewButton = new GUIButton(new RectTransform(new Point((int)(30 * GUI.Scale), HUDLayoutSettings.CrewArea.Height), guiFrame.RectTransform)
            { AbsoluteOffset = HUDLayoutSettings.CrewArea.Location },
                "", style: "UIToggleButton");
            toggleCrewButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                ToggleCrewAreaOpen = !ToggleCrewAreaOpen;
                return true;
            };

            characterListBox = new GUIListBox(new RectTransform(new Point(100, (int)(crewArea.Rect.Height - scrollButtonSize.Y * 1.6f)), crewArea.RectTransform, Anchor.CenterLeft), false, Color.Transparent, null)
            {
                //Spacing = (int)(3 * GUI.Scale),
                ScrollBarEnabled = false,
                ScrollBarVisible = false,
                CanBeFocused = true,
                OnSelected = (component, userdata) => false
            };

            scrollButtonUp = new GUIButton(new RectTransform(scrollButtonSize, crewArea.RectTransform, Anchor.TopLeft, Pivot.TopLeft), "", Alignment.Center, "GUIButtonVerticalArrow")
            {
                Visible = false,
                UserData = -1,
                OnClicked = ScrollCharacterList
            };
            scrollButtonDown = new GUIButton(new RectTransform(scrollButtonSize, crewArea.RectTransform, Anchor.BottomLeft, Pivot.BottomLeft), "", Alignment.Center, "GUIButtonVerticalArrow")
            {
                Visible = false,
                UserData = 1,
                OnClicked = ScrollCharacterList
            };
            scrollButtonDown.Children.ForEach(c => c.SpriteEffects = SpriteEffects.FlipVertically);

            if (isSinglePlayer)
            {
                ChatBox = new ChatBox(guiFrame, isSinglePlayer: true)
                {
                    OnEnterMessage = (textbox, text) =>
                    {
                        if (Character.Controlled?.Info == null)
                        {
                            textbox.Deselect();
                            textbox.Text = "";
                            return true;
                        }

                        textbox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            string msgCommand = ChatMessage.GetChatMessageCommand(text, out string msg);
                            AddSinglePlayerChatMessage(
                                Character.Controlled.Info.Name,
                                msg,
                                ((msgCommand == "r" || msgCommand == "radio") && ChatMessage.CanUseRadio(Character.Controlled)) ? ChatMessageType.Radio : ChatMessageType.Default,
                                Character.Controlled);
                            var headset = GetHeadset(Character.Controlled, true);
                            if (headset != null && headset.CanTransmit())
                            {
                                headset.TransmitSignal(stepsTaken: 0, signal: msg, source: headset.Item, sender: Character.Controlled, sendToChat: false);
                            }
                        }
                        textbox.Deselect();
                        textbox.Text = "";
                        return true;
                    }
                };

                ChatBox.InputBox.OnTextChanged += ChatBox.TypingChatMessage;
            }

            var reports = Order.PrefabList.FindAll(o => o.TargetAllCharacters && o.SymbolSprite != null);
            if (reports.None())
            {
                DebugConsole.ThrowError("No valid orders for report buttons found! Cannot create report buttons. The orders for the report buttons must have 'targetallcharacters' attribute enabled and a valid 'symbolsprite' defined.");
                return;
            }
            reportButtonFrame = new GUILayoutGroup(new RectTransform(
                new Point((HUDLayoutSettings.CrewArea.Height - (int)((reports.Count - 1) * 5 * GUI.Scale)) / reports.Count, HUDLayoutSettings.CrewArea.Height), guiFrame.RectTransform))
            {
                AbsoluteSpacing = (int)(5 * GUI.Scale),
                UserData = "reportbuttons",
                CanBeFocused = false
            };

            //report buttons
            foreach (Order order in reports)
            {
                if (!order.TargetAllCharacters || order.SymbolSprite == null) continue;
                var btn = new GUIButton(new RectTransform(new Point(reportButtonFrame.Rect.Width), reportButtonFrame.RectTransform), style: null)
                {
                    OnClicked = (GUIButton button, object userData) =>
                    {
                        if (Character.Controlled == null || Character.Controlled.SpeechImpediment >= 100.0f) { return false; }
                        SetCharacterOrder(null, order, null, Character.Controlled);
                        var visibleHulls = new List<Hull>(Character.Controlled.GetVisibleHulls());
                        foreach (var hull in visibleHulls)
                        {
                            HumanAIController.PropagateHullSafety(Character.Controlled, hull);
                            HumanAIController.RefreshTargets(Character.Controlled, order, hull);
                        }
                        return true;
                    },
                    UserData = order,
                    ToolTip = order.Name
                };

                new GUIFrame(new RectTransform(new Vector2(1.5f), btn.RectTransform, Anchor.Center), "OuterGlow")
                {
                    Color = Color.Red * 0.8f,
                    HoverColor = Color.Red * 1.0f,
                    PressedColor = Color.Red * 0.6f,
                    UserData = "highlighted",
                    CanBeFocused = false,
                    Visible = false
                };

                var img = new GUIImage(new RectTransform(Vector2.One, btn.RectTransform), order.Prefab.SymbolSprite, scaleToFit: true)
                {
                    Color = order.Color,
                    HoverColor = Color.Lerp(order.Color, Color.White, 0.5f),
                    ToolTip = order.Name
                };
            }

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            prevUIScale = GUI.Scale;

            ToggleCrewAreaOpen = GameMain.Config.CrewMenuOpen;
        }


        #endregion

        #region Character list management

        public Rectangle GetCharacterListArea()
        {
            return characterListBox.Rect;
        }

        public IEnumerable<Character> GetCharacters()
        {
            return characters;
        }

        public IEnumerable<CharacterInfo> GetCharacterInfos()
        {
            return characterInfos;
        }

        public void AddCharacter(Character character)
        {
            if (character.Removed)
            {
                DebugConsole.ThrowError("Tried to add a removed character to CrewManager!\n" + Environment.StackTrace);
                return;
            }
            if (character.IsDead)
            {
                DebugConsole.ThrowError("Tried to add a dead character to CrewManager!\n" + Environment.StackTrace);
                return;
            }

            if (!characters.Contains(character)) characters.Add(character);
            if (!characterInfos.Contains(character.Info))
            {
                characterInfos.Add(character.Info);
            }

            CreateCharacterFrame(character, characterListBox.Content);
            characterListBox.Content.RectTransform.SortChildren((c1, c2) => { return c2.NonScaledSize.X - c1.NonScaledSize.X; });

            if (character is AICharacter)
            {
                var ai = character.AIController as HumanAIController;
                if (ai == null)
                {
                    DebugConsole.ThrowError("Error in crewmanager - attempted to give orders to a character with no HumanAIController");
                    return;
                }
                character.SetOrder(ai.CurrentOrder, "", null, false);
            }
        }

        public void AddCharacterInfo(CharacterInfo characterInfo)
        {
            if (characterInfos.Contains(characterInfo))
            {
                DebugConsole.ThrowError("Tried to add the same character info to CrewManager twice.\n" + Environment.StackTrace);
                return;
            }

            characterInfos.Add(characterInfo);
        }

        /// <summary>
        /// Remove the character from the crew (and crew menus).
        /// </summary>
        /// <param name="character">The character to remove</param>
        /// <param name="removeInfo">If the character info is also removed, the character will not be visible in the round summary.</param>
        public void RemoveCharacter(Character character, bool removeInfo = false)
        {
            if (character == null)
            {
                DebugConsole.ThrowError("Tried to remove a null character from CrewManager.\n" + Environment.StackTrace);
                return;
            }
            characters.Remove(character);
            if (removeInfo) characterInfos.Remove(character.Info);
        }

        /// <summary>
        /// Remove info of a selected character. The character will not be visible in any menus or the round summary.
        /// </summary>
        /// <param name="characterInfo"></param>
        public void RemoveCharacterInfo(CharacterInfo characterInfo)
        {
            characterInfos.Remove(characterInfo);
        }

        /// <summary>
        /// Create the UI component that holds the character's portrait and order/report buttons for the character
        /// </summary>
        private GUIComponent CreateCharacterFrame(Character character, GUIComponent parent)
        {
            int genericOrderCount = 0, correctOrderCount = 0, wrongOrderCount = 0;
            //sort the orders
            //  1. generic orders (follow, wait, etc)
            //  2. orders appropriate for the character's job (captain -> steer, etc)
            //  3. orders inappropriate for the job (captain -> operate reactor, etc)
            List<Order> orders = new List<Order>();
            foreach (Order order in Order.PrefabList)
            {
                if (order.TargetAllCharacters || order.SymbolSprite == null) continue;
                if (!JobPrefab.List.Values.Any(jp => jp.AppropriateOrders.Contains(order.Identifier)) &&
                    (order.AppropriateJobs == null || !order.AppropriateJobs.Any()))
                {
                    orders.Insert(0, order);
                    genericOrderCount++;
                }
                else if (order.HasAppropriateJob(character))
                {
                    orders.Add(order);
                    correctOrderCount++;
                }
            }
            foreach (Order order in Order.PrefabList)
            {
                if (order.SymbolSprite == null) continue;
                if (!order.TargetAllCharacters && !orders.Contains(order))
                {
                    orders.Add(order);
                    wrongOrderCount++;
                }
            }

            int spacing = (int)(10 * GUI.Scale);
            int height = (int)(45 * GUI.Scale);
            characterInfoWidth = (int)(200 * GUI.Scale);

            float charactersPerView = characterListBox.Rect.Height / (float)(height + characterListBox.Spacing);

            //if we can fit less than 25% of the last character or more than 75%,
            //change the size of the character frame slightly to fit them more nicely in the list box
            if (charactersPerView % 1.0f < 0.25f || charactersPerView % 1.0f > 0.75f)
            {
                height = (int)(characterListBox.Rect.Height / (float)Math.Round(charactersPerView)) - characterListBox.Spacing;
            }

            int iconSize = (int)(height * 0.8f);


            var frame = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, height), parent.RectTransform), style: "InnerFrame")
            {
                UserData = character,
                CanBeFocused = false
            };
            frame.Color = character.Info.Job.Prefab.UIColor;
            frame.SelectedColor = Color.Lerp(frame.Color, Color.White, 0.5f);
            frame.HoverColor = Color.Lerp(frame.Color, Color.White, 0.9f);

            new GUIFrame(new RectTransform(new Point(characterInfoWidth, (int)(frame.Rect.Height * 1.3f)), frame.RectTransform, Anchor.CenterLeft), style: "OuterGlow")
            {
                UserData = "highlight",
                Color = frame.SelectedColor,
                HoverColor = frame.SelectedColor,
                PressedColor = frame.SelectedColor,
                SelectedColor = frame.SelectedColor,
                CanBeFocused = false
            };
            //---------------- character area ----------------

            string characterToolTip = character.Info.Name;
            if (character.Info.Job != null)
            {
                characterToolTip += " (" + character.Info.Job.Name + ")";
            }
            var characterArea = new GUIButton(new RectTransform(new Point(characterInfoWidth, frame.Rect.Height), frame.RectTransform, Anchor.CenterLeft), style: "GUITextBox")
            {
                UserData = character,
                Color = frame.Color,
                SelectedColor = frame.SelectedColor,
                HoverColor = frame.HoverColor,
                ToolTip = characterToolTip
            };

            var soundIcon = new GUIImage(new RectTransform(new Point((int)(characterArea.Rect.Height * 0.5f)), characterArea.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(5, 0) },
                sprite: GUI.Style.GetComponentStyle("GUISoundIcon").Sprites[GUIComponent.ComponentState.None].FirstOrDefault().Sprite, scaleToFit: true)
            {
                UserData = new Pair<string, float>("soundicon", 0.0f),
                CanBeFocused = false,
                Visible = true
            };
            new GUIImage(new RectTransform(new Point((int)(characterArea.Rect.Height * 0.5f)), characterArea.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(5, 0) },
                "GUISoundIconDisabled")
            {
                UserData = "soundicondisabled",
                CanBeFocused = true,
                Visible = false
            };

            if (isSinglePlayer)
            {
                characterArea.OnClicked = CharacterClicked;
            }
            else
            {
                characterArea.CanBeSelected = false;
            }

            var characterImage = new GUICustomComponent(new RectTransform(new Point(characterArea.Rect.Height), characterArea.RectTransform, Anchor.CenterLeft),
                onDraw: (sb, component) => character.Info.DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()))
            {
                CanBeFocused = false,
                HoverColor = Color.White,
                SelectedColor = Color.White,
                ToolTip = characterToolTip
            };


            if (GameMain.GameSession?.GameMode?.Mission is CombatMission combatMission)
            {
                new GUIFrame(new RectTransform(Vector2.One, characterArea.RectTransform), style: "InnerGlow",
                    color: character.TeamID == Character.TeamType.Team1 ? Color.SteelBlue : Color.OrangeRed);
            }

            var characterName = new GUITextBlock(new RectTransform(new Point(characterArea.Rect.Width - characterImage.Rect.Width - soundIcon.Rect.Width - 10, characterArea.Rect.Height),
                characterArea.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(soundIcon.Rect.Width + 10, 0) },
                character.Name, textColor: frame.Color, font: GUI.SmallFont, wrap: true)
            {
                Color = frame.Color,
                HoverColor = Color.Transparent,
                SelectedColor = Color.Transparent,
                CanBeFocused = false,
                ToolTip = characterToolTip,
                AutoScale = true
            };

            //---------------- order buttons ----------------

            var orderButtonFrame = new GUILayoutGroup(new RectTransform(new Point(100, frame.Rect.Height), frame.RectTransform)
            { AbsoluteOffset = new Point(characterInfoWidth + spacing, 0) },
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                AbsoluteSpacing = (int)(10 * GUI.Scale),
                UserData = "orderbuttons"
            };

            var spacer = new GUIFrame(new RectTransform(new Point(spacing, orderButtonFrame.Rect.Height), frame.RectTransform)
            {
                AbsoluteOffset = new Point(characterInfoWidth, 0)
            });

            //listbox for holding the orders inappropriate for this character
            //(so we can easily toggle their visibility)
            var wrongOrderList = new GUIListBox(new RectTransform(new Point(50, orderButtonFrame.Rect.Height), orderButtonFrame.RectTransform), isHorizontal: true, style: null)
            {
                ScrollBarEnabled = false,
                ScrollBarVisible = false,
                Enabled = false,
                Spacing = spacing,
                ClampMouseRectToParent = false
            };
            wrongOrderList.Content.ClampMouseRectToParent = false;

            for (int i = 0; i < orders.Count; i++)
            {
                var order = orders[i];
                if (order.TargetAllCharacters) continue;

                RectTransform btnParent = (i >= genericOrderCount + correctOrderCount) ?
                    wrongOrderList.Content.RectTransform :
                    orderButtonFrame.RectTransform;

                var btn = new GUIButton(new RectTransform(new Point(iconSize, iconSize), btnParent, Anchor.CenterLeft),
                    style: null)
                {
                    UserData = order
                };

                new GUIFrame(new RectTransform(new Vector2(1.5f), btn.RectTransform, Anchor.Center), "OuterGlow")
                {
                    Color = Color.Lerp(order.Color, frame.Color, 0.5f) * 0.8f,
                    HoverColor = Color.Lerp(order.Color, frame.Color, 0.5f) * 1.0f,
                    PressedColor = Color.Lerp(order.Color, frame.Color, 0.5f) * 0.6f,
                    UserData = "selected",
                    CanBeFocused = false,
                    Visible = false
                };

                var img = new GUIImage(new RectTransform(Vector2.One, btn.RectTransform), order.Prefab.SymbolSprite);
                img.Scale = iconSize / (float)img.SourceRect.Width;
                img.Color = Color.Lerp(order.Color, frame.Color, 0.5f);
                img.ToolTip = order.Name;
                img.HoverColor = Color.Lerp(img.Color, Color.White, 0.5f);

                btn.OnClicked += (GUIButton button, object userData) =>
                {
#if CLIENT
                    if (GameMain.Client != null && Character.Controlled == null) { return false; }
#endif
                    if (Character.Controlled != null && Character.Controlled.SpeechImpediment >= 100.0f) { return false; }

                    if (btn.GetChildByUserData("selected").Visible)
                    {
                        SetCharacterOrder(character, Order.GetPrefab("dismissed"), null, Character.Controlled);
                    }
                    else
                    {
                        if (order.ItemComponentType != null || order.ItemIdentifiers.Length > 0 || order.Options.Length > 1)
                        {
                            CreateOrderTargetFrame(button, character, order);
                        }
                        else
                        {
                            SetCharacterOrder(character, order, null, Character.Controlled);
                        }
                    }
                    return true;
                };
                btn.UserData = order;
                btn.ToolTip = order.Name;

                //divider between different groups of orders
                if (i == genericOrderCount - 1 || i == genericOrderCount + correctOrderCount - 1)
                {
                    //TODO: divider sprite
                    new GUIFrame(new RectTransform(new Point(8, iconSize), orderButtonFrame.RectTransform), style: "GUIButton");
                }
            }

            var toggleWrongOrderBtn = new GUIButton(new RectTransform(new Point((int)(30 * GUI.Scale), wrongOrderList.Rect.Height), wrongOrderList.Content.RectTransform),
                "", style: "UIToggleButton")
            {
                UserData = "togglewrongorder",
                CanBeFocused = false
            };

            wrongOrderList.RectTransform.NonScaledSize = new Point(
                wrongOrderList.Content.Children.Sum(c => c.Rect.Width + wrongOrderList.Spacing),
                wrongOrderList.RectTransform.NonScaledSize.Y);
            wrongOrderList.RectTransform.SetAsLastChild();

            new GUIFrame(new RectTransform(new Point(
                wrongOrderList.Rect.Width - toggleWrongOrderBtn.Rect.Width - wrongOrderList.Spacing * 2,
                wrongOrderList.Rect.Height), wrongOrderList.Content.RectTransform),
                style: null)
            {
                CanBeFocused = false
            };

            //scale to fit the content
            orderButtonFrame.RectTransform.NonScaledSize = new Point(
                orderButtonFrame.Children.Sum(c => c.Rect.Width + orderButtonFrame.AbsoluteSpacing),
                orderButtonFrame.RectTransform.NonScaledSize.Y);

            frame.RectTransform.NonScaledSize = new Point(
                characterInfoWidth + spacing + (orderButtonFrame.Rect.Width - wrongOrderList.Rect.Width),
                frame.RectTransform.NonScaledSize.Y);

            characterListBox.RectTransform.NonScaledSize = new Point(
                characterListBox.Content.Children.Max(c => c.Rect.Width) + wrongOrderList.Rect.Width,
                characterListBox.RectTransform.NonScaledSize.Y);
            characterListBox.Content.RectTransform.NonScaledSize = characterListBox.RectTransform.NonScaledSize;
            characterListBox.UpdateScrollBarSize();
            return frame;
        }

        /// <summary>
        /// Sets which character is selected in the crew UI (highlight effect etc)
        /// </summary>
        public bool CharacterClicked(GUIComponent component, object selection)
        {
            if (!AllowCharacterSwitch) { return false; }
            Character character = selection as Character;
            if (character == null || character.IsDead || character.IsUnconscious) return false;
            SelectCharacter(character);
            return true;
        }

        public void ReviveCharacter(Character revivedCharacter)
        {
            if (characterListBox.Content.GetChildByUserData(revivedCharacter) is GUIComponent characterBlock)
            {
                characterBlock.Parent.RemoveChild(characterBlock);
            }
            if (characterInfos.Contains(revivedCharacter.Info)) AddCharacter(revivedCharacter);
        }

        public void KillCharacter(Character killedCharacter)
        {
            if (characterListBox.Content.GetChildByUserData(killedCharacter) is GUIComponent characterBlock)
            {
                CoroutineManager.StartCoroutine(KillCharacterAnim(characterBlock));
            }
            RemoveCharacter(killedCharacter);
        }

        private bool ScrollCharacterList(GUIButton button, object obj)
        {
            if (characterListBox.Content.CountChildren == 0) return false;
            int dir = (int)obj;

            float step =
                (characterListBox.Content.Children.First().Rect.Height + characterListBox.Spacing) /
                (characterListBox.TotalSize - characterListBox.Rect.Height);
            characterListBox.BarScroll += dir * step;
            
            //round the scroll so that we're not displaying partial character frames
            float roundedPos = MathUtils.RoundTowardsClosest(characterListBox.BarScroll, step);
            if (Math.Abs(roundedPos - characterListBox.BarScroll) < step / 2)
            {
                characterListBox.BarScroll = roundedPos;
            }

            return false;
        }

        private IEnumerable<object> KillCharacterAnim(GUIComponent component)
        {
            List<GUIComponent> components = component.GetAllChildren().ToList();
            components.Add(component);
            components.RemoveAll(c => 
                c.UserData is Pair<string, float> pair && pair.First == "soundicon" || 
                c.UserData as string == "soundicondisabled");

            foreach (GUIComponent comp in components)
            {
                comp.Color = Color.DarkRed;
            }

            yield return new WaitForSeconds(1.0f);

            float timer = 0.0f;
            float hideDuration = 1.0f;
            while (timer < hideDuration)
            {
                foreach (GUIComponent comp in components)
                {
                    comp.Color = Color.Lerp(Color.DarkRed, Color.Transparent, timer / hideDuration);
                    comp.RectTransform.LocalScale = new Vector2(comp.RectTransform.LocalScale.X, 1.0f - (timer / hideDuration));
                }
                timer += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }
            component.Parent?.RemoveChild(component);
            characterListBox.UpdateScrollBarSize();
            yield return CoroutineStatus.Success;
        }

        #endregion

        #region Dialog
        /// <summary>
        /// Adds the message to the single player chatbox.
        /// </summary>
        public void AddSinglePlayerChatMessage(string senderName, string text, ChatMessageType messageType, Character sender)
        {
            if (!isSinglePlayer)
            {
                DebugConsole.ThrowError("Cannot add messages to single player chat box in multiplayer mode!\n" + Environment.StackTrace);
                return;
            }
            if (string.IsNullOrEmpty(text)) { return; }

            if (sender != null)
            {
                GameMain.GameSession.CrewManager.SetCharacterSpeaking(sender);
            }
            ChatBox.AddMessage(ChatMessage.Create(senderName, text, messageType, sender));
        }

        private WifiComponent GetHeadset(Character character, bool requireEquipped)
        {
            if (character?.Inventory == null) return null;

            var radioItem = character.Inventory.Items.FirstOrDefault(it => it != null && it.GetComponent<WifiComponent>() != null);
            if (radioItem == null) return null;
            if (requireEquipped && !character.HasEquippedItem(radioItem)) return null;

            return radioItem.GetComponent<WifiComponent>();
        }

        partial void CreateRandomConversation()
        {
            if (GameMain.Client != null)
            {
                //let the server create random conversations in MP
                return;
            }
            List<Character> availableSpeakers = Character.CharacterList.FindAll(c =>
                c.AIController is HumanAIController &&
                !c.IsDead &&
                c.SpeechImpediment <= 100.0f);
            pendingConversationLines.AddRange(NPCConversation.CreateRandom(availableSpeakers));
        }

        #endregion


        #region Voice chat

        public void SetPlayerVoiceIconState(Client client, bool muted, bool mutedLocally)
        {
            if (client?.Character == null) { return; }

            var playerFrame = characterListBox.Content.FindChild(client.Character)?.FindChild(client.Character);
            if (playerFrame == null) { return; }
            var soundIcon = playerFrame.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon");
            var soundIconDisabled = playerFrame.FindChild("soundicondisabled");
            soundIcon.Visible = !muted && !mutedLocally;
            soundIconDisabled.Visible = muted || mutedLocally;
            soundIconDisabled.ToolTip = TextManager.Get(mutedLocally ? "MutedLocally" : "MutedGlobally");
        }

        public void SetClientSpeaking(Client client)
        {
            if (client?.Character != null) { SetCharacterSpeaking(client.Character); }
        }

        public void SetCharacterSpeaking(Character character)
        {
            var playerFrame = characterListBox.Content.FindChild(character)?.FindChild(character);
            if (playerFrame == null) { return; }
            var soundIcon = playerFrame.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon");
            Pair<string, float> userdata = soundIcon.UserData as Pair<string, float>;
            userdata.Second = 1.0f;

            soundIcon.Color = Color.White;
        }

        #endregion

        /// <summary>
        /// Sets the character's current order (if it's close enough to receive messages from orderGiver) and
        /// displays the order in the crew UI
        /// </summary>
        public void SetCharacterOrder(Character character, Order order, string option, Character orderGiver)
        {
            if (order != null && order.TargetAllCharacters)
            {
                if (orderGiver == null || orderGiver.CurrentHull == null) { return; }
                var hull = orderGiver.CurrentHull;
                AddOrder(new Order(order.Prefab, hull, null, orderGiver), order.Prefab.FadeOutTime);
                if (IsSinglePlayer)
                {
                    orderGiver.Speak(
                        order.GetChatMessage("", hull.DisplayName, givingOrderToSelf: character == orderGiver), ChatMessageType.Order);
                }
                else
                {
                    OrderChatMessage msg = new OrderChatMessage(order, "", hull, null, orderGiver);
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(msg);
                    }
                }
            }
            else
            {
                character.SetOrder(order, option, orderGiver, speak: orderGiver != character);
                if (IsSinglePlayer)
                {
                    orderGiver?.Speak(
                        order.GetChatMessage(character.Name, orderGiver.CurrentHull?.DisplayName, givingOrderToSelf: character == orderGiver, orderOption: option), null);
                }
                else if (orderGiver != null)
                {
                    OrderChatMessage msg = new OrderChatMessage(order, option, order.TargetItemComponent?.Item, character, orderGiver);
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(msg);
                    }
                }
                DisplayCharacterOrder(character, order);
            }
        }

        /// <summary>
        /// Displays the specified order in the crew UI next to the character.
        /// </summary>
        public void DisplayCharacterOrder(Character character, Order order)
        {
            foreach (GUIComponent characterListElement in characterListBox.Content.Children)
            {
                var characterFrame = characterListElement.FindChild(character);
                if (characterFrame == null) continue;

                var orderButtonFrame = characterListElement.GetChildByUserData("orderbuttons");

                //get all order buttons from the frame
                List<GUIButton> orderButtons = new List<GUIButton>();
                foreach (GUIComponent child in orderButtonFrame.Children)
                {
                    if (child is GUIButton orderBtn)
                    {
                        orderButtons.Add(orderBtn);
                    }
                    //the non-character-appropriate orders are in a hideable listbox, we need to go deeper!
                    else if (child is GUIListBox listBox)
                    {
                        foreach (GUIComponent listBoxElement in listBox.Content.Children)
                        {
                            if (listBoxElement is GUIButton orderBtn2 && listBoxElement.UserData is Order) orderButtons.Add(orderBtn2);
                        }
                    }
                }

                foreach (GUIButton button in orderButtons)
                {
                    var selectedIndicator = button.GetChildByUserData("selected");
                    if (selectedIndicator != null)
                    {
                        selectedIndicator.Visible = (order != null && ((Order)button.UserData).Prefab == order.Prefab);
                    }
                }
            }
        }

        /// <summary>
        /// Create the UI panel that's used to select the target and options for a given order
        /// (which railgun to use, whether to power up the reactor or shut it down...)
        /// </summary>
        private void CreateOrderTargetFrame(GUIComponent orderButton, Character character, Order order)
        {
            Submarine submarine = Character.Controlled != null && Character.Controlled.TeamID == Character.TeamType.Team2 && Submarine.MainSubs.Length > 1 ?
                Submarine.MainSubs[1] :
                Submarine.MainSub;

            List<Item> matchingItems = new List<Item>();
            if (order.ItemComponentType != null || order.ItemIdentifiers.Length > 0)
            {
                matchingItems = order.ItemIdentifiers.Length > 0 ?
                    Item.ItemList.FindAll(it => order.ItemIdentifiers.Contains(it.Prefab.Identifier) || it.HasTag(order.ItemIdentifiers)) :
                    Item.ItemList.FindAll(it => it.Components.Any(ic => ic.GetType() == order.ItemComponentType));

                matchingItems.RemoveAll(it => it.Submarine != submarine && !submarine.DockedTo.Contains(it.Submarine));
                matchingItems.RemoveAll(it => it.Submarine != null && it.Submarine.IsOutpost);
            }

            //more than one target item -> create a minimap-like selection with a pic of the sub
            if (matchingItems.Count > 1)
            {
                Rectangle subBorders = submarine.GetDockedBorders();

                Point frameSize;
                if (subBorders.Width > subBorders.Height)
                {
                    //make sure the right side doesn't go over the right side of the screen
                    frameSize.X = Math.Min(GameMain.GraphicsWidth / 2, GameMain.GraphicsWidth - orderButton.Rect.Center.X - 50);
                    //height depends on the dimensions of the sub
                    frameSize.Y = (int)(frameSize.X * (subBorders.Height / (float)subBorders.Width));
                }
                else
                {
                    //make sure the bottom side doesn't go over the bottom of the screen
                    frameSize.Y = Math.Min((int)(GameMain.GraphicsHeight * 0.6f), GameMain.GraphicsHeight - orderButton.Rect.Center.Y - 50);
                    //width depends on the dimensions of the sub
                    frameSize.X = (int)(frameSize.Y * (subBorders.Width / (float)subBorders.Height));
                }
                orderTargetFrame = new GUIFrame(new RectTransform(frameSize, GUI.Canvas)
                    { AbsoluteOffset = new Point(orderButton.Rect.Center.X, orderButton.Rect.Bottom) },
                    style: "InnerFrame")
                {
                    UserData = character
                };
                submarine.CreateMiniMap(orderTargetFrame, matchingItems);

                new GUICustomComponent(new RectTransform(Vector2.One, orderTargetFrame.RectTransform), DrawMiniMapOverlay)
                {
                    CanBeFocused = false,
                    UserData = submarine
                };

                List<GUIComponent> optionFrames = new List<GUIComponent>();
                foreach (Item item in matchingItems)
                {
                    var itemTargetFrame = orderTargetFrame.Children.First().FindChild(item);
                    if (itemTargetFrame == null) continue;

                    Anchor anchor = Anchor.TopLeft;
                    if (itemTargetFrame.RectTransform.RelativeOffset.X < 0.5f && itemTargetFrame.RectTransform.RelativeOffset.Y < 0.5f)
                        anchor = Anchor.BottomRight;
                    else if (itemTargetFrame.RectTransform.RelativeOffset.X > 0.5f && itemTargetFrame.RectTransform.RelativeOffset.Y < 0.5f)
                        anchor = Anchor.BottomLeft;
                    else if (itemTargetFrame.RectTransform.RelativeOffset.X < 0.5f && itemTargetFrame.RectTransform.RelativeOffset.Y > 0.5f)
                        anchor = Anchor.TopRight;

                    var optionFrame = new GUIFrame(new RectTransform(new Point((int)(250 * GUI.Scale), (int)((40 + order.Options.Length * 40) * GUI.Scale)), itemTargetFrame.RectTransform, anchor),
                        style: "InnerFrame");
                    optionFrames.Add(optionFrame);

                    new GUIFrame(new RectTransform(Vector2.One, optionFrame.RectTransform, Anchor.Center),
                        style: "OuterGlow")
                    {
                        Color = Color.Black * 0.7f
                    };

                    var optionContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f), optionFrame.RectTransform, Anchor.Center))
                    {
                        Stretch = true,
                        RelativeSpacing = 0.05f
                    };

                    new GUITextBlock(new RectTransform(new Vector2(1.0f,0.3f), optionContainer.RectTransform), item != null ? item.Name : order.Name);
                    for (int i = 0; i < order.Options.Length; i++)
                    {
                        string option = order.Options[i];
                        var optionButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.2f), optionContainer.RectTransform),
                            order.OptionNames[i], style: "GUITextBox")
                        {
                            UserData = item == null ? order : new Order(order, item, item.Components.FirstOrDefault(ic => ic.GetType() == order.ItemComponentType)),
                            Font = GUI.SmallFont,
                            OnClicked = (btn, userData) =>
                            {
#if CLIENT
                                if (GameMain.Client != null && Character.Controlled == null) { return false; }
#endif
                                SetCharacterOrder(character, userData as Order, option, Character.Controlled);
                                orderTargetFrame = null;
                                OrderOptionButtons.Clear();
                                return true;
                            }
                        };

                        OrderOptionButtons.Add(optionButton);
                    }
                }

                GUI.PreventElementOverlap(optionFrames, null, new Rectangle(10, 10, GameMain.GraphicsWidth - 20, GameMain.GraphicsHeight - 20));
            }
            //only one target (or an order with no particular targets), just show options
            else
            {
                orderTargetFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.2f + order.Options.Length * 0.1f, 0.18f), GUI.Canvas)
                    { AbsoluteOffset = new Point((int)(200 * GUI.Scale), orderButton.Rect.Bottom) },
                    isHorizontal: true, childAnchor: Anchor.BottomLeft)
                {
                    UserData = character,
                    Stretch = true
                };
                //line connecting the order button to the option buttons
                //TODO: sprite
                new GUIFrame(new RectTransform(new Vector2(0.5f, 1.0f), orderTargetFrame.RectTransform), style: null);

                for (int i = 0; i < order.Options.Length; i++)
                {
                    Item item = matchingItems.Count > 0 ? matchingItems[0] : null;
                    string option = order.Options[i];
                    var optionButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.5f), orderTargetFrame.RectTransform),
                        order.OptionNames[i], style: "GUITextBox")
                    {
                        UserData = item == null ? order : new Order(order, item, item.Components.FirstOrDefault(ic => ic.GetType() == order.ItemComponentType)),
                        OnClicked = (btn, userData) =>
                        {
#if CLIENT
                            if (GameMain.Client != null && Character.Controlled == null) { return false; }
#endif
                            SetCharacterOrder(character, userData as Order, option, Character.Controlled);
                            orderTargetFrame = null;
                            OrderOptionButtons.Clear();
                            return true;
                        }
                    };
                    new GUIFrame(new RectTransform(Vector2.One * 1.5f, optionButton.RectTransform, Anchor.Center), style: "OuterGlow")
                    {
                        Color = Color.Black,
                        HoverColor = Color.CadetBlue,
                        PressedColor = Color.Black
                    }.RectTransform.SetAsFirstChild();

                    OrderOptionButtons.Add(optionButton);

                    //lines between the order buttons
                    if (i < order.Options.Length - 1)
                    {
                        //TODO: sprite
                        new GUIFrame(new RectTransform(new Vector2(0.1f, 1.0f), orderTargetFrame.RectTransform), style: null);
                    }
                }
            }
            int shadowSize = (int)(200 * GUI.Scale);
            orderTargetFrameShadow = new GUIFrame(new RectTransform(orderTargetFrame.Rect.Size + new Point(shadowSize * 2), GUI.Canvas)
                { AbsoluteOffset = orderTargetFrame.Rect.Location - new Point(shadowSize) },
                style: "OuterGlow",
                color: matchingItems.Count > 1 ? Color.Black * 0.9f : Color.Black * 0.7f);
        }

        public void HighlightOrderButton(Character character, string orderIdentifier, Color color, Vector2? flashRectInflate = null)
        {
            var order = Order.GetPrefab(orderIdentifier);
            if (order == null)
            {
                DebugConsole.ThrowError("Could not find an order with the AI tag \"" + orderIdentifier + "\".\n" + Environment.StackTrace);
                return;
            }
            ToggleCrewAreaOpen = true;
            var characterElement = characterListBox.Content.FindChild(character);
            GUIButton orderBtn = characterElement.FindChild(order, recursive: true) as GUIButton;
            if (orderBtn.FlashTimer <= 0)
            {
                orderBtn.Flash(color, 1.5f, false, flashRectInflate);
            }

            //orderBtn.Pulsate(Vector2.One, Vector2.One * 2.0f, 1.5f);
        }

#region Updating and drawing the UI

        private void DrawMiniMapOverlay(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            Submarine sub = container.UserData as Submarine;

            if (sub?.HullVertices == null) { return; }

            var dockedBorders = sub.GetDockedBorders();
            dockedBorders.Location += sub.WorldPosition.ToPoint();

            float scale = Math.Min(
                container.Rect.Width / (float)dockedBorders.Width,
                container.Rect.Height / (float)dockedBorders.Height) * 0.9f;

            float displayScale = ConvertUnits.ToDisplayUnits(scale);
            Vector2 offset = (sub.WorldPosition - new Vector2(dockedBorders.Center.X, dockedBorders.Y - dockedBorders.Height / 2)) * scale;
            Vector2 center = container.Rect.Center.ToVector2();

            for (int i = 0; i < sub.HullVertices.Count; i++)
            {
                Vector2 start = (sub.HullVertices[i] * displayScale + offset);
                start.Y = -start.Y;
                Vector2 end = (sub.HullVertices[(i + 1) % sub.HullVertices.Count] * displayScale + offset);
                end.Y = -end.Y;
                GUI.DrawLine(spriteBatch, center + start, center + end, Color.DarkCyan * Rand.Range(0.3f, 0.35f), width: 10);
            }
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD || GUI.DisableUpperHUD) return;
            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y ||
                prevUIScale != GUI.Scale)
            {
                var prevCharacterListBox = characterListBox;
                InitProjectSpecific();

                foreach (GUIComponent c in prevCharacterListBox.Content.Children)
                {
                    Character character = c.UserData as Character;
                    if (character == null || character.IsDead || character.Removed) continue;
                    AddCharacter(character);
                    DisplayCharacterOrder(character, character.CurrentOrder);
                }
            }

            guiFrame.AddToGUIUpdateList();
            if (orderTargetFrame != null)
            {
                orderTargetFrameShadow?.AddToGUIUpdateList();
                orderTargetFrame?.AddToGUIUpdateList();
            }
        }

        public void SelectNextCharacter()
        {
            if (!AllowCharacterSwitch) { return; }
            if (GameMain.IsMultiplayer) { return; }
            if (characters.None()) { return; }
            SelectCharacter(characters[TryAdjustIndex(1)]);
        }

        public void SelectPreviousCharacter()
        {
            if (!AllowCharacterSwitch) { return; }
            if (GameMain.IsMultiplayer) { return; }
            if (characters.None()) { return; }
            SelectCharacter(characters[TryAdjustIndex(-1)]);
        }

        private void SelectCharacter(Character character)
        {
            if (!AllowCharacterSwitch) { return; }
            //make the previously selected character wait in place for some time
            //(so they don't immediately start idling and walking away from their station)
            if (Character.Controlled?.AIController?.ObjectiveManager != null)
            {
                Character.Controlled.AIController.ObjectiveManager.WaitTimer = CharacterWaitOnSwitch;
            }
            Character.Controlled = character;
        }

        private int TryAdjustIndex(int amount)
        {
            int index = Character.Controlled == null ? 0 : characters.IndexOf(Character.Controlled) + amount;
            int lastIndex = characters.Count - 1;
            if (index > lastIndex)
            {
                index = 0;
            }
            if (index < 0)
            {
                index = lastIndex;
            }
            return index;
        }

        partial void UpdateProjectSpecific(float deltaTime)
        {
            // Quick selection
            if (!GameMain.IsMultiplayer && GUI.KeyboardDispatcher.Subscriber == null)
            {
                if (PlayerInput.KeyHit(InputType.SelectNextCharacter))
                {
                    SelectNextCharacter();
                }
                if (PlayerInput.KeyHit(InputType.SelectPreviousCharacter))
                {
                    SelectPreviousCharacter();
                }
            }

            if (GUI.DisableHUD || GUI.DisableUpperHUD) return;
            if (ChatBox != null)
            {
                ChatBox.Update(deltaTime);
                ChatBox.InputBox.Visible = Character.Controlled != null;

                if (!DebugConsole.IsOpen && ChatBox.InputBox.Visible && GUI.KeyboardDispatcher.Subscriber == null)
                {
                    if (PlayerInput.KeyHit(InputType.Chat) && !ChatBox.InputBox.Selected)
                    {
                        ChatBox.GUIFrame.Flash(Color.DarkGreen, 0.5f);
                        ChatBox.InputBox.Select();
                    }

                    if (PlayerInput.KeyHit(InputType.RadioChat) && !ChatBox.InputBox.Selected)
                    {
                        ChatBox.GUIFrame.Flash(Color.YellowGreen, 0.5f);
                        ChatBox.InputBox.Select();
                        ChatBox.InputBox.Text = "r; ";
                    }
                }
            }

            crewArea.Visible = characters.Count > 0 && CharacterHealth.OpenHealthWindow == null;
            if (orderTargetFrame != null) orderTargetFrame.Visible = characterListBox.Visible;

            scrollButtonUp.Visible = characterListBox.BarScroll > 0.01f && characterListBox.BarSize < 1.0f;
            scrollButtonDown.Visible = characterListBox.BarScroll < 0.99 && characterListBox.BarSize < 1.0f;

            foreach (GUIComponent child in characterListBox.Content.Children)
            {
                Character character = (Character)child.UserData;
                child.Visible =
                    Character.Controlled == null ||
                    (Character.Controlled.TeamID == character.TeamID);

                if (child.Visible)
                {
                    child.GetChildByUserData("highlight").Visible = character == Character.Controlled;

                    var soundIcon = child.FindChild(character)?.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon") as GUIImage;
                    VoipClient.UpdateVoiceIndicator(soundIcon, 0.0f, deltaTime);

                    GUIListBox wrongOrderList = child.GetChildByUserData("orderbuttons")?.GetChild<GUIListBox>();
                    if (wrongOrderList != null)
                    {
                        Rectangle hoverRect = wrongOrderList.Rect;
                        if (wrongOrderList.BarScroll < 0.5f)
                        {
                            //higher tolerance when the orderlist is open (mouse needs to be moved further before it closes)
                            hoverRect.Inflate((int)(50 * GUI.Scale), (int)(50 * GUI.Scale));
                        }
                        else
                        {
                            hoverRect.Inflate((int)(30 * GUI.Scale), (int)(0 * GUI.Scale));
                        }

                        bool toggleOpen =
                            characterListBox.Content.Rect.Contains(PlayerInput.MousePosition) &&
                            hoverRect.Contains(PlayerInput.MousePosition);
                        wrongOrderList.CanBeFocused = toggleOpen;
                        wrongOrderList.Content.CanBeFocused = toggleOpen;
                        var wrongOrderBtn = wrongOrderList.GetChildByUserData("togglewrongorderbtn");
                        if (wrongOrderBtn != null)
                        {
                            wrongOrderBtn.CanBeFocused = toggleOpen;
                        }

                        //order target frame open on this character, check if we're giving any of the orders in wrongOrderList
                        if (!toggleOpen && orderTargetFrame != null && orderTargetFrame.UserData == child.UserData)
                        {
                            toggleOpen = wrongOrderList.Content.Children.Any(c =>
                                c.UserData is Order order &&
                                orderTargetFrame.Children.Any(c2 => c2.UserData == c.UserData));
                        }

                        float scroll = MathHelper.Clamp(wrongOrderList.BarScroll + (toggleOpen ? -deltaTime * 5.0f : deltaTime * 5.0f), 0.0f, 1.0f);
                        if (Math.Abs(wrongOrderList.BarScroll - scroll) > 0.01f) { wrongOrderList.BarScroll = scroll; }
                    }
                }
            }

            crewArea.RectTransform.AbsoluteOffset =
                Vector2.SmoothStep(new Vector2(-crewArea.Rect.Width, 0), new Vector2(toggleCrewButton.Rect.Width, 0), crewAreaOpenState).ToPoint();
            crewAreaOpenState = ToggleCrewAreaOpen ?
                Math.Min(crewAreaOpenState + deltaTime * 2.0f, 1.0f) :
                Math.Max(crewAreaOpenState - deltaTime * 2.0f, 0.0f);

            if (GUI.KeyboardDispatcher.Subscriber == null &&
                PlayerInput.KeyHit(InputType.CrewOrders) &&
                characters.Contains(Character.Controlled))
            {
                //deselect construction unless it's the ladders the character is climbing
                if (Character.Controlled != null && !Character.Controlled.IsClimbing)
                {
                    Character.Controlled.SelectedConstruction = null;
                }
                ToggleCrewAreaOpen = !ToggleCrewAreaOpen;
            }

            UpdateReports(deltaTime);

            if (orderTargetFrame != null)
            {
                Rectangle hoverArea = orderTargetFrame.Rect;
                foreach (GUIComponent child in orderTargetFrame.Children.First().Children)
                {
                    if (!(child.UserData is Item)) continue;
                    foreach (GUIComponent grandChild in child.Children)
                    {
                        hoverArea = Rectangle.Union(hoverArea, grandChild.Rect);
                    }
                }
                hoverArea.Inflate(100, 100);

                if (!hoverArea.Contains(PlayerInput.MousePosition) || PlayerInput.SecondaryMouseButtonClicked())
                {
                    orderTargetFrame = null;
                    OrderOptionButtons.Clear();
                }
            }
        }

#endregion

        /// <summary>
        /// Creates a listbox that includes all the characters in the crew, can be used externally (round info menus etc)
        /// </summary>
        public void CreateCrewListFrame(IEnumerable<Character> crew, GUIFrame crewFrame)
        {
            List<Character.TeamType> teamIDs = crew.Select(c => c.TeamID).Distinct().ToList();

            if (!teamIDs.Any()) teamIDs.Add(Character.TeamType.None);

            int listBoxHeight = 300 / teamIDs.Count;

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), crewFrame.RectTransform))
            {
                Stretch = true
            };

            for (int i = 0; i < teamIDs.Count; i++)
            {
                if (teamIDs.Count > 1)
                {
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), content.RectTransform), CombatMission.GetTeamName(teamIDs[i]));
                }

                GUIListBox crewList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.1f) })
                {
                    UserData = crewFrame
                };
                crewList.OnSelected = (component, obj) =>
                {
                    SelectCrewCharacter(component.UserData as Character, crewList);
                    return true;
                };

                foreach (Character character in crew.Where(c => c.TeamID == teamIDs[i]))
                {
                    GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), crewList.Content.RectTransform), style: "ListBoxElement")
                    {
                        UserData = character,
                        Color = (GameMain.NetworkMember != null && GameMain.Client.Character == character) ? Color.Gold * 0.2f : Color.Transparent
                    };

                    var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: true)
                    {
                        RelativeSpacing = 0.05f,
                        Stretch = true
                    };

                    new GUICustomComponent(new RectTransform(new Vector2(0.2f, 1.0f), paddedFrame.RectTransform, Anchor.CenterLeft),
                        onDraw: (sb, component) => character.Info.DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()))
                    {
                        CanBeFocused = false,
                        HoverColor = Color.White,
                        SelectedColor = Color.White
                    };

                    GUITextBlock textBlock = new GUITextBlock(new RectTransform(Vector2.One, paddedFrame.RectTransform),
                        ToolBox.LimitString(character.Info.Name + " (" + character.Info.Job.Name + ")", GUI.Font, paddedFrame.Rect.Width - paddedFrame.Rect.Height),
                        textColor: character.Info.Job.Prefab.UIColor);
                }
            }
        }

        /// <summary>
        /// Select a character from CrewListFrame
        /// </summary>
        protected bool SelectCrewCharacter(Character character, GUIComponent crewList)
        {
            if (character == null) return false;

            GUIComponent crewFrame = (GUIComponent)crewList.UserData;

            GUIComponent existingPreview = crewFrame.FindChild("SelectedCharacter");
            if (existingPreview != null) crewFrame.RemoveChild(existingPreview);

            var previewPlayer = new GUIFrame(new RectTransform(new Vector2(0.45f, 0.9f), crewFrame.RectTransform, Anchor.CenterRight) { RelativeOffset = new Vector2(0.05f, 0.0f) }, style: "InnerFrame")
            {
                UserData = "SelectedCharacter"
            };

            character.Info.CreateInfoFrame(previewPlayer);

            if (GameMain.NetworkMember != null) GameMain.Client.SelectCrewCharacter(character, previewPlayer);

            return true;
        }

#region Reports

        /// <summary>
        /// Enables/disables report buttons when needed
        /// </summary>
        public void UpdateReports(float deltaTime)
        {
            bool canIssueOrders = false;
            if (Character.Controlled?.CurrentHull?.Submarine != null && Character.Controlled.SpeechImpediment < 100.0f)
            {
                WifiComponent radio = GetHeadset(Character.Controlled, true);
                canIssueOrders = radio != null && radio.CanTransmit();
            }

            if (canIssueOrders)
            {
                reportButtonFrame.Visible = true;

                var reportButtonParent = ChatBox ?? GameMain.Client?.ChatBox;
                if (reportButtonParent == null) { return; }

                reportButtonFrame.RectTransform.AbsoluteOffset = new Point(
                    Math.Min(reportButtonParent.GUIFrame.Rect.X, reportButtonParent.ToggleButton.Rect.X) - reportButtonFrame.Rect.Width - (int)(10 * GUI.Scale),
                    reportButtonParent.GUIFrame.Rect.Y);

                bool hasFires = Character.Controlled.CurrentHull.FireSources.Count > 0;
                ToggleReportButton("reportfire", hasFires);

                bool hasLeaks = Character.Controlled.CurrentHull.Submarine != null && Character.Controlled.CurrentHull.ConnectedGaps.Any(g => !g.IsRoomToRoom && g.Open > 0.0f);
                ToggleReportButton("reportbreach", hasLeaks);

                bool hasIntruders = Character.CharacterList.Any(c => c.CurrentHull == Character.Controlled.CurrentHull && AIObjectiveFightIntruders.IsValidTarget(c, Character.Controlled));
                ToggleReportButton("reportintruders", hasIntruders);

                foreach (GUIComponent reportButton in reportButtonFrame.Children)
                {
                    var highlight = reportButton.GetChildByUserData("highlighted");
                    if (highlight.Visible)
                    {
                        highlight.RectTransform.LocalScale = new Vector2(1.25f + (float)Math.Sin(Timing.TotalTime * 5.0f) * 0.25f);
                    }
                }
            }
            else
            {
                reportButtonFrame.Visible = false;
            }
        }

        /// <summary>
        /// Should report buttons be visible on the screen atm?
        /// </summary>
        private bool ReportButtonsVisible()
        {
            return CharacterHealth.OpenHealthWindow == null;
        }
        
        private void ToggleReportButton(string orderIdentifier, bool enabled)
        {
            Order order = Order.GetPrefab(orderIdentifier);
            
            var reportButton = reportButtonFrame.GetChildByUserData(order);
            if (reportButton != null)
            {
                reportButton.GetChildByUserData("highlighted").Visible = enabled;
            }
        }

#endregion

        public void InitSinglePlayerRound()
        {
            characterListBox.ClearChildren();
            characters.Clear();

            WayPoint[] waypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub);

            for (int i = 0; i < waypoints.Length; i++)
            {
                Character character;
                character = Character.Create(characterInfos[i], waypoints[i].WorldPosition, characterInfos[i].Name);
                if (character.Info != null && !character.Info.StartItemsGiven)
                {
                    character.GiveJobItems(waypoints[i]);
                    character.Info.StartItemsGiven = true;
                }

                if (character.Info?.InventoryData != null)
                {
                    character.Info.SpawnInventoryItems(character.Inventory, character.Info.InventoryData);
                }

                AddCharacter(character);
                if (i == 0)
                {
                    Character.Controlled = character;
                }
            }

            if (characters.Any()) characterListBox.Select(0);

            conversationTimer = Rand.Range(5.0f, 10.0f);
        }

        public void EndRound()
        {
            //remove characterinfos whose characters have been removed or killed
            characterInfos.RemoveAll(c => c.Character == null || c.Character.Removed || c.CauseOfDeath != null);

            characters.Clear();
            characterListBox.ClearChildren();
        }

        public void Reset()
        {
            characters.Clear();
            characterInfos.Clear();
            characterListBox.ClearChildren();
        }

        public void Save(XElement parentElement)
        {
            XElement element = new XElement("crew");

            foreach (CharacterInfo ci in characterInfos)
            {
                var infoElement = ci.Save(element);
                if (ci.InventoryData != null)
                {
                    infoElement.Add(ci.InventoryData);
                }
            }
            parentElement.Add(element);
        }
    }
}
