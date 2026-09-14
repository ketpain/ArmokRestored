using System;
using System.IO;

namespace Armok.Lifecycle
{
    public static class SuspendedOperation
    {
        // A lost resume acknowledgement is a failed session, never a claimed success.
        // Closing the client is the only remaining cleanup; the peer owns its suspend lock.
        public static void Run(Func<int> suspend, Func<int> resume, Action interrupt, Action operation)
        {
            try { if (suspend() <= 0) throw new IOException("DF did not acknowledge suspension."); }
            catch { interrupt(); throw; }
            Exception failure = null;
            try { operation(); }
            catch (Exception error) { failure = error; }
            try
            {
                if (resume() < 0) throw new IOException("DF did not acknowledge resume.");
            }
            catch (Exception error)
            {
                interrupt();
                var resumeFailure = new IOException("DF resume was not confirmed; the connection was closed.", error);
                if (failure != null) throw new AggregateException(failure, resumeFailure);
                throw resumeFailure;
            }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
