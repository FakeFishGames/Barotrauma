using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class TailComponent : StringComponent
    {
        /*
            signal_in1 - Input String
            signal_in2 - Input Limit/Divider
            signal_out - Output all characters in String RIGHT of the first Limit
        */

        public TailComponent(Item item, XElement element)
            : base(item, element)
        {
        }

        protected override string Calculate(string signal1, string signal2)
        {
            int index = 0;
            for (int iter = 0; iter < signal1.Length; iter++)
            {
                if (index >= signal2.Length)
                {
                    //Indicates there is a match
                    return signal1.Substring(iter); //Returns text after signal_2
                }

                if (signal2[index] == signal1[iter])
                { //If this letter matches with the i'th signal_2 letter
                    index++; // Next check is on next letter in signal_2
                }
                else
                {
                    index = 0; //Next check is for first character in signal_2
                }
            }
            return ""; //Returns empty signal
        }
    }
}
