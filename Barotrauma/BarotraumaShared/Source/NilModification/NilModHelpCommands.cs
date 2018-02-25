using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Barotrauma.Networking;
using System.Xml.Linq;
using System.IO;

namespace Barotrauma
{
    class HelpTopic
    {
        public string Name;

        public List<string> Pages;
    }

        //Class for the storing, sending and receiving of HELP information to players and management of the HELP XML file
        class NilModHelpCommands
    {
        const string SavePath = "Data/NilModHelpData.xml";

        public string DefaultHelpstring;

        public List<HelpTopic> Topics;

        public void ReadHelpRequest(string HelpCommand, Client RequestingClient)
        {
            string[] separatedCommand = null;
            if (!string.IsNullOrWhiteSpace(HelpCommand.Trim()))
            {
                separatedCommand = HelpCommand.Split(' ');
            }
            string RequestedTopic;
            int PageNumber;
            if (RequestingClient != null)
            {
                DebugConsole.NewMessage("Help request from player: \"" + RequestingClient.Name + "\" with help command: \"" + HelpCommand + "\" Received", Microsoft.Xna.Framework.Color.White);
            }
            else
            {
                DebugConsole.NewMessage("Help request from player: \"" + "Host" + "\" with help command: \"" + HelpCommand + "\" Received", Microsoft.Xna.Framework.Color.White);
            }
            RequestedTopic = "";
            if (separatedCommand == null)
            {
                SendHelpRequest(null, 0, RequestingClient);
                return;
            }
            for (int i = 0; i < separatedCommand.Count() - 1; i++)
            {
                RequestedTopic += " " + separatedCommand[i];
            }
            if(separatedCommand.Count() == 1)
            {
                RequestedTopic = separatedCommand[0];
            }
            RequestedTopic = RequestedTopic.Trim();

            if (separatedCommand[separatedCommand.Count() - 1].All(Char.IsDigit) && (separatedCommand.Count() > 1))
            {
                if (!Topics.Any(tp => tp.Name.ToLowerInvariant() == RequestedTopic.ToLowerInvariant()))
                {
                    if (RequestingClient != null)
                    {
                        NilMod.NilModEventChatter.SendServerMessage("help topic not found, Format should be: help;topic name pagenumber|help;topic name", RequestingClient);
                    }
                    else
                    {
                        DebugConsole.NewMessage("help topic not found, Format should be: help;topic name pagenumber|help;topic name", Microsoft.Xna.Framework.Color.White);
                    }
                }
                else
                {
                    PageNumber = Convert.ToInt32(separatedCommand[separatedCommand.Count() - 1]);
                    SendHelpRequest(RequestedTopic, PageNumber, RequestingClient);
                    return;
                }
            }
            else if ((!Topics.Any(tp => (tp.Name.ToLowerInvariant() == (RequestedTopic + " " + separatedCommand[separatedCommand.Count() - 1]).ToLowerInvariant()))) && separatedCommand.Count() > 1)
            {
                if (RequestingClient != null)
                {
                    NilMod.NilModEventChatter.SendServerMessage("help topic not found, Format should be: help;topic name pagenumber|help;topic name", RequestingClient);
                }
                else
                {
                    DebugConsole.NewMessage("help topic not found, Format should be: help;topic name pagenumber|help;topic name", Microsoft.Xna.Framework.Color.White);
                }
            }
            else if (!Topics.Any(tp => (tp.Name.ToLowerInvariant() == RequestedTopic.ToLowerInvariant()) && separatedCommand.Count() == 1))
            {
                if (RequestingClient != null)
                {
                    NilMod.NilModEventChatter.SendServerMessage("help topic not found, Format should be: help;topic name pagenumber|help;topic name", RequestingClient);
                }
                else
                {
                    DebugConsole.NewMessage("help topic not found, Format should be: help;topic name pagenumber|help;topic name", Microsoft.Xna.Framework.Color.White);
                }
            }
            else
            {
                PageNumber = 0;
                SendHelpRequest(RequestedTopic, PageNumber, RequestingClient);
                return;
            }
        }

        public void SendHelpRequest(string topic, int page, Client RequestingClient)
        {
            HelpTopic chosentopic = null;
            if (topic != null)
            {
                chosentopic = Topics.Find(tp => tp.Name.ToLowerInvariant() == topic.ToLowerInvariant());
                if (chosentopic == null) return;
                if (chosentopic.Pages.Count() == 0) return;
                if (page >= chosentopic.Pages.Count()) page = chosentopic.Pages.Count() - 1;
            }

            if (RequestingClient != null)
            {
                var chatMsg = ChatMessage.Create(
                "Help System",
                (topic != null ? chosentopic.Pages[page] : DefaultHelpstring),
                (ChatMessageType)ChatMessageType.MessageBox,
                null);

                GameMain.Server.SendChatMessage(chatMsg, RequestingClient);
            }
            else
            {
                if(topic != null)
                {
                    DebugConsole.ExecuteCommand("messagebox " + chosentopic.Pages[page]);
                }
                else
                {
                    DebugConsole.ExecuteCommand("messagebox " + DefaultHelpstring);
                }
                
            }
        }

        public void ReportSettings()
        {
            
        }

        public void Load()
        {
            XDocument doc = null;

            if (File.Exists(SavePath))
            {
                doc = XMLExtensions.TryLoadXml(SavePath);
            }
            else
            {
                SaveDefault();
                //doc = ToolBox.TryLoadXml(SavePath);
            }
            if (doc != null)
            {
                XElement NilModHelpTopicsdoc = doc.Root.Element("HelpTopics");
                XElement NilModDefaultHelpTopicdoc = doc.Root.Element("DefaultHelpTopic");
                Topics = new List<HelpTopic>();

                DefaultHelpstring = NilModDefaultHelpTopicdoc.Element("Line").GetAttributeString("Text", "1").Trim();
                //DefaultHelpstring = ToolBox.GetAttributeString(NilModDefaultHelpTopicdoc, "Text", "1").Trim();

                //Load the help topics
                if (NilModHelpTopicsdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElementTopic in NilModHelpTopicsdoc.Elements())
                    {
                        HelpTopic newtopic = new HelpTopic();
                        newtopic.Name = subElementTopic.GetAttributeString("Name", "1").Trim();
                        newtopic.Pages = new List<string>();
                        foreach (XElement SubElementPage in subElementTopic.Elements())
                        {
                            newtopic.Pages.Add(SubElementPage.GetAttributeString("Text", ""));
                        }
                        Topics.Add(newtopic);
                    }
                }
            }
        }

        //The Default Help library in XML Format c:
        public void SaveDefault()
        {

        }
    }
}
