using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    abstract class Tutorial
    {
        public static List<Tutorial> Tutorials;

        protected GUIComponent infoBox;

        private XElement configElement;

        private Character character;

        private SpawnType spawnPointType;

        private string submarinePath;
        private string levelSeed;

        public string Name
        {
            get;
            private set;
        }

        private bool completed;
        public bool Completed
        {
            get { return completed; }
            protected set
            {
                if (completed == value) return;
                completed = value;
                GameMain.Config.Save();
            }
        }

        public static void Init()
        {
            Tutorials = new List<Tutorial>();
            foreach (string file in GameMain.Instance.GetFilesOfType(ContentType.Tutorials))
            {
                XDocument doc = XMLExtensions.TryLoadXml(file);
                if (doc?.Root == null) continue;

                foreach (XElement element in doc.Root.Elements())
                {
                    Tutorial newTutorial = Load(element);
                    if (newTutorial != null) Tutorials.Add(newTutorial);
                }
            }
        }

        private static Tutorial Load(XElement element)
        {
            Type t;
            string type = element.Name.ToString().ToLowerInvariant();
            try
            {
                // Get the type of a specified class.                
                t = Type.GetType("Barotrauma.Tutorials." + type + "", false, true);
                if (t == null)
                {
                    DebugConsole.ThrowError("Could not find tutorial type \"" + type + "\"");
                    return null;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Could not find tutorial type \"" + type + "\"", e);
                return null;
            }

            ConstructorInfo constructor;
            try
            {
                if (!t.IsSubclassOf(typeof(Tutorial))) return null;
                constructor = t.GetConstructor(new Type[] { typeof(XElement) });
                if (constructor == null)
                {
                    DebugConsole.ThrowError("Could not find the constructor of tutorial type \"" + type + "\"");
                    return null;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Could not find the constructor of tutorial type \"" + type + "\"", e);
                return null;
            }
            Tutorial tutorial = null;
            try
            {
                object component = constructor.Invoke(new object[] { element });
                tutorial = (Tutorial)component;
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Error while loading tutorial of the type " + t + ".", e.InnerException);
            }

            return tutorial;
        }

        public Tutorial(XElement element)
        {
            configElement = element;
            Name = element.GetAttributeString("name", "Unnamed");
            submarinePath = element.GetAttributeString("submarinepath", "");
            levelSeed = element.GetAttributeString("levelseed", "tuto");

            Completed = GameMain.Config.CompletedTutorialNames.Contains(Name);

            Enum.TryParse(element.GetAttributeString("spawnpointtype", "Human"), true, out spawnPointType);
        }
        
        public void Initialize()
        {
            GameMain.Instance.ShowLoading(Loading());
        }

        private IEnumerable<object> Loading()
        {
            yield return CoroutineStatus.Running;

            Submarine.MainSub = Submarine.Load(submarinePath, "", true);
            yield return CoroutineStatus.Running;

            GameMain.GameSession = new GameSession(Submarine.MainSub, "", GameModePreset.list.Find(gm => gm.Name.ToLowerInvariant() == "tutorial"));
            (GameMain.GameSession.GameMode as TutorialMode).tutorial = this;
            GameMain.GameSession.StartRound(levelSeed);
            GameMain.GameSession.EventManager.Events.Clear();
            GameMain.GameScreen.Select();

            yield return CoroutineStatus.Success;
        }

        public virtual void Start()
        {
            WayPoint wayPoint = WayPoint.GetRandom(spawnPointType, null);
            if (wayPoint == null)
            {
                DebugConsole.ThrowError("A waypoint with the spawntype \"" + spawnPointType + "\" is required for the tutorial event");
                return;
            }

            CharacterInfo charInfo = configElement.Element("Character") == null ?                
                new CharacterInfo(Character.HumanConfigFile, "", Gender.None, JobPrefab.List.Find(jp => jp.Identifier == "engineer")) :
                new CharacterInfo(configElement.Element("Character"));
            
            character = Character.Create(charInfo, wayPoint.WorldPosition, "", false, false);
            Character.Controlled = character;
            character.GiveJobItems(null);

            var idCard = character.Inventory.FindItemByIdentifier("idcard");
            if (idCard == null)
            {
                DebugConsole.ThrowError("Item prefab \"ID Card\" not found!");
                return;
            }
            idCard.AddTag("com");
            idCard.AddTag("eng");
            
            CoroutineManager.StartCoroutine(UpdateState());
        }

        public virtual void AddToGUIUpdateList()
        {
            if (infoBox != null) infoBox.AddToGUIUpdateList();
        }

        public virtual void Update(float deltaTime)
        {
            if (character != null)
            {
                if (Character.Controlled == null)
                {
                    CoroutineManager.StopCoroutines("TutorialMode.UpdateState");
                    infoBox = null;
                }
                else if (Character.Controlled.IsDead)
                {
                    Character.Controlled = null;

                    CoroutineManager.StopCoroutines("TutorialMode.UpdateState");
                    infoBox = null;
                    CoroutineManager.StartCoroutine(Dead());
                }
            }
        }

        public virtual IEnumerable<object> UpdateState()
        {
            yield return CoroutineStatus.Success;
        }

        private IEnumerable<object> Dead()
        {
            yield return new WaitForSeconds(3.0f);

            var messageBox = new GUIMessageBox("You have died", "Do you want to try again?", new string[] { "Yes", "No" });

            messageBox.Buttons[0].OnClicked += Restart;
            messageBox.Buttons[0].OnClicked += messageBox.Close;


            messageBox.Buttons[1].OnClicked = GameMain.MainMenuScreen.SelectTab;
            messageBox.Buttons[1].OnClicked += messageBox.Close;

            yield return CoroutineStatus.Success;
        }


        protected bool CloseInfoFrame(GUIButton button, object userData)
        {
            infoBox = null;
            return true;
        }

        protected GUIComponent CreateInfoFrame(string text, bool hasButton = false)
        {
            int width = 300;
            int height = hasButton ? 110 : 80;

            string wrappedText = ToolBox.WrapText(text, width, GUI.Font);

            height += wrappedText.Split('\n').Length * 25;

            var infoBlock = new GUIFrame(new RectTransform(new Point(width, height), GUI.Canvas, Anchor.TopRight) { AbsoluteOffset = new Point(20) });
            infoBlock.Flash(Color.Green);

            var textBlock = new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.7f), infoBlock.RectTransform, Anchor.Center),
                text, wrap: true);

            if (hasButton)
            {
                var okButton = new GUIButton(new RectTransform(new Point(80, 25), infoBlock.RectTransform, Anchor.BottomCenter) { AbsoluteOffset = new Point(0, 5) },
                    TextManager.Get("OK"))
                {
                    OnClicked = CloseInfoFrame
                };
            }
            
            GUI.PlayUISound(GUISoundType.Message);

            return infoBlock;
        }


        private bool Restart(GUIButton button, object obj)
        {
            TutorialMode.StartTutorial(this);

            return true;
        }
    }
}
