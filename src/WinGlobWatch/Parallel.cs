using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinGlobWatch
{
    public static class Parallel
    {
        public static async Task ForEach<TElement>(IEnumerable<TElement> elements, Action<TElement> perform, int mdop = -1)
        {
            if (mdop == -1)
            {
                mdop = Environment.ProcessorCount*2;
            }

            if (mdop < 2)
            {
                mdop = 2;
            }

            SynchronizingEnumerator<TElement> enumerator = new SynchronizingEnumerator<TElement>(elements.GetEnumerator());
            Task[] waiters = new Task[mdop - 1];
            HashSet<Exception> exceptions = new HashSet<Exception>();

            TElement current;
            while (enumerator.TryTakeNext(out current))
            {
                TElement localCurrent = current;

                int slot = await FindFreeSlotAsync(waiters, exceptions);
                waiters[slot] = Task.Run(() => perform(localCurrent));
            }

            try
            {
                await Task.WhenAll(waiters.Where(x => x != null));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }
        }

        private static int FindFreeSlot(Task[] waiters, ICollection<Exception> exceptions)
        {
            for (int i = 0; i < waiters.Length; ++i)
            {
                if (waiters[i] == null)
                {
                    return i;
                }

                if (waiters[i].IsCanceled || waiters[i].IsFaulted)
                {
                    if (waiters[i].Exception != null)
                    {
                        exceptions.Add(waiters[i].Exception);
                    }

                    return i;
                }

                if (waiters[i].IsCompleted)
                {
                    return i;
                }
            }

            return -1;
        }

        private static async Task<int> FindFreeSlotAsync(Task[] waiters, ICollection<Exception> exceptions)
        {
            int freeSlot = FindFreeSlot(waiters, exceptions);

            if (freeSlot == -1)
            {
                try
                {
                    await Task.WhenAny(waiters.Where(x => x != null));
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }

                freeSlot = FindFreeSlot(waiters, exceptions);
            }

            return freeSlot;
        }

        private class SynchronizingEnumerator<TElement> : IDisposable
        {
            private readonly IEnumerator<TElement> _underlyingEnumerator;
            private long _isDisposed;
            private int _sync;

            public SynchronizingEnumerator(IEnumerator<TElement> elements)
            {
                _underlyingEnumerator = elements;
            }

            public void Dispose()
            {
                while (Interlocked.CompareExchange(ref _sync, 1, 0) != 0)
                {
                }

                Interlocked.Exchange(ref _isDisposed, 1);
                _underlyingEnumerator.Dispose();
                Interlocked.Exchange(ref _sync, 0);
            }

            public bool TryTakeNext(out TElement value)
            {
                if (Interlocked.Read(ref _isDisposed) == 1)
                {
                    value = default(TElement);
                    return false;
                }

                while (Interlocked.CompareExchange(ref _sync, 1, 0) != 0)
                {
                    if (Interlocked.Read(ref _isDisposed) == 1)
                    {
                        value = default(TElement);
                        return false;
                    }
                }

                if (!_underlyingEnumerator.MoveNext())
                {
                    value = default(TElement);
                    return false;
                }

                value = _underlyingEnumerator.Current;
                Interlocked.Exchange(ref _sync, 0);
                return true;
            }
        }
    }
}