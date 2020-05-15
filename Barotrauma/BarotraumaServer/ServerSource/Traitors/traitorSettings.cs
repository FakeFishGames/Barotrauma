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
        public string traitorSelectMode;
        public float traitorRatio;
        public float traitorRandomFactor;
        public int traitorStaticNumber;
        public int traitorRandomMin;
        public int traitorRandomMax;

        private Dictionary<string, string> data = new Dictionary<string, string>();

        public traitorSettings()
        {
            readSettings("traitorSettings.txt");
            
            data.TryGetValue("traitorSelectMode", out string output);
            traitorSelectMode = Convert.ToString(output);
            
            data.TryGetValue("traitorRatio", out string output2);
            traitorRatio = float.Parse(output2,System.Globalization.CultureInfo.InvariantCulture);

            data.TryGetValue("traitorStaticNumber", out string output3);
            traitorStaticNumber = Int32.Parse(output3);

            data.TryGetValue("traitorRandomMin", out string output4);
            traitorRandomMin = Int32.Parse(output4);

            data.TryGetValue("traitorRandomMax", out string output5);
            traitorRandomMax = Int32.Parse(output5);

            data.TryGetValue("traitorRandomFactor", out string output6);
            traitorRandomFactor = float.Parse(output6, System.Globalization.CultureInfo.InvariantCulture);
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
