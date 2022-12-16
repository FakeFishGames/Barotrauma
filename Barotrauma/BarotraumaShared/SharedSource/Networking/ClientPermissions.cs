using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    [Flags]
    public enum ClientPermissions
    {
        None = 0x0,
        ManageRound = 0x1,
        Kick = 0x2,
        Ban = 0x4,
        Unban = 0x8,
        SelectSub = 0x10,
        SelectMode = 0x20,
        ManageCampaign = 0x40,
        ConsoleCommands = 0x80,
        ServerLog = 0x100,
        ManageSettings = 0x200,
        ManagePermissions = 0x400,
        KarmaImmunity = 0x800,
        ManageMoney = 0x1000,
        SellInventoryItems = 0x2000,
        SellSubItems = 0x4000,
        ManageMap = 0x8000,
        ManageHires = 0x10000,
        ManageBotTalents = 0x20000,
        All = 0x3FFFF
    }

    class PermissionPreset
    {
        public static readonly List<PermissionPreset> List = new List<PermissionPreset>();
        
        public readonly LocalizedString Name;
        public readonly LocalizedString Description;
        public readonly ClientPermissions Permissions;
        public readonly HashSet<DebugConsole.Command> PermittedCommands;
        
        public PermissionPreset(XElement element)
        {
            string name = element.GetAttributeString("name", "");
            Name = TextManager.Get("permissionpresetname." + name).Fallback(name);
            Description = TextManager.Get("permissionpresetdescription." + name) .Fallback(element.GetAttributeString("description", ""));

            string permissionsStr = element.GetAttributeString("permissions", "");
            if (!Enum.TryParse(permissionsStr, out Permissions))
            {
                DebugConsole.ThrowError("Error in permission preset \"" + Name + "\" - " + permissionsStr + " is not a valid permission!");
            }

            PermittedCommands = new HashSet<DebugConsole.Command>();
            if (Permissions.HasFlag(ClientPermissions.ConsoleCommands))
            {
                foreach (var subElement in element.Elements())
                {
                    if (!subElement.Name.ToString().Equals("command", StringComparison.OrdinalIgnoreCase)) { continue; }
                    string commandName = subElement.GetAttributeString("name", "");

                    DebugConsole.Command command = DebugConsole.FindCommand(commandName);
                    if (command == null)
                    {
#if SERVER
                        DebugConsole.ThrowError("Error in permission preset \"" + Name + "\" - " + commandName + "\" is not a valid console command.");
#endif
                        continue;
                    }

                    PermittedCommands.Add(command);
                }
            }
        }

        public static void LoadAll(string file)
        {
            if (!File.Exists(file)) { return; }

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null) { return; }

            List.Clear();
            foreach (XElement element in doc.Root.Elements())
            {
                List.Add(new PermissionPreset(element));
            }
        }

        public bool MatchesPermissions(ClientPermissions permissions, ISet<DebugConsole.Command> permittedConsoleCommands)
        {
            return permissions == Permissions
                   && PermittedCommands.All(permittedConsoleCommands.Contains)
                   && permittedConsoleCommands.All(PermittedCommands.Contains);
        }
    }
}
