#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Directory = System.IO.Directory;

namespace Barotrauma
{
    internal class EventEditorScreen : Screen
    {
        private GUIFrame GuiFrame = null!;

        public override Camera Cam { get; }
        public static string? DrawnTooltip { get; set; }

        public static readonly List<EditorNode> nodeList = new List<EditorNode>();

        private readonly List<EditorNode> selectedNodes = new List<EditorNode>();

        public static Vector2 DraggingPosition = Vector2.Zero;
        public static NodeConnection? DraggedConnection;

        private EditorNode? draggedNode;
        private Vector2 dragOffset;

        private readonly Dictionary<EditorNode, Vector2> markedNodes = new Dictionary<EditorNode, Vector2>();

        private static string projectName = string.Empty;

        private OutpostGenerationParams? lastTestParam;
        private LocationType? lastTestType;

        private int CreateID()
        {
            int maxId = nodeList.Any() ? nodeList.Max(node => node.ID) : 0;
            return ++maxId;
        }

        private Point screenResolution;

        public EventEditorScreen()
        {
            Cam = new Camera();
            nodeList.Clear();
            CreateGUI();
        }

        private void CreateGUI()
        {
            GuiFrame = new GUIFrame(new RectTransform(new Vector2(0.2f, 0.4f), GUI.Canvas) { MinSize = new Point(300, 400) });
            GUILayoutGroup layoutGroup = new GUILayoutGroup(RectTransform(0.9f, 0.9f, GuiFrame, Anchor.Center)) { Stretch = true };

            // === BUTTONS === //
            GUILayoutGroup buttonLayout = new GUILayoutGroup(RectTransform(1.0f, 0.50f, layoutGroup)) { RelativeSpacing = 0.04f };
            GUIButton newProjectButton = new GUIButton(RectTransform(1.0f, 0.33f, buttonLayout), TextManager.Get("EventEditor.NewProject"));
            GUIButton saveProjectButton = new GUIButton(RectTransform(1.0f, 0.33f, buttonLayout), TextManager.Get("EventEditor.SaveProject"));
            GUIButton loadProjectButton = new GUIButton(RectTransform(1.0f, 0.33f, buttonLayout), TextManager.Get("EventEditor.LoadProject"));
            GUIButton exportProjectButton = new GUIButton(RectTransform(1.0f, 0.33f, buttonLayout), TextManager.Get("EventEditor.Export"));


            // === LOAD PREFAB === //

            GUILayoutGroup loadEventLayout = new GUILayoutGroup(RectTransform(1.0f, 0.125f, layoutGroup));
            new GUITextBlock(RectTransform(1.0f, 0.5f, loadEventLayout), TextManager.Get("EventEditor.LoadEvent"), font: GUI.SubHeadingFont);

            GUILayoutGroup loadDropdownLayout = new GUILayoutGroup(RectTransform(1.0f, 0.5f, loadEventLayout), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            GUIDropDown loadDropdown = new GUIDropDown(RectTransform(0.8f, 1.0f, loadDropdownLayout), elementCount: 10);
            GUIButton loadButton = new GUIButton(RectTransform(0.2f, 1.0f, loadDropdownLayout), TextManager.Get("Load"));

            // === ADD ACTION === //

            GUILayoutGroup addActionLayout = new GUILayoutGroup(RectTransform(1.0f, 0.125f, layoutGroup));
            new GUITextBlock(RectTransform(1.0f, 0.5f, addActionLayout), TextManager.Get("EventEditor.AddAction"), font: GUI.SubHeadingFont);

            GUILayoutGroup addActionDropdownLayout = new GUILayoutGroup(RectTransform(1.0f, 0.5f, addActionLayout), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            GUIDropDown addActionDropdown = new GUIDropDown(RectTransform(0.8f, 1.0f, addActionDropdownLayout), elementCount: 10);
            GUIButton addActionButton = new GUIButton(RectTransform(0.2f, 1.0f, addActionDropdownLayout), TextManager.Get("EventEditor.Add"));

            // === ADD VALUE === //
            GUILayoutGroup addValueLayout = new GUILayoutGroup(RectTransform(1.0f, 0.125f, layoutGroup));
            new GUITextBlock(RectTransform(1.0f, 0.5f, addValueLayout), TextManager.Get("EventEditor.AddValue"), font: GUI.SubHeadingFont);

            GUILayoutGroup addValueDropdownLayout = new GUILayoutGroup(RectTransform(1.0f, 0.5f, addValueLayout), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            GUIDropDown addValueDropdown = new GUIDropDown(RectTransform(0.8f, 1.0f, addValueDropdownLayout), elementCount: 7);
            GUIButton addValueButton = new GUIButton(RectTransform(0.2f, 1.0f, addValueDropdownLayout), TextManager.Get("EventEditor.Add"));
            
            // === ADD SPECIAL === //
            GUILayoutGroup addSpecialLayout = new GUILayoutGroup(RectTransform(1.0f, 0.125f, layoutGroup));
            new GUITextBlock(RectTransform(1.0f, 0.5f, addSpecialLayout), TextManager.Get("EventEditor.AddSpecial"), font: GUI.SubHeadingFont);
            GUILayoutGroup addSpecialDropdownLayout = new GUILayoutGroup(RectTransform(1.0f, 0.5f, addSpecialLayout), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            GUIDropDown addSpecialDropdown = new GUIDropDown(RectTransform(0.8f, 1.0f, addSpecialDropdownLayout), elementCount: 1);
            GUIButton addSpecialButton = new GUIButton(RectTransform(0.2f, 1.0f, addSpecialDropdownLayout), TextManager.Get("EventEditor.Add"));

            // Add event prefabs with identifiers to the list
            foreach (EventPrefab eventPrefab in EventSet.GetAllEventPrefabs().Where(prefab => !string.IsNullOrWhiteSpace(prefab.Identifier)).Distinct())
            {
                loadDropdown.AddItem(eventPrefab.Identifier, eventPrefab);
            }

            // Add all types that inherit the EventAction class
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes().Where(type => type.IsSubclassOf(typeof(EventAction))))
            {
                addActionDropdown.AddItem(type.Name, type);
            }

            addSpecialDropdown.AddItem("Custom", typeof(CustomNode));

            addValueDropdown.AddItem(nameof(Single), typeof(float));
            addValueDropdown.AddItem(nameof(Boolean), typeof(bool));
            addValueDropdown.AddItem(nameof(String), typeof(string));
            addValueDropdown.AddItem(nameof(SpawnType), typeof(SpawnType));
            addValueDropdown.AddItem(nameof(LimbType), typeof(LimbType));
            addValueDropdown.AddItem(nameof(ReputationAction.ReputationType), typeof(ReputationAction.ReputationType));
            addValueDropdown.AddItem(nameof(SpawnAction.SpawnLocationType), typeof(SpawnAction.SpawnLocationType));
            addValueDropdown.AddItem(nameof(CharacterTeamType), typeof(CharacterTeamType));

            loadButton.OnClicked += (button, o) => Load(loadDropdown.SelectedData as EventPrefab);
            addActionButton.OnClicked += (button, o) => AddAction(addActionDropdown.SelectedData as Type);
            addValueButton.OnClicked += (button, o) => AddValue(addValueDropdown.SelectedData as Type);
            addSpecialButton.OnClicked += (button, o) => AddSpecial(addSpecialDropdown.SelectedData as Type);
            exportProjectButton.OnClicked += ExportEventToFile;
            saveProjectButton.OnClicked += SaveProjectToFile;
            newProjectButton.OnClicked += TryCreateNewProject;
            loadProjectButton.OnClicked += (button, o) =>
            {
                FileSelection.OnFileSelected = (file) =>
                {
                    XDocument? document = XMLExtensions.TryLoadXml(file);
                    if (document?.Root != null)
                    {
                        Load(document.Root);
                    }
                };

                string directory = Path.GetFullPath("EventProjects");
                if (!Directory.Exists(directory)) { Directory.CreateDirectory(directory); }
                
                FileSelection.ClearFileTypeFilters();
                FileSelection.AddFileTypeFilter("Scripted Event", "*.sevproj");
                FileSelection.SelectFileTypeFilter("*.sevproj");
                FileSelection.CurrentDirectory = directory;
                FileSelection.Open = true;
                return true;
            };
            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        private bool ExportEventToFile(GUIButton button, object o)
        {
            XElement? save = ExportXML();
            if (save != null)
            {
                try
                {
                    string directory = Path.GetFullPath("EventProjects");
                    if (!Directory.Exists(directory)) { Directory.CreateDirectory(directory); }

                    string exportPath = Path.Combine(directory, "Exported");
                    if (!Directory.Exists(exportPath)) { Directory.CreateDirectory(exportPath); }

                    var msgBox = new GUIMessageBox(TextManager.Get("EventEditor.ExportProjectPrompt"), "", new[] { TextManager.Get("Cancel"), TextManager.Get("EventEditor.Export") }, new Vector2(0.2f, 0.175f), minSize: new Point(300, 175));
                    var layout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), msgBox.Content.RectTransform), isHorizontal: true);
                    GUITextBox nameInput = new GUITextBox(new RectTransform(Vector2.One, layout.RectTransform)) { Text = projectName };

                    // Cancel button
                    msgBox.Buttons[0].OnClicked = delegate
                    {
                        msgBox.Close();
                        return true;
                    };

                    // Ok button
                    msgBox.Buttons[1].OnClicked = delegate
                    {
                        foreach (var illegalChar in Path.GetInvalidFileNameChars())
                        {
                            if (!nameInput.Text.Contains(illegalChar)) { continue; }

                            GUI.AddMessage(TextManager.GetWithVariable("SubNameIllegalCharsWarning", "[illegalchar]", illegalChar.ToString()), GUI.Style.Red);
                            return false;
                        }

                        msgBox.Close();
                        string path = Path.Combine(exportPath, $"{nameInput.Text}.xml");
                        File.WriteAllText(path, save.ToString());
                        AskForConfirmation(TextManager.Get("EventEditor.OpenTextHeader"), TextManager.Get("EventEditor.OpenTextBody"), () =>
                        {
                            ToolBox.OpenFileWithShell(path);
                            return true;
                        });
                        GUI.AddMessage($"XML exported to {path}", GUI.Style.Green);
                        return true;
                    };
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to export event", e);
                }
            }
            else
            {
                GUI.AddMessage("Unable to export because the project contains errors", GUI.Style.Red);
            }

            return true;
        }

        private bool TryCreateNewProject(GUIButton button, object o)
        {
            AskForConfirmation(TextManager.Get("EventEditor.NewProject"), TextManager.Get("EventEditor.NewProjectPrompt"), () =>
            {
                nodeList.Clear();
                markedNodes.Clear();
                selectedNodes.Clear();
                projectName = TextManager.Get("EventEditor.Unnamed");
                return true;
            });
            return true;
        }

        public static GUIMessageBox AskForConfirmation(string header, string body, Func<bool> onConfirm)
        {
            string[] buttons = { TextManager.Get("Ok"), TextManager.Get("Cancel") };
            GUIMessageBox msgBox = new GUIMessageBox(header, body, buttons);

            // Cancel button
            msgBox.Buttons[1].OnClicked = delegate
            {
                msgBox.Close();
                return true;
            };

            // Ok button
            msgBox.Buttons[0].OnClicked = delegate
            {
                onConfirm.Invoke();
                msgBox.Close();
                return true;
            };
            return msgBox;
        }

        private bool SaveProjectToFile(GUIButton button, object o)
        {
            string directory = Path.GetFullPath("EventProjects");

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var msgBox = new GUIMessageBox(TextManager.Get("EventEditor.NameFilePrompt"), "", new[] { TextManager.Get("Cancel"), TextManager.Get("Save") }, new Vector2(0.2f, 0.175f), minSize: new Point(300, 175));
            var layout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), msgBox.Content.RectTransform), isHorizontal: true);
            GUITextBox nameInput = new GUITextBox(new RectTransform(Vector2.One, layout.RectTransform)) { Text = projectName };

            // Cancel button
            msgBox.Buttons[0].OnClicked = delegate
            {
                msgBox.Close();
                return true;
            };

            // Ok button
            msgBox.Buttons[1].OnClicked = delegate
            {
                foreach (var illegalChar in Path.GetInvalidFileNameChars())
                {
                    if (!nameInput.Text.Contains(illegalChar)) { continue; }

                    GUI.AddMessage(TextManager.GetWithVariable("SubNameIllegalCharsWarning", "[illegalchar]", illegalChar.ToString()), GUI.Style.Red);
                    return false;
                }

                msgBox.Close();
                projectName = nameInput.Text;
                XElement save = SaveEvent(projectName);
                string filePath = System.IO.Path.Combine(directory, $"{projectName}.sevproj");
                File.WriteAllText(Path.Combine(directory, $"{projectName}.sevproj"), save.ToString());
                GUI.AddMessage($"Project saved to {filePath}", GUI.Style.Green);

                AskForConfirmation(TextManager.Get("EventEditor.TestPromptHeader"), TextManager.Get("EventEditor.TestPromptBody"), CreateTestSetupMenu);
                return true;
            };
            return true;
        }

        private bool Load(EventPrefab? prefab)
        {
            if (prefab == null) { return false; }

            AskForConfirmation(TextManager.Get("EventEditor.NewProject"), TextManager.Get("EventEditor.NewProjectPrompt"), () =>
            {
                nodeList.Clear();
                selectedNodes.Clear();
                markedNodes.Clear();

                bool hadNodes = true;
                CreateNodes(prefab.ConfigElement, ref hadNodes);
                if (!hadNodes)
                {
                    GUI.NotifyPrompt(TextManager.Get("EventEditor.RandomGenerationHeader"), TextManager.Get("EventEditor.RandomGenerationBody"));
                }
                return true;
            });
            return true;
        }

        private bool AddAction(Type? type)
        {
            if (type == null) { return false; }

            Vector2 spawnPos = Cam.WorldViewCenter;
            spawnPos.Y = -spawnPos.Y;
            EventNode newNode = new EventNode(type, type.Name) { ID = CreateID() };
            newNode.Position = spawnPos - newNode.Size / 2;
            nodeList.Add(newNode);
            return true;
        }

        private bool AddValue(Type? type)
        {
            if (type == null) { return false; }

            Vector2 spawnPos = Cam.WorldViewCenter;
            spawnPos.Y = -spawnPos.Y;
            ValueNode newValue = new ValueNode(type, type.Name) { ID = CreateID() };
            newValue.Position = spawnPos - newValue.Size / 2;
            nodeList.Add(newValue);
            return true;
        }
        
        private bool AddSpecial(Type? type)
        {
            if (type == null) { return false; }
            Vector2 spawnPos = Cam.WorldViewCenter;
            spawnPos.Y = -spawnPos.Y;
    
            ConstructorInfo? constructor = type.GetConstructor(new Type[0]);
            SpecialNode? newNode = null;
            if (constructor != null)
            { 
                newNode = constructor.Invoke(new object[0]) as SpecialNode;
            }
            if (newNode != null)
            {
                newNode.ID = CreateID();
                newNode.Position = spawnPos - newNode.Size / 2;
                nodeList.Add(newNode);
                return true;
            }
            return false;
        }

        private void CreateNodes(XElement element, ref bool hadNodes, EditorNode? parent = null, int ident = 0)
        {
            EditorNode? lastNode = null;
            foreach (XElement subElement in element.Elements())
            {
                bool skip = true;
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "failure":
                    case "success":
                    case "option":
                        CreateNodes(subElement, ref hadNodes, parent, ident);
                        break;
                    default:
                        skip = false;
                        break;
                }

                if (!skip)
                {
                    Vector2 defaultNodePos = new Vector2(-16000, -16000);
                    EditorNode newNode;
                    Type? t = Type.GetType($"Barotrauma.{subElement.Name}");
                    if (t != null && EditorNode.IsInstanceOf(t, typeof(EventAction)))
                    {
                        newNode = new EventNode(t, subElement.Name.ToString()) { Position = new Vector2(ident, 0), ID = CreateID() };
                    }
                    else
                    {
                        newNode = new CustomNode(subElement.Name.ToString()) { Position = new Vector2(ident, 0), ID = CreateID() };
                        foreach (XAttribute attribute in subElement.Attributes().Where(attribute => !attribute.ToString().StartsWith("_")))
                        {
                            newNode.Connections.Add(new NodeConnection(newNode, NodeConnectionType.Value, attribute.Name.ToString(), typeof(string)));
                        }
                    }

                    Vector2 npos = subElement.GetAttributeVector2("_npos", defaultNodePos);
                    if (npos != defaultNodePos)
                    {
                        newNode.Position = npos;
                    }
                    else
                    {
                        hadNodes = false;
                    }

                    XElement? parentElement = subElement.Parent;

                    foreach (XElement xElement in subElement.Elements())
                    {
                        if (xElement.Name.ToString().ToLowerInvariant() == "option")
                        {
                            NodeConnection optionConnection = new NodeConnection(newNode, NodeConnectionType.Option)
                            {
                                OptionText = xElement.GetAttributeString("text", string.Empty),
                                EndConversation = xElement.GetAttributeBool("endconversation", false)
                            };
                            newNode.Connections.Add(optionConnection);
                        }
                    }

                    foreach (NodeConnection connection in newNode.Connections)
                    {
                        if (connection.Type == NodeConnectionType.Value)
                        {
                            foreach (XAttribute attribute in subElement.Attributes())
                            {
                                if (string.Equals(connection.Attribute, attribute.Name.ToString(), StringComparison.InvariantCultureIgnoreCase) && connection.ValueType != null)
                                {
                                    if (connection.ValueType.IsEnum)
                                    {
                                        Array values = Enum.GetValues(connection.ValueType);
                                        foreach (object? @enum in values)
                                        {
                                            if (string.Equals(@enum?.ToString(), attribute.Value, StringComparison.InvariantCultureIgnoreCase))
                                            {
                                                connection.OverrideValue = @enum;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        connection.OverrideValue = Convert.ChangeType(attribute.Value, connection.ValueType);
                                    }
                                }
                            }
                        }
                    }

                    if (npos == defaultNodePos)
                    {
                        hadNodes = false;
                        bool Predicate(EditorNode node) => Rectangle.Union(node.GetDrawRectangle(), node.HeaderRectangle).Intersects(Rectangle.Union(newNode.GetDrawRectangle(), newNode.HeaderRectangle));

                        while (nodeList.Any(Predicate))
                        {
                            EditorNode? otherNode = nodeList.Find(Predicate);
                            if (otherNode != null)
                            {
                                newNode.Position += new Vector2(128, otherNode.GetDrawRectangle().Height + otherNode.HeaderRectangle.Height + new Random().Next(128, 256));
                            }
                        }
                    }

                    if (parentElement?.FirstElement() == subElement)
                    {
                        switch (parentElement?.Name.ToString().ToLowerInvariant())
                        {
                            case "failure":
                                parent?.Connect(newNode, NodeConnectionType.Failure);
                                break;
                            case "success":
                                parent?.Connect(newNode, NodeConnectionType.Success);
                                break;
                            case "option":
                                if (parent != null)
                                {
                                    NodeConnection? activateConnection = newNode.Connections.Find(connection => connection.Type == NodeConnectionType.Activate);
                                    NodeConnection? optionConnection = parent.Connections.FirstOrDefault(connection =>
                                        connection.Type == NodeConnectionType.Option && string.Equals(connection.OptionText, parentElement.GetAttributeString("text", string.Empty), StringComparison.Ordinal));

                                    if (activateConnection != null)
                                    {
                                        optionConnection?.ConnectedTo.Add(activateConnection);
                                    }
                                }
                                break;
                            default:
                                parent?.Connect(newNode, NodeConnectionType.Add);
                                break;
                        }
                    }
                    else
                    {
                        lastNode?.Connect(newNode, NodeConnectionType.Next);
                    }

                    lastNode = newNode;
                    nodeList.Add(newNode);
                    ident += 600;
                    CreateNodes(subElement, ref hadNodes, newNode, ident);
                }
                else
                {
                    
                }
            }
        }

        private static RectTransform RectTransform(float x, float y, GUIComponent parent, Anchor anchor = Anchor.TopRight)
        {
            return new RectTransform(new Vector2(x, y), parent.RectTransform, anchor);
        }

        public override void Select()
        {
            GUI.PreventPauseMenuToggle = false;
            projectName = TextManager.Get("EventEditor.Unnamed");
            base.Select();
        }

        public override void Deselect()
        {
            base.Deselect();
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        private XElement? ExportXML()
        {
            XElement mainElement = new XElement("ScriptedEvent", new XAttribute("identifier", projectName.RemoveWhitespace().ToLowerInvariant()));
            EditorNode? startNode = null;
            foreach (EditorNode eventNode in nodeList.Where(node => node is EventNode || node is SpecialNode))
            {
                if (eventNode.GetParent() == null)
                {
                    if (startNode != null)
                    {
                        DebugConsole.ThrowError("You have more than one start node, only one will be picked while the others will get ignored.");
                    }
                    startNode ??= eventNode;
                }
            }

            if (startNode == null) { return null; }

            ExportChildNodes(startNode, mainElement);

            return mainElement;
        }

        private void ExportChildNodes(EditorNode startNode, XElement parent)
        {
            XElement? newElement = startNode.ToXML();
            if (newElement == null) { return; }
            parent.Add(newElement);

            EditorNode? success = startNode.GetNext(NodeConnectionType.Success);
            EditorNode? failure = startNode.GetNext(NodeConnectionType.Failure);
            EditorNode? add = startNode.GetNext(NodeConnectionType.Add);
            Tuple<EditorNode?, string?, bool>[] options = startNode is EventNode eNode ? eNode.GetOptions() : new Tuple<EditorNode?, string?, bool>[0];

            if (success != null)
            {
                XElement successElement = new XElement("Success");
                ExportChildNodes(success, successElement);
                newElement.Add(successElement);
            }

            if (failure != null)
            {
                XElement failureElement = new XElement("Failure");
                ExportChildNodes(failure, failureElement);
                newElement.Add(failureElement);
            }

            if (add is CustomNode custom)
            {
                ExportChildNodes(custom, newElement);
            }

            foreach (var (node, text, end) in options)
            {
                XElement optionElement = new XElement("Option");
                optionElement.Add(new XAttribute("text", text ?? ""));
                if (end) { optionElement.Add(new XAttribute("endconversation", true)); }

                if (node is EventNode eventNode)
                {
                    ExportChildNodes(eventNode, optionElement);
                }

                newElement.Add(optionElement);
            }

            EditorNode? next = startNode.GetNext();
            if (next != null)
            {
                ExportChildNodes(next, parent);
            }
        }

        private XElement SaveEvent(string name)
        {
            XElement mainElement = new XElement("SavedEvent", new XAttribute("name", name));
            XElement nodes = new XElement("Nodes");
            foreach (var editorNode in nodeList)
            {
                nodes.Add(editorNode.Save());
            }

            mainElement.Add(nodes);

            XElement connections = new XElement("AllConnections");
            foreach (var editorNode in nodeList)
            {
                connections.Add(editorNode.SaveConnections());
            }

            mainElement.Add(connections);
            return mainElement;
        }

        private void Load(XElement saveElement)
        {
            nodeList.Clear();
            projectName = saveElement.GetAttributeString("name", TextManager.Get("EventEditor.Unnamed"));
            foreach (XElement element in saveElement.Elements())
            {
                switch (element.Name.ToString().ToLowerInvariant())
                {
                    case "nodes":
                    {
                        foreach (XElement subElement in element.Elements())
                        {
                            EditorNode? node = EditorNode.Load(subElement);
                            if (node != null)
                            {
                                nodeList.Add(node);
                            }
                        }

                        break;
                    }
                    case "allconnections":
                    {
                        foreach (XElement subElement in element.Elements())
                        {
                            int id = subElement.GetAttributeInt("i", -1);
                            EditorNode? node = nodeList.Find(editorNode => editorNode.ID == id);
                            node?.LoadConnections(subElement);
                        }

                        break;
                    }
                }
            }
        }

        private void CreateContextMenu(EditorNode node, NodeConnection? connection = null)
        {
            if (GUIContextMenu.CurrentContextMenu != null) { return; }

            GUIContextMenu.CreateContextMenu(
                new ContextMenuOption("EventEditor.Edit", isEnabled: node is ValueNode || connection?.Type == NodeConnectionType.Value || connection?.Type == NodeConnectionType.Option, onSelected: delegate
                {
                    CreateEditMenu(node as ValueNode, connection);
                }),
                new ContextMenuOption("EventEditor.MarkEnding", isEnabled: connection != null && connection.Type == NodeConnectionType.Option, onSelected: delegate
                {
                    if (connection == null) { return; }

                    connection.EndConversation = !connection.EndConversation;
                }),
                new ContextMenuOption("EventEditor.RemoveConnection", isEnabled: connection != null, onSelected: delegate
                {
                    if (connection == null) { return; }

                    connection.ClearConnections();
                    connection.OverrideValue = null;
                    connection.OptionText = connection.OptionText;
                }),
                new ContextMenuOption("EventEditor.AddOption", isEnabled: node.CanAddConnections, onSelected: node.AddOption),
                new ContextMenuOption("EventEditor.RemoveOption", isEnabled: connection != null && node.RemovableTypes.Contains(connection.Type), onSelected: delegate
                {
                    connection?.Parent.RemoveOption(connection);
                }),
                new ContextMenuOption("EventEditor.Delete", isEnabled: true, onSelected: delegate
                {
                    nodeList.Remove(node);
                    node.ClearConnections();
                }));
        }
        
        private bool CreateTestSetupMenu()
        {
            var msgBox = new GUIMessageBox(TextManager.Get("EventEditor.TestPromptHeader"), "", new[] { TextManager.Get("Cancel"), TextManager.Get("OK") },  
                relativeSize: new Vector2(0.2f, 0.3f), minSize: new Point(300, 175));

            var layout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), msgBox.Content.RectTransform));

            new GUITextBlock(new RectTransform(new Vector2(1, 0.25f), layout.RectTransform), TextManager.Get("EventEditor.OutpostGenParams"), font: GUI.SubHeadingFont);
            GUIDropDown paramInput = new GUIDropDown(new RectTransform(new Vector2(1, 0.25f), layout.RectTransform), string.Empty, OutpostGenerationParams.Params.Count);
            foreach (OutpostGenerationParams param in OutpostGenerationParams.Params)
            {
                paramInput.AddItem(param.Identifier, param);
            }
            paramInput.OnSelected = (_, param) =>
            {
                lastTestParam = param as OutpostGenerationParams;
                return true;
            };
            paramInput.SelectItem(lastTestParam ?? OutpostGenerationParams.Params.FirstOrDefault());

            new GUITextBlock(new RectTransform(new Vector2(1, 0.25f), layout.RectTransform), TextManager.Get("EventEditor.LocationType"), font: GUI.SubHeadingFont);
            GUIDropDown typeInput = new GUIDropDown(new RectTransform(new Vector2(1, 0.25f), layout.RectTransform), string.Empty, LocationType.List.Count);
            foreach (LocationType type in LocationType.List)
            {
                typeInput.AddItem(type.Identifier, type);
            }
            typeInput.OnSelected = (_, type) =>
            {
                lastTestType = type as LocationType;
                return true;
            };
            typeInput.SelectItem(lastTestType ?? LocationType.List.FirstOrDefault());

            // Cancel button
            msgBox.Buttons[0].OnClicked = (button, o) =>
            {
                msgBox.Close();
                return true;
            };

            // Ok button
            msgBox.Buttons[1].OnClicked = (button, o) =>
            {
                TestEvent(lastTestParam, lastTestType);
                msgBox.Close();
                return true;
            };

            return true;
        }

        private static void CreateEditMenu(ValueNode? node, NodeConnection? connection = null)
        {
            object? newValue;
            Type? type;
            if (node != null)
            {
                newValue = node.Value;
                type = node.Type;
            }
            else if (connection != null)
            {
                newValue = connection.OverrideValue;
                type = connection.ValueType;
            }
            else
            {
                return;
            }

            if (connection?.Type == NodeConnectionType.Option)
            {
                newValue = connection.OptionText;
                type = typeof(string);
            }

            if (type == null) { return; }

            Vector2 size = type == typeof(string) ? new Vector2(0.2f, 0.3f) : new Vector2(0.2f, 0.175f);
            var msgBox = new GUIMessageBox(TextManager.Get("EventEditor.Edit"), "", new[] { TextManager.Get("Cancel"), TextManager.Get("OK") }, size, minSize: new Point(300, 175));


            Vector2 layoutSize = type == typeof(string) ? new Vector2(1f, 0.5f) : new Vector2(1f, 0.25f);
            var layout = new GUILayoutGroup(new RectTransform(layoutSize, msgBox.Content.RectTransform), isHorizontal: true);

            if (type.IsEnum)
            {
                Array enums = Enum.GetValues(type);
                GUIDropDown valueInput = new GUIDropDown(new RectTransform(Vector2.One, layout.RectTransform), newValue?.ToString(), enums.Length);
                foreach (object? @enum in enums) { valueInput.AddItem(@enum?.ToString(), @enum); }

                valueInput.OnSelected += (component, o) =>
                {
                    newValue = o;
                    return true;
                };
            }
            else
            {
                if (type == typeof(string))
                {
                    GUIListBox listBox = new GUIListBox(new RectTransform(Vector2.One, layout.RectTransform)) { CanBeFocused = false };
                    GUITextBox valueInput = new GUITextBox(new RectTransform(Vector2.One, listBox.Content.RectTransform, Anchor.TopRight), wrap: true, style: "GUITextBoxNoBorder");
                    valueInput.OnTextChanged += (component, o) =>
                    {
                        Vector2 textSize = valueInput.Font.MeasureString(valueInput.WrappedText);
                        valueInput.RectTransform.NonScaledSize = new Point(valueInput.RectTransform.NonScaledSize.X, (int) textSize.Y + 10);
                        listBox.UpdateScrollBarSize();
                        listBox.BarScroll = 1.0f;
                        newValue = o;
                        return true;
                    };
                    valueInput.Text = newValue?.ToString() ?? "<type here>";
                }
                else if (type == typeof(float) || type == typeof(int))
                {
                    GUINumberInput valueInput = new GUINumberInput(new RectTransform(Vector2.One, layout.RectTransform), GUINumberInput.NumberType.Float) { FloatValue = (float) (newValue ?? 0.0f) };
                    valueInput.OnValueChanged += component => { newValue = component.FloatValue; };
                }
                else if (type == typeof(bool))
                {
                    GUITickBox valueInput = new GUITickBox(new RectTransform(Vector2.One, layout.RectTransform), "Value") { Selected = (bool) (newValue ?? false) };
                    valueInput.OnSelected += component =>
                    {
                        newValue = component.Selected;
                        return true;
                    };
                }
            }

            // Cancel button
            msgBox.Buttons[0].OnClicked = (button, o) =>
            {
                msgBox.Close();
                return true;
            };

            // Ok button
            msgBox.Buttons[1].OnClicked = (button, o) =>
            {
                if (node != null)
                {
                    node.Value = newValue;
                }
                else if (connection != null)
                {
                    if (connection.Type == NodeConnectionType.Option)
                    {
                        connection.OptionText = newValue?.ToString();
                    }
                    else
                    {
                        connection.ClearConnections();
                        connection.OverrideValue = newValue;
                    }
                }

                msgBox.Close();
                return true;
            };
        }

        private bool TestEvent(OutpostGenerationParams? param, LocationType? type)
        {
            SubmarineInfo subInfo = SubmarineInfo.SavedSubmarines.FirstOrDefault(info => info.HasTag(SubmarineTag.Shuttle));

            XElement? eventXml = ExportXML();
            EventPrefab? prefab;
            if (eventXml != null)
            { 
                prefab = new EventPrefab(eventXml);
            }
            else
            {
                GUI.AddMessage("Unable to open test enviroment because the event contains errors.", GUI.Style.Red);
                return false;
            }

            GameSession gameSession = new GameSession(subInfo, "", GameModePreset.TestMode, CampaignSettings.Empty, null);
            TestGameMode gameMode = (TestGameMode) gameSession.GameMode;

            gameMode.SpawnOutpost = true;
            gameMode.OutpostParams = param;
            gameMode.OutpostType = type;
            gameMode.TriggeredEvent = prefab;
            gameMode.OnRoundEnd = () =>
            {
                Submarine.Unload();
                GameMain.EventEditorScreen.Select();
            };

            GameMain.GameScreen.Select();
            gameSession.StartRound(null, false);
            return true;
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            DrawnTooltip = string.Empty;
            Cam.UpdateTransform();

            // "world" space
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, transformMatrix: Cam.Transform);
            graphics.Clear(new Color(0.2f, 0.2f, 0.2f, 1.0f));

            foreach (EditorNode node in nodeList.Where(node => node is SpecialNode))
            {
                node.Draw(spriteBatch);
            }
            
            // Render value nodes below event nodes
            foreach (EditorNode node in nodeList.Where(node => node is ValueNode))
            {
                node.Draw(spriteBatch);
            }

            foreach (EditorNode node in nodeList.Where(node => node is EventNode))
            {
                node.Draw(spriteBatch);
            }

            draggedNode?.Draw(spriteBatch);
            foreach (var (node, _) in markedNodes)
            {
                node.Draw(spriteBatch);
            }

            spriteBatch.End();

            // GUI
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState);
            GUI.Draw(Cam, spriteBatch);

            if (!string.IsNullOrWhiteSpace(DrawnTooltip))
            {
                string tooltip = ToolBox.WrapText(DrawnTooltip, 256.0f, GUI.SmallFont);
                GUI.DrawString(spriteBatch, PlayerInput.MousePosition + new Vector2(32, 32), tooltip, Color.White, Color.Black * 0.8f, 4, GUI.SmallFont);
            }

            spriteBatch.End();
        }

        public override void Update(double deltaTime)
        {
            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y)
            {
                CreateGUI();
            }

            Cam.MoveCamera((float) deltaTime, allowMove: true, allowZoom: GUI.MouseOn == null);
            Vector2 mousePos = Cam.ScreenToWorld(PlayerInput.MousePosition);
            mousePos.Y = -mousePos.Y;

            foreach (EditorNode node in nodeList)
            {
                if (PlayerInput.PrimaryMouseButtonDown())
                {
                    NodeConnection? connection = node.GetConnectionOnMouse(mousePos);
                    if (connection != null && connection.Type.NodeSide == NodeConnectionType.Side.Right)
                    {
                        if (connection.Type != NodeConnectionType.Out)
                        {
                            if (connection.ConnectedTo.Any()) { return; }
                        }

                        DraggedConnection = connection;
                    }
                }

                // ReSharper disable once AssignmentInConditionalExpression
                if (node.IsHighlighted = node.HeaderRectangle.Contains(mousePos))
                {
                    if (PlayerInput.PrimaryMouseButtonDown())
                    {
                        // Ctrl + clicking the headers add them to the "selection" that allows us to drag multiple nodes at once
                        if (PlayerInput.IsCtrlDown())
                        {
                            if (selectedNodes.Contains(node))
                            {
                                selectedNodes.Remove(node);
                            }
                            else
                            {
                                selectedNodes.Add(node);
                            }

                            node.IsSelected = selectedNodes.Contains(node);
                            break;
                        }

                        draggedNode = node;
                        dragOffset = draggedNode.Position - mousePos;
                        foreach (EditorNode selectedNode in selectedNodes)
                        {
                            if (!markedNodes.ContainsKey(selectedNode))
                            {
                                markedNodes.Add(selectedNode, selectedNode.Position - mousePos);
                            }
                        }
                    }
                }

                if (PlayerInput.SecondaryMouseButtonClicked())
                {
                    NodeConnection? connection = node.GetConnectionOnMouse(mousePos);
                    if (node.GetDrawRectangle().Contains(mousePos) || connection != null)
                    {
                        CreateContextMenu(node, node.GetConnectionOnMouse(mousePos));
                        break;
                    }
                }
            }

            if (PlayerInput.SecondaryMouseButtonClicked())
            {
                foreach (var selectedNode in selectedNodes)
                {
                    selectedNode.IsSelected = false;
                }

                selectedNodes.Clear();
            }

            if (draggedNode != null)
            {
                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    draggedNode = null;
                    markedNodes.Clear();
                }
                else
                {
                    Vector2 offsetChange = Vector2.Zero;
                    draggedNode.IsHighlighted = true;
                    draggedNode.Position = mousePos + dragOffset;

                    if (PlayerInput.KeyHit(Keys.Up)) { offsetChange.Y--; }

                    if (PlayerInput.KeyHit(Keys.Down)) { offsetChange.Y++; }

                    if (PlayerInput.KeyHit(Keys.Left)) { offsetChange.X--; }

                    if (PlayerInput.KeyHit(Keys.Right)) { offsetChange.X++; }

                    dragOffset += offsetChange;

                    foreach (var (editorNode, offset) in markedNodes.Where(pair => pair.Key != draggedNode))
                    {
                        editorNode.Position = mousePos + offset;
                    }

                    if (offsetChange != Vector2.Zero)
                    {
                        foreach (var (key, value) in markedNodes.ToList())
                        {
                            markedNodes[key] = value + offsetChange;
                        }
                    }
                }
            }

            if (DraggedConnection != null)
            {
                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    foreach (EditorNode node in nodeList)
                    {
                        var nodeOnMouse = node.GetConnectionOnMouse(mousePos);
                        if (nodeOnMouse != null && nodeOnMouse != DraggedConnection && nodeOnMouse.Type.NodeSide == NodeConnectionType.Side.Left)
                        {
                            if (!DraggedConnection.CanConnect(nodeOnMouse)) { continue; }

                            nodeOnMouse.ClearConnections();
                            DraggedConnection.Parent.Connect(DraggedConnection, nodeOnMouse);
                            break;
                        }
                    }

                    DraggedConnection = null;
                }
                else
                {
                    DraggingPosition = mousePos;
                }
            }
            else
            {
                DraggingPosition = Vector2.Zero;
            }

            if (PlayerInput.MidButtonHeld())
            {
                Vector2 moveSpeed = PlayerInput.MouseSpeed * (float) deltaTime * 60.0f / Cam.Zoom;
                moveSpeed.X = -moveSpeed.X;
                Cam.Position += moveSpeed;
            }

            base.Update(deltaTime);
        }
    }
}