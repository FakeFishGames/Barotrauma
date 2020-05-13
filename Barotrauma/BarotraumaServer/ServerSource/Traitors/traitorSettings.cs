using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Configuration;
using System.Collections.Specialized;

namespace Barotrauma.ServerSource.Traitors
{
    class traitorSettings
    {
        public bool useRatio;
        public float traitorRatio;
        public int traitorCount;

        private Dictionary<string, string> data = new Dictionary<string, string>();

        public traitorSettings()
        {
            readSettings("traitorSettings.txt");
            
            data.TryGetValue("useRatio", out string output);
            useRatio = Convert.ToBoolean(output);
            
            data.TryGetValue("traitorRatio", out string output2);
            traitorRatio = float.Parse(output2,System.Globalization.CultureInfo.InvariantCulture);

            data.TryGetValue("traitorCount", out string output3);
            traitorCount = Int32.Parse(output3);
        }
    
        private void readSettings(String path)
        {
            var fullPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, path);
            String[] lines = File.ReadAllLines(fullPath);
            DebugConsole.NewMessage(fullPath);
            DebugConsole.Log(fullPath);
            foreach (String line in lines)
            {
                String[] values = line.Split('=');
                DebugConsole.NewMessage(values[0]);
                DebugConsole.NewMessage(values[1]);
                DebugConsole.Log(values[0]);
                DebugConsole.Log(values[1]);
                data.Add(values[0], values[1]);
            }
        }
    }
}
