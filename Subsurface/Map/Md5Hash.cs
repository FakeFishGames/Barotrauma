using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Subsurface
{
    class Md5Hash
    {
        private string md5Hash;
        private string shortHash;

        public string MD5Hash
        {
            get 
            {
                return md5Hash;
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
            this.md5Hash = md5Hash;

            shortHash = GetShortHash(md5Hash);
        }

        public Md5Hash(XDocument doc)
        {
            string docString = Regex.Replace(doc.ToString(), @"\s+", "");
            // step 1, calculate MD5 hash from input

            MD5 md5 = MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(docString);
            byte[] hash = md5.ComputeHash(inputBytes);

            // step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }

            md5Hash = sb.ToString();

            shortHash = GetShortHash(md5Hash);
        }

        private string GetShortHash(string fullHash)
        {
            return fullHash;
        }
    }
}
