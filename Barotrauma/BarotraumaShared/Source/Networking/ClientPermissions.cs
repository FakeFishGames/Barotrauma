using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    [Flags]
    enum ClientPermissions
    {
        None = 0,
        [Description("End round")]
        EndRound = 1,
        [Description("Kick")]
        Kick = 2,
        [Description("Ban")]
        Ban = 4,
        [Description("Select submarine")]
        SelectSub = 8,
        [Description("Select game mode")]
        SelectMode = 16,
        [Description("Manage campaign")]
        ManageCampaign = 32,
        [Description("Console commands")]
        ConsoleCommands = 64
    }

    class PermissionPreset
    {
        public static List<PermissionPreset> List = new List<PermissionPreset>();
           
        public readonly string Name;
        public readonly string Description;
        public readonly ClientPermissions Permissions;
        public readonly List<DebugConsole.Command> PermittedCommands;
        
        public PermissionPreset(XElement element)
        {
            Name = element.GetAttributeString("name", "");
            Description = element.GetAttributeString("description", "");

            string permissionsStr = element.GetAttributeString("permissions", "");
            if (!Enum.TryParse(permissionsStr, out Permissions))
            {
                DebugConsole.ThrowError("Error in permission preset \"" + Name + "\" - " + permissionsStr + " is not a valid permission!");
            }

            PermittedCommands = new List<DebugConsole.Command>();
            if (Permissions.HasFlag(ClientPermissions.ConsoleCommands))
            {
                foreach (XElement subElement in element.Elements())
                {
                    if (subElement.Name.ToString().ToLowerInvariant() != "command") continue;
                    string commandName = subElement.GetAttributeString("name", "");

                    DebugConsole.Command command = DebugConsole.FindCommand(commandName);
                    if (command == null)
                    {
                        DebugConsole.ThrowError("Error in permission preset \"" + Name + "\" - " + commandName + "\" is not a valid console command.");
                        continue;
                    }

                    PermittedCommands.Add(command);
                }
            }
        }

        public static void LoadAll(string file)
        {
            if (!File.Exists(file)) return;

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            foreach (XElement element in doc.Root.Elements())
            {
                List.Add(new PermissionPreset(element));
            }
        }
    }
}
