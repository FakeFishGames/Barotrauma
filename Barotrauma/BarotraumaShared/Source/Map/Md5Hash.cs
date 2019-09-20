using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Barotrauma
{
    public class Md5Hash
    {
        private static Regex removeWhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        public Md5Hash(FileStream fileStream)
        {
            Hash = CalculateHash(fileStream);

            ShortHash = GetShortHash(Hash);
        }

        public Md5Hash(XDocument doc)
        {
            if (doc == null) { return; }
            
            string docString = removeWhitespaceRegex.Replace(doc.ToString(), "");
            
            byte[] inputBytes = Encoding.ASCII.GetBytes(docString);
            
            Hash = CalculateHash(inputBytes);            
            ShortHash = GetShortHash(Hash);
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
            return fullHash.Length < 7 ? fullHash : fullHash.Substring(0, 7);
        }
    }
}
