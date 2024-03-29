using System.Threading;

namespace Barotrauma.Threading
{
    public readonly ref struct ReadLock
    {
        private readonly ReaderWriterLockSlim rwl;
        public ReadLock(ReaderWriterLockSlim rwl)
        {
            this.rwl = rwl;
            rwl.EnterReadLock();
        }

        public void Dispose()
        {
            rwl.ExitReadLock();
        }
    }
        
    public readonly ref struct WriteLock
    {
        private readonly ReaderWriterLockSlim rwl;
        public WriteLock(ReaderWriterLockSlim rwl)
        {
            this.rwl = rwl;
            rwl.EnterWriteLock();
        }

        public void Dispose()
        {
            rwl.ExitWriteLock();
        }
    }
}