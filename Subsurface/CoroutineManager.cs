using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface
{
    enum Status
    {
        Running, Success, Failure
    }

    // Keeps track of all running coroutines, and runs them till the end.
    static class CoroutineManager
    {
        static List<IEnumerator<Status>> Coroutines = new List<IEnumerator<Status>>();

        // Starting a coroutine just means adding an enumerator to the list.
        // You might also want to be able to stop coroutines or delete them,
        // which might mean putting them into a dictionary
        public static  void StartCoroutine(IEnumerable<Status> func)
        {
            Coroutines.Add(func.GetEnumerator());
        }

        // Updating just means stepping through all the coroutines
        public static void Update()
        {
            for (int i = Coroutines.Count-1; i>=0; i--)
            {
                Coroutines[i].MoveNext();

                switch (Coroutines[i].Current)
                {
                    case Status.Success:
                        Coroutines.RemoveAt(i);
                        break;
                    case Status.Failure:
                        DebugConsole.ThrowError("Coroutine ''" + Coroutines[i]+ "'' has failed");
                        break;
                }
            }
        }
    }  
}
