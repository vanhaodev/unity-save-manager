using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace vanhaodev.savemanager
{
    /// <summary>
    /// Save hooks. Subscribe to show a "saving" icon, popups, analytics, cheat reports...<br/>
    /// - Always delivered on the main thread (safe to touch UI), on the next frame
    ///   even for sync calls, so a handler can never run in the middle of a save.<br/>
    /// - An exception in a handler is logged and never breaks saving.<br/>
    /// - Events posted right before the app quits may not be delivered.
    /// </summary>
    public static partial class Save
    {
        /// <summary>A save of these keys started writing to disk.</summary>
        public static event Action<IReadOnlyList<string>> SaveStarted;

        /// <summary>These keys are safely on disk (deleted keys are included).</summary>
        public static event Action<IReadOnlyList<string>> Saved;

        /// <summary>
        /// Writing these keys failed (disk full, file locked...). The previous save on disk is intact;
        /// the data is kept and written again on the next SetAsync / Set / Flush.
        /// </summary>
        public static event Action<IReadOnlyList<string>, Exception> SaveFailed;

        /// <summary>
        /// Save files are broken, swapped or edited outside the game.
        /// The library already fell back to the last valid save (or Get throws).
        /// </summary>
        public static event Action<string> IntegrityFailed;

        /// <summary>A transaction closed without Commit(); these changes were not saved.</summary>
        public static event Action<IReadOnlyList<string>> TransactionDiscarded;

        private static SynchronizationContext _mainContext;

        // Unity's main-thread context; null outside Unity (handlers then run inline).
        private static void CaptureMainThread()
        {
            _mainContext ??= SynchronizationContext.Current;
        }

        private static void ResetEvents()
        {
            SaveStarted = null;
            Saved = null;
            SaveFailed = null;
            IntegrityFailed = null;
            TransactionDiscarded = null;
            _mainContext = SynchronizationContext.Current;
        }

        internal static void RaiseSaveStarted(IReadOnlyList<string> keys) => Dispatch(() => InvokeEach(SaveStarted, h => h(keys)));
        internal static void RaiseSaved(IReadOnlyList<string> keys) => Dispatch(() => InvokeEach(Saved, h => h(keys)));
        internal static void RaiseSaveFailed(IReadOnlyList<string> keys, Exception error) => Dispatch(() => InvokeEach(SaveFailed, h => h(keys, error)));
        internal static void RaiseIntegrityFailed(string message) => Dispatch(() => InvokeEach(IntegrityFailed, h => h(message)));
        internal static void RaiseTransactionDiscarded(IReadOnlyList<string> keys) => Dispatch(() => InvokeEach(TransactionDiscarded, h => h(keys)));

        /// <summary>Run on the main thread on a later frame (inline outside Unity).</summary>
        internal static void RunOnMainThreadLater(Action action) => Dispatch(action);

        private static void Dispatch(Action raise)
        {
            var context = _mainContext;
            if (context == null)
            {
                raise();
                return;
            }

            context.Post(_ => raise(), null);
        }

        // Call every subscriber separately: one throwing handler must not skip the others.
        private static void InvokeEach<T>(T handlers, Action<T> call) where T : Delegate
        {
            if (handlers == null)
                return;

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    call((T)handler);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
