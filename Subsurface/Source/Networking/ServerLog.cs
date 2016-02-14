using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    class ServerLog
    {
        const int LinesPerFile = 500;

        const string SavePath = "ServerLogs";

        private string serverName;

        private Queue<string> lines;

        public ServerLog(string serverName)
        {
            this.serverName = serverName;

            lines = new Queue<string>();
        }

        public void WriteLine(string line)
        {
            string logLine = "[" + DateTime.Now.ToLongTimeString() + "] " + line;

            lines.Enqueue(logLine);

            if (lines.Count>=LinesPerFile)
            {
                Save();
            }
        }

        public void Save()
        {
            if (!Directory.Exists(SavePath))
            {
                try
                {
                    Directory.CreateDirectory(SavePath);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to create a folder for server logs", e);
                    return;
                }                
            }

            string fileName = serverName+"_"+DateTime.Now.ToShortDateString()+"_"+DateTime.Now.ToShortTimeString()+".txt";

            fileName = fileName.Replace(":", "");

            string filePath = Path.Combine(SavePath, fileName);

            try
            {
                File.WriteAllLines(filePath, lines);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving the server log to " + filePath + " failed", e);
            }
        }
    }
}
