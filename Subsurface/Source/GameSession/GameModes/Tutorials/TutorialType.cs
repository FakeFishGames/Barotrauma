using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Barotrauma.Tutorials
{
    class TutorialType
    {

        public static List<TutorialType> TutorialTypes;

        protected GUIComponent infoBox;

        Character character;


        public string Name
        {
            get;
            private set;
        }

        static TutorialType()
        {
            TutorialTypes = new List<TutorialType>();

            TutorialTypes.Add(new BasicTutorial("Basic tutorial"));

        }

        public TutorialType(string name)
        {
            this.Name = name;
        }

        public virtual void Initialize()
        {

            GameMain.GameSession = new GameSession(Submarine.MainSub, "", GameModePreset.list.Find(gm => gm.Name.ToLowerInvariant() == "tutorial"));
            (GameMain.GameSession.gameMode as TutorialMode).tutorialType = this;

            GameMain.GameSession.StartShift("tuto1");

            GameMain.GameSession.TaskManager.Tasks.Clear();

            GameMain.GameScreen.Select();
        }

        public virtual void Start()
        {

            WayPoint wayPoint = WayPoint.GetRandom(SpawnType.Cargo, null);
            if (wayPoint == null)
            {
                DebugConsole.ThrowError("A waypoint with the spawntype \"cargo\" is required for the tutorial event");
                return;
            }

            CharacterInfo charInfo = new CharacterInfo(Character.HumanConfigFile, "", Gender.None, JobPrefab.List.Find(jp => jp.Name == "Engineer"));

            character = Character.Create(charInfo, wayPoint.WorldPosition, false, false);
            Character.Controlled = character;
            character.GiveJobItems(null);

            var idCard = character.Inventory.FindItem("ID Card");
            if (idCard == null)
            {
                DebugConsole.ThrowError("Item prefab \"ID Card\" not found!");
                return;
            }
            idCard.AddTag("com");
            idCard.AddTag("eng");

            //CoroutineManager.StartCoroutine(QuitChecker());
            CoroutineManager.StartCoroutine(UpdateState());
        }

        public virtual void Update(float deltaTime)
        {
            if (character!=null)
            {
                if (Character.Controlled==null)
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


            //CrewManager.Update(deltaTime);

            if (infoBox != null) infoBox.Update(deltaTime);
        }

        public virtual IEnumerable<object> UpdateState()
        {
            yield return CoroutineStatus.Success;
        }

        public virtual void Draw(SpriteBatch spriteBatch)
        {

            if (infoBox != null) infoBox.Draw(spriteBatch);
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

            var infoBlock = new GUIFrame(new Rectangle(-20, 20, width, height), null, Alignment.TopRight, GUI.Style);
            //infoBlock.Color = infoBlock.Color * 0.8f;
            infoBlock.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
            infoBlock.Flash(Color.Green);

            var textBlock = new GUITextBlock(new Rectangle(10, 10, width - 40, height), text, GUI.Style, infoBlock, true);

            if (hasButton)
            {
                var okButton = new GUIButton(new Rectangle(0, -40, 80, 25), "OK", Alignment.BottomCenter, GUI.Style, textBlock);
                okButton.OnClicked = CloseInfoFrame;
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
