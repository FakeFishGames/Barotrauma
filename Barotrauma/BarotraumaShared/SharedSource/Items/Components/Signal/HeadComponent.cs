using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class HeadComponent : StringComponent
    {
        /*
            signal_in1 - Input String
            signal_in2 - Input Limit/Divider
            signal_out - Output all characters in String LEFT of the first Limit
        */
        
        public SplitComponent(Item item, XElement element)
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
                    return signal1.Substring(0,iter - index); //Returns text before signal 2
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
            if(index > 0) { //If limit is at end of string
                return signal1.Substring(0, signal1.Length - index);
            }
            return ""; //Returns empty
        }


    }
}
