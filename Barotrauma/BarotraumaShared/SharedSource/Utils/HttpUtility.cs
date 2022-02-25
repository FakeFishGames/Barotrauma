// 
// Formerly System.Web.HttpUtility, now it just vaguely resembles it
//
// Authors:
//   Patrik Torstensson (Patrik.Torstensson@labs2.com)
//   Wictor WilÃ©n (decode/encode functions) (wictor@ibizkit.se)
//   Tim Coleman (tim@timcoleman.com)
//   Gonzalo Paniagua Javier (gonzalo@ximian.com)
//
// Copyright (C) 2005-2010 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System.Collections.Generic;

namespace Barotrauma
{
    public sealed class HttpUtility
    {
        public static Dictionary<Identifier, string> ParseQueryString(string query)
        {
            Dictionary<Identifier, string> collection = new Dictionary<Identifier, string>();
            var splitGet = query.Split('?');
            if (splitGet.Length > 1)
            {
                var get = splitGet[1];
                foreach (string kvp in get.Split('&'))
                {
                    var splitKeyValue = kvp.Split('=');
                    if (splitKeyValue.Length > 1)
                    {
                        collection.Add(splitKeyValue[0].ToIdentifier(), splitKeyValue[1]);
                    }
                }
            }
            return collection;
        }
    }
}
