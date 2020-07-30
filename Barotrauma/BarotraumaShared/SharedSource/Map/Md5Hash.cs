using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    public class Md5Hash
    {
        private static readonly Regex removeWhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const string cachePath = "Data/hashcache.txt";
        private static readonly Dictionary<string, Tuple<Md5Hash, long>> cache = new Dictionary<string, Tuple<Md5Hash, long>>();

        public static void LoadCache()
        {
            try
            {
                if (!File.Exists(cachePath)) { return; }
                string[] lines = File.ReadAllLines(cachePath, Encoding.UTF8);
                if (lines.Length <= 0 || lines[0] != GameMain.Version.ToString()) { return; }
                foreach (string line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) { continue; }
                    string[] parts = line.Split('|');
                    if (parts.Length < 3) { continue; }

                    string path = parts[0].CleanUpPath();
                    string hashStr = parts[1];
                    long timeLong = long.Parse(parts[2]);

                    Md5Hash hash = new Md5Hash(hashStr);
                    DateTime time = DateTime.FromBinary(timeLong);

                    if (File.GetLastWriteTime(path) == time && !cache.ContainsKey(path))
                    {
                        cache.Add(path, new Tuple<Md5Hash, long>(hash, timeLong));
                    }
                }
            }
            catch (Exception e)
            {
                DebugConsole.NewMessage($"Failed to load hash cache: {e.Message}\n{e.StackTrace}", Microsoft.Xna.Framework.Color.Orange);
                cache.Clear();
            }
        }

        public static void SaveCache()
        {
#if SERVER
            //don't save to the cache if the server is owned by a client,
            //since this suggests that they're running concurrently and
            //will interfere with each other here
            if (GameMain.Server?.OwnerConnection != null) { return; }
#endif

            string[] lines = new string[cache.Count + 1];
            lines[0] = GameMain.Version.ToString();
            int i = 1;
            foreach (KeyValuePair<string, Tuple<Md5Hash, long>> kpv in cache)
            {
                lines[i] = kpv.Key + "|" + kpv.Value.Item1 + "|" + kpv.Value.Item2;
                i++;
            }
            File.WriteAllLines(cachePath, lines, Encoding.UTF8);
        }

        private bool LoadFromCache(string filename)
        {
            if (!string.IsNullOrWhiteSpace(filename))
            {
                filename = filename.CleanUpPath();
                lock (cache)
                {
                    if (cache.ContainsKey(filename))
                    {
                        Hash = cache[filename].Item1.Hash;
                        ShortHash = cache[filename].Item1.ShortHash;
                        return true;
                    }
                }
            }
            return false;
        }

        public void SaveToCache(string filename, long? time = null)
        {
            if (string.IsNullOrWhiteSpace(filename)) { return; }
            
            lock (cache)
            {
                filename = filename.CleanUpPath();
                Tuple<Md5Hash, long> cacheVal = new Tuple<Md5Hash, long>(this, time ?? File.GetLastWriteTime(filename).ToBinary());
                if (cache.ContainsKey(filename))
                {
                    cache[filename] = cacheVal;
                }
                else
                {
                    cache.Add(filename, cacheVal);
                }
                SaveCache();
            }            
        }

        public static Md5Hash FetchFromCache(string filename)
        {
            Md5Hash newHash = new Md5Hash();
            if (newHash.LoadFromCache(filename)) { return newHash; }
            return null;
        }

        public string Hash { get; private set; }

        public string ShortHash { get; private set; }

        private Md5Hash()
        {
            this.Hash = null;
            ShortHash = null;
        }

        public Md5Hash(string md5Hash)
        {
            this.Hash = md5Hash;
            ShortHash = GetShortHash(md5Hash);
        }

        public Md5Hash(byte[] bytes)
        {
            Hash = CalculateHash(bytes);

            ShortHash = GetShortHash(Hash);
        }

        public Md5Hash(FileStream fileStream, string filename = null, bool tryLoadFromCache = true)
        {
            if (tryLoadFromCache)
            {
                if (LoadFromCache(filename)) { return; }
            }

            Hash = CalculateHash(fileStream);

            ShortHash = GetShortHash(Hash);

            SaveToCache(filename);
        }

        public Md5Hash(XDocument doc, string filename = null, bool tryLoadFromCache = true)
        {
            if (tryLoadFromCache)
            {
                if (LoadFromCache(filename)) { return; }
            }

            if (doc == null) { return; }
            
            string docString = removeWhitespaceRegex.Replace(doc.ToString(), "");
            
            byte[] inputBytes = Encoding.ASCII.GetBytes(docString);
            
            Hash = CalculateHash(inputBytes);
            ShortHash = GetShortHash(Hash);

            SaveToCache(filename);
        }

        public override string ToString()
        {
            return Hash;
        }

        private string CalculateHash(FileStream stream)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] byteHash = md5.ComputeHash(stream);
                // step 2, convert byte array to hex string
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < byteHash.Length; i++)
                {
                    sb.Append(byteHash[i].ToString("X2"));
                }

                return sb.ToString();
            }
        }

        private string CalculateHash(byte[] bytes)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] byteHash = md5.ComputeHash(bytes);
                // step 2, convert byte array to hex string
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < byteHash.Length; i++)
                {
                    sb.Append(byteHash[i].ToString("X2"));
                }

                return sb.ToString();
            }
        }

        public static string GetShortHash(string fullHash)
        {
            if (string.IsNullOrEmpty(fullHash)) { return ""; }
            return fullHash.Length < 7 ? fullHash : fullHash.Substring(0, 7);
        }

        public static bool RemoveFromCache(string filename)
        {
            if (!string.IsNullOrWhiteSpace(filename))
            {
                filename = filename.CleanUpPath();
                lock (cache)
                {
                    if (cache.ContainsKey(filename))
                    {
                        cache.Remove(filename);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
