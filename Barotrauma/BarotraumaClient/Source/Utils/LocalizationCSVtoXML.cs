using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Barotrauma
{
    class LocalizationCSVtoXML
    {
        private enum XMLTypes { Undefined = 0, InfoText = 1, Conversations = 2 }
        private static Regex csvSplit = new Regex("(?:^|,)(\"(?:[^\"])*\"|[^,]*)", RegexOptions.Compiled); // Handling commas inside data fields surrounded by ""
        private static List<int> conversationClosingIndent = new List<int>();
        private static char[] separator = new char[1] { ',' };

        public static void Convert(string language)
        {
            string csvFilePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            string[] files = Directory.GetFiles(csvFilePath, "*.csv");

            if (files.Length > 0)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    Console.WriteLine();
                    string[] csvContent = File.ReadAllLines(files[i], Encoding.UTF8);
                    List<string> xmlContent = ConvertToXML(File.ReadAllLines(files[i], Encoding.UTF8), language);
                    string xmlFileName = Path.GetFileName(files[i]).Split('.')[0];
                    string xmlFileFullPath = csvFilePath + '\\' + xmlFileName + ".xml";
                    File.WriteAllLines(xmlFileFullPath, xmlContent);
                    Console.WriteLine(".xml file successfully created at: " + xmlFileFullPath);
                }
            }
            else
            {
                Console.WriteLine("No .csv files found!");
            }

            Console.WriteLine("Press any key to quit.");
            Console.ReadKey();
        }

        private static List<string> ConvertToXML(string[] csvContent, string language)
        {
            List<string> xmlContent = new List<string>();
            xmlContent.Add("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>");

            XMLTypes xmlType = XMLTypes.Undefined;

            if (int.TryParse(csvContent[1][1].ToString(), out int startsWithNumber))
            {
                xmlType = XMLTypes.Conversations;
                xmlContent.Add($"<Conversations identifier=\"vanillaconversations\" Language=\"{language}\">");
                xmlContent.Add(string.Empty);
            }
            else
            {
                xmlType = XMLTypes.InfoText;
                xmlContent.Add($"<infotexts Language=\"{language}\">");
                xmlContent.Add(string.Empty);
            }

            if (xmlType == XMLTypes.InfoText)
            {
                for (int i = 0; i < csvContent.Length; i++)
                {
                    csvContent[i] = csvContent[i].Trim(separator);

                    if (csvContent[i].Length == 0)
                    {
                        xmlContent.Add(string.Empty);
                    }
                    else
                    {
                        string[] split = csvContent[i].Split(separator, 2);

                        if (split.Length == 2)
                        {
                            split[1] = split[1].Replace("\"", ""); // Replaces quotation marks around data that are added when exporting via excel
                            xmlContent.Add($"<{split[0]}>{split[1]}</{split[0]}>");
                        }
                        else if (split[0].Contains(".")) // An empty field
                        {
                            xmlContent.Add($"<{split[0]}><!-- No data --></{split[0]}>");
                        }
                        else // A header
                        {
                            xmlContent.Add($"<!-- {split[0]} -->");
                        }
                    }
                }
            }
            else if (xmlType == XMLTypes.Conversations)
            {
                for (int i = 0; i < csvContent.Length; i++)
                {
                    string[] split = SplitCSV(csvContent[i]);

                    int emptyFields = 0;

                    for (int j = 0; j < split.Length; j++)
                    {
                        if (split[j] == string.Empty) emptyFields++;
                    }

                    if (emptyFields == split.Length) // Empty line with only commas, indicates the end of the previous conversation
                    {
                        HandleClosingElements(xmlContent, 0);
                        xmlContent.Add(string.Empty);
                        continue;
                    }
                    else if (emptyFields == split.Length - 1 && split[0] != string.Empty) // A header
                    {
                        xmlContent.Add($"<!-- {split[0]} -->");
                        continue;
                    }

                    string speakerIndex = split[1];
                    int depthIndex = int.Parse(split[2]);
                    // 3 = original line
                    string line = split[4].Replace("\"", "");
                    string flags = split[5].Replace("\"", "");
                    string allowedJobs = split[6].Replace("\"", "");
                    string speakerTags = split[7].Replace("\"", "");
                    string minIntensity = split[8].Replace("\"", "");
                    string maxIntensity = split[9].Replace("\"", "");

                    string conversationLine = $"{GetIndenting(depthIndex)}<Conversation line=\"{line}\" {GetSpeaker(speakerIndex)}{GetFlags(flags)}{GetAllowedJobs(allowedJobs)}{GetSpeakerTags(speakerTags)}{GetMinIntensity(minIntensity)}{GetMaxIntensity(maxIntensity)}";

                    bool nextIsSubConvo = false;
                    int nextDepth = 999;

                    if (i < csvContent.Length - 1)
                    {
                        string[] trailingConvo = csvContent[i + 1].Split(separator);

                        if (trailingConvo[1] != string.Empty)
                        {
                            nextDepth = int.Parse(trailingConvo[2]);
                            nextIsSubConvo = nextDepth > depthIndex;
                        }

                        if (!nextIsSubConvo)
                        {
                            xmlContent.Add(conversationLine.TrimEnd() + "/>");
                            if (nextDepth < depthIndex)
                            {
                                HandleClosingElements(xmlContent, nextDepth);
                            }
                        }
                        else
                        {
                            xmlContent.Add(conversationLine.TrimEnd() + ">");
                            conversationClosingIndent.Add(depthIndex);
                        }
                    }
                    else
                    {
                        xmlContent.Add(conversationLine.TrimEnd() + "/>");
                    }
                }
            }

            if (xmlType == XMLTypes.Conversations)
            {
                xmlContent.Add(string.Empty);
                xmlContent.Add("</Conversations>");
            }
            else
            {
                xmlContent.Add(string.Empty);
                xmlContent.Add("</infotexts>");
            }

            return xmlContent;
        }

        private static void HandleClosingElements(List<string> xmlContent, int targetDepth)
        {
            if (conversationClosingIndent.Count == 0) return;

            for (int k = conversationClosingIndent.Count - 1; k >= 0; k--)
            {
                int currentIndent = conversationClosingIndent[k];
                if (currentIndent < targetDepth) break;
                xmlContent.Add($"{GetIndenting(currentIndent)}</Conversation>");
                conversationClosingIndent.RemoveAt(k);
            }
        }

        private static string[] SplitCSV(string input)
        {
            List<string> list = new List<string>();
            string curr = null;
            foreach (Match match in csvSplit.Matches(input))
            {
                curr = match.Value;
                if (0 == curr.Length)
                {
                    list.Add("");
                }

                list.Add(curr.TrimStart(','));
            }

            return list.ToArray();
        }

        private static string GetIndenting(int depthIndex)
        {
            string indenting = string.Empty;

            for (int i = 0; i < depthIndex; i++)
            {
                indenting += "\t";
            }

            return indenting;
        }

        private static string GetSpeaker(string speakerIndex)
        {
            if (speakerIndex == string.Empty)
            {
                return string.Empty;
            }
            else
            {
                return $"speaker=\"{speakerIndex}\" ";
            }
        }

        private static string GetFlags(string flags)
        {
            if (flags == string.Empty)
            {
                return string.Empty;
            }
            else
            {
                return $"flags=\"{flags}\" ";
            }
        }

        private static string GetAllowedJobs(string allowedJobs)
        {
            if (allowedJobs == string.Empty)
            {
                return string.Empty;
            }
            else
            {
                return $"allowedjobs=\"{allowedJobs}\" ";
            }
        }

        private static string GetSpeakerTags(string speakerTags)
        {
            if (speakerTags == string.Empty)
            {
                return string.Empty;
            }
            else
            {
                return $"speakertags=\"{speakerTags}\" ";
            }
        }

        private static string GetMinIntensity(string minIntensity)
        {
            if (minIntensity == string.Empty)
            {
                return string.Empty;
            }
            else
            {
                return $"minintensity=\"{minIntensity}\" ";
            }
        }

        private static string GetMaxIntensity(string maxIntensity)
        {
            if (maxIntensity == string.Empty)
            {
                return string.Empty;
            }
            else
            {
                return $"maxIntensity=\"{maxIntensity}\" ";
            }
        }
    }
}
