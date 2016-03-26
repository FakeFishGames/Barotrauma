using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    enum CoroutineStatus
    {
        Running, Success, Failure
    }

    // Keeps track of all running coroutines, and runs them till the end.
    static class CoroutineManager
    {
        static readonly List<IEnumerator<object>> Coroutines = new List<IEnumerator<object>>();

        public static float UnscaledDeltaTime, DeltaTime;

        // Starting a coroutine just means adding an enumerator to the list.
        // You might also want to be able to stop coroutines or delete them,
        // which might mean putting them into a dictionary
        public static void StartCoroutine(IEnumerable<object> func)
        {
            Coroutines.Add(func.GetEnumerator());
        }

        public static bool IsCoroutineRunning(string name)
        {
            IEnumerator<object> coroutine = Coroutines.FirstOrDefault(
                c => c.ToString().Contains(name));

            return coroutine!=null;
        }
        
        public static void StopCoroutine(string name)
        {
            IEnumerator<object> coroutine = Coroutines.FirstOrDefault(c => c.ToString().Contains(name));

            if (coroutine != null) Coroutines.Remove(coroutine);
        }

        // Updating just means stepping through all the coroutines
        public static void Update(float unscaledDeltaTime, float deltaTime)
        {
            UnscaledDeltaTime = unscaledDeltaTime;
            DeltaTime = deltaTime;

            for (int i = Coroutines.Count-1; i>=0; i--)
            {
                if (Coroutines[i].Current != null)
                {
                    WaitForSeconds wfs = Coroutines[i].Current as WaitForSeconds;
                    if (wfs != null)
                    {
                        if (!wfs.CheckFinished(unscaledDeltaTime)) continue;
                    }
                    else
                    {
                        switch ((CoroutineStatus)Coroutines[i].Current)
                        {
                            case CoroutineStatus.Success:
                                Coroutines.RemoveAt(i);
                                continue;
                            case CoroutineStatus.Failure:
                                DebugConsole.ThrowError("Coroutine ''" + Coroutines[i]+ "'' has failed");
                                break;
                        }
                    }
                }

                try
                {
                    Coroutines[i].MoveNext();
                }

                catch (Exception e)
                {                    
#if DEBUG
                    DebugConsole.ThrowError("Coroutine " + Coroutines[i] + " threw an exception: " + e.Message);
#endif
                    Coroutines.RemoveAt(i);
                }

            }
        }
    }
  
    class WaitForSeconds
    {
        float timer;


        public WaitForSeconds(float time)
        {
            timer = time;
        }

        public bool CheckFinished(float deltaTime) 
        {
            timer -= deltaTime;
            return timer<=0.0f;
        }
    }


}
