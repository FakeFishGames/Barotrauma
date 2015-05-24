//using Microsoft.Xna.Framework;
//using Microsoft.Xna.Framework.Graphics;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//namespace Subsurface
//{
//    static class EventManager
//    {
//        static Event activeEvent;

//        static GUIFrame infoPanel;

//        const int MaxPreviousEvents = 6;
//        const float PreviouslyUsedWeight = 10.0f;

//        static List<Event> previousEvents = new List<Event>();

//        public static Event ActiveEvent
//        {
//            get { return activeEvent; }
//        }
        
//        public static void StartShift()
//        {
//            int eventCount = Event.list.Count();

//            //create an array where the probability of an event being selected will be calculated
//            float[] eventProbability = new float[eventCount];

//            float probabilitySum = 0.0f;

//            for (int i = 0; i < eventCount; i++)
//            {
//                eventProbability[i] = Event.list[i].Commonness;
                
//                //if the event has been previously selected, it's less likely to be selected now
//                int previousEventIndex = previousEvents.FindIndex(x => x == Event.list[i]);
//                if (previousEventIndex>=0)
//                {
//                    //how many shifts ago was the event last selected
//                    int eventDist = eventCount - previousEventIndex;

//                    float weighting = (1.0f / eventDist) * PreviouslyUsedWeight;

//                    eventProbability[i] *= weighting;
//                }

//                probabilitySum += eventProbability[i];
//            }

//            float randomNumber = (float)Game1.random.NextDouble()*probabilitySum;

//            for (int i = 0; i < eventCount; i++)
//            {
//                if (randomNumber <= eventProbability[i])
//                {
//                    SelectEvent(Event.list[i]);
//                    return;
//                }

//                randomNumber -= eventProbability[i];
//            }
//        }

//        public static void SelectEvent(Event selectedEvent)
//        {
//            if (selectedEvent == null) return;

//            activeEvent = selectedEvent;
//            previousEvents.Add(activeEvent);

//            activeEvent.Init();
//        }

//        public static void Update(GameTime gameTime)
//        {
//            if (activeEvent==null) return;
//            activeEvent.Update(gameTime);
//        }

//        public static void DrawInfo(SpriteBatch spriteBatch)
//        {
//            if (activeEvent==null || !activeEvent.IsStarted) return;

//            if (infoPanel == null)
//            {
//                infoPanel = new GUIFrame(new Rectangle(Game1.GraphicsWidth - 320, 20, 300, 100), Color.White * 0.8f);
//                infoPanel.Padding = GUI.style.smallPadding;
//                infoPanel.AddChild(new GUITextBlock(new Rectangle(0, 0, 200, 20), activeEvent.Name, Color.Transparent, Color.Black));
//                infoPanel.AddChild(new GUITextBlock(new Rectangle(0, 0, 200, 50), activeEvent.Description, Color.Transparent, Color.Black));
//            }


//            //infoPanel.Draw(spriteBatch);
//        }

//        public static void EventFinished(Event e)
//        {
//            if (e != activeEvent) return;
//            infoPanel.AddChild(new GUITextBlock(new Rectangle(0, 0, 200, 80), "Finished!", Color.Transparent, Color.Black));
//        }
//    }
//}
