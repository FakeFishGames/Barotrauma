using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Barotrauma
{
    public class Md5Hash
    {
        private string hash;
        private string shortHash;

        public string Hash
        {
            get 
            {
                return hash;
            }  
        }

        public string ShortHash
        {
            get 
            { 
                return shortHash;
            }
        }

        public Md5Hash(string md5Hash)
        {
            this.hash = md5Hash;

            shortHash = GetShortHash(md5Hash);
        }

        public Md5Hash(byte[] bytes)
        {
            hash = CalculateHash(bytes);

            shortHash = GetShortHash(hash);
        }

        public Md5Hash(FileStream fileStream)
        {
            hash = CalculateHash(fileStream);

            shortHash = GetShortHash(hash);
        }

        public Md5Hash(XDocument doc)
        {
            if (doc == null) return;

            string docString = Regex.Replace(doc.ToString(), @"\s+", "");

            byte[] inputBytes = Encoding.ASCII.GetBytes(docString);

            hash = CalculateHash(inputBytes);

            shortHash = GetShortHash(hash);
        }

        public override string ToString()
        {
            return hash;
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
