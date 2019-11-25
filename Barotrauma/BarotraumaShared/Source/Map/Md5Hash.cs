using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Barotrauma
{
    public class Md5Hash
    {
        private static readonly Regex removeWhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const string cachePath = "Data/hashcache.txt";
        private static readonly Dictionary<string, Tuple<Md5Hash, long>> cache = new Dictionary<string, Tuple<Md5Hash, long>>();

        public static void LoadCache()
        {
            if (!File.Exists(cachePath)) { return; }
            string[] lines = File.ReadAllLines(cachePath);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) { continue; }
                string[] parts = line.Split('|');
                string path = parts[0].Replace('\\','/');
                string hashStr = parts[1];
                long timeLong = long.Parse(parts[2]);

                Md5Hash hash = new Md5Hash(hashStr);
                DateTime time = DateTime.FromBinary(timeLong);

                if (File.GetLastWriteTime(path) == time)
                {
                    cache.Add(path, new Tuple<Md5Hash, long>(hash, timeLong));
                }
            }
        }

        public static void SaveCache()
        {
            string[] lines = new string[cache.Count];
            int i = 0;
            foreach (KeyValuePair<string, Tuple<Md5Hash, long>> kpv in cache)
            {
                lines[i] = kpv.Key + "|" + kpv.Value.Item1 + "|" + kpv.Value.Item2;
                i++;
            }
            File.WriteAllLines(cachePath, lines);
        }

        public string Hash { get; private set; }

        public string ShortHash { get; private set; }

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
            if (!string.IsNullOrWhiteSpace(filename))
            {
                filename = filename.Replace('\\', '/');
                if (tryLoadFromCache)
                {
                    lock (cache)
                    {
                        if (cache.ContainsKey(filename))
                        {
                            Hash = cache[filename].Item1.Hash;
                            ShortHash = cache[filename].Item1.ShortHash;
                            return;
                        }
                    }
                }
            }

            Hash = CalculateHash(fileStream);

            ShortHash = GetShortHash(Hash);

            if (!string.IsNullOrWhiteSpace(filename))
            {
                lock (cache)
                {
                    Tuple<Md5Hash, long> cacheVal = new Tuple<Md5Hash, long>(this, File.GetLastWriteTime(filename).ToBinary());
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
        }

        public Md5Hash(XDocument doc, string filename = null, bool tryLoadFromCache = true)
        {
            if (!string.IsNullOrWhiteSpace(filename))
            {
                filename = filename.Replace('\\', '/');
                if (tryLoadFromCache)
                {
                    lock (cache)
                    {
                        if (cache.ContainsKey(filename))
                        {
                            Hash = cache[filename].Item1.Hash;
                            ShortHash = cache[filename].Item1.ShortHash;
                            return;
                        }
                    }
                }
            }

            if (doc == null) { return; }
            
            string docString = removeWhitespaceRegex.Replace(doc.ToString(), "");
            
            byte[] inputBytes = Encoding.ASCII.GetBytes(docString);
            
            Hash = CalculateHash(inputBytes);
            ShortHash = GetShortHash(Hash);

            if (!string.IsNullOrWhiteSpace(filename))
            {
                lock (cache)
                {
                    Tuple<Md5Hash, long> cacheVal = new Tuple<Md5Hash, long>(this, File.GetLastWriteTime(filename).ToBinary());
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
    }
}
