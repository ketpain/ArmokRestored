using System;
using System.Collections;
using System.Collections.Generic;

namespace Armok.Lifecycle
{
    // Own nested enumerators so a child failure cannot silently strand its parent.
    public sealed class LoadOperation : IEnumerator, IDisposable
    {
        private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        private readonly Func<bool> cancelled;
        public Exception Failure { get; private set; }
        public bool Completed { get; private set; }
        public object Current { get; private set; }
        public LoadOperation(IEnumerator root, Func<bool> cancelled) { stack.Push(root); this.cancelled = cancelled; }
        public bool MoveNext()
        {
            try
            {
                while (stack.Count > 0)
                {
                    if (cancelled()) { Dispose(); return false; }
                    var iterator = stack.Peek();
                    if (!iterator.MoveNext()) { stack.Pop(); (iterator as IDisposable)?.Dispose(); continue; }
                    if (iterator.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    Current = iterator.Current; return true;
                }
                Completed = true;
            }
            catch (Exception error) { Failure = error; Dispose(); }
            return false;
        }
        public void Reset() => throw new NotSupportedException();
        public void Dispose()
        {
            while (stack.Count > 0)
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception error) { if (Failure == null) Failure = error; }
        }
    }
}
