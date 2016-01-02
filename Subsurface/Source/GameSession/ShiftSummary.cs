using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class ShiftSummary
    {
        class Casualty
        {
            public readonly CharacterInfo character;
            public readonly CauseOfDeath causeOfDeath;

            public readonly string description;

            public Casualty(CharacterInfo characterInfo, CauseOfDeath causeOfDeath, string description)
            {
                this.character = characterInfo;
                this.causeOfDeath = causeOfDeath;
                this.description = description;
            }
        }

        private Location startLocation, endLocation;

        private GameSession gameSession;

        private List<Casualty> casualties;

        private Mission selectedMission;
               
        public ShiftSummary(GameSession gameSession)
        {
            this.gameSession = gameSession;

            startLocation = gameSession.Map.CurrentLocation;
            endLocation = gameSession.Map.SelectedLocation;

            casualties = new List<Casualty>();

            foreach (Character character in gameSession.CrewManager.characters)
            {
                character.OnDeath = AddCasualty;
            }

            selectedMission = gameSession.Mission;

        }


        public void AddCasualty(Character character, CauseOfDeath causeOfDeath)
        {
            casualties.Add(new Casualty(character.Info, causeOfDeath, ""));
        }

        public GUIFrame CreateSummaryFrame()
        {
            bool gameOver = !gameSession.CrewManager.characters.Any(c => !c.IsDead);
            bool progress = Submarine.Loaded.AtEndPosition;

            GUIFrame frame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.8f);

            int width = 700, height = 400;
            GUIFrame innerFrame = new GUIFrame(new Rectangle(0,0,width,height), null, Alignment.Center, GUI.Style, frame);

            int y = 0;
            string summaryText = InfoTextManager.GetInfoText(gameOver ? "gameover" :
                (progress ? "progress" : "return"));

            var infoText = new GUITextBlock(new Rectangle(0,y,0,50), summaryText, GUI.Style, innerFrame, true);
            y += infoText.Rect.Height;

            new GUITextBlock(new Rectangle(0,y,0,20), "Crew status:", GUI.Style, innerFrame, GUI.LargeFont);
            y += 30;

            int x = 0;
            foreach (Character character in gameSession.CrewManager.characters)
            {
                var characterFrame = new GUIFrame(new Rectangle(x,y,170,70), character.IsDead ? Color.DarkRed*0.7f : Color.Transparent, GUI.Style, innerFrame);
                characterFrame.Padding = new Vector4(5.0f,5.0f,5.0f,5.0f);
                character.Info.CreateCharacterFrame(characterFrame, 
                    character.Info.Job!=null ? (character.Info.Name + '\n'+"("+character.Info.Job.Name+")") : character.Info.Name, null);

                string statusText;
                Color statusColor;

                var casualty = casualties.Find(c => c.character == character.Info);

                if (casualty != null)
                {
                    statusText = InfoTextManager.GetInfoText("CauseOfDeath." + casualty.causeOfDeath.ToString());
                    statusColor = Color.DarkRed;
                }
                else
                {
                    statusText = (character.Health / character.MaxHealth > 0.8f) ? "OK" : "Injured";
                    statusColor = Color.DarkGreen;
                }
                
                new GUITextBlock(new Rectangle(0,0,0,20), statusText, 
                    GUI.Style, Alignment.BottomLeft, Alignment.TopCenter, characterFrame, true, GUI.SmallFont).Color = statusColor*0.7f;
                
                
                x += characterFrame.Rect.Width + 10;
            }

            y += 80;

            if (GameMain.GameSession.Mission != null)
            {
                new GUITextBlock(new Rectangle(0, y, 0, 20), "Mission: "+GameMain.GameSession.Mission.Name, GUI.Style, innerFrame, GUI.LargeFont);
                y += 30;
                
                new GUITextBlock(new Rectangle(0,y,0,30), 
                    (GameMain.GameSession.Mission.Completed) ? GameMain.GameSession.Mission.SuccessMessage : GameMain.GameSession.Mission.FailureMessage,
                    GUI.Style, innerFrame);

                if (GameMain.GameSession.Mission.Completed)
                {
                    new GUITextBlock(new Rectangle(0, y+40, 0, 30), "Reward: "+GameMain.GameSession.Mission.Reward, GUI.Style, innerFrame);
                }
  
            }

            return frame;
        }
    }
}
