using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Saves several keys together: all of them or none (like a SQL transaction / TransactionScope).<br/>
	/// <code>
	/// using (var tx = Save.BeginTransaction())
	/// {
	///     Save.SetAsync("quest", quest);
	///     Save.SetAsync("money", money);
	///     tx.Commit();
	/// }
	/// </code>
	/// - Every SetAsync inside the block (even called deep inside other code) joins the transaction.<br/>
	/// - Leaving the block without Commit() (exception, early return, forgotten) discards ALL changes
	///   and logs a warning. The save on disk stays as it was.<br/>
	/// - Nested transactions join the outermost one; if any of them is not committed, everything is discarded.<br/>
	/// - Must be opened and disposed on the same thread (main thread). Do not await inside.<br/>
	/// - Set / SetAsync(forceSync) / Delete / Flush are written immediately, not part of the transaction.
	/// </summary>
	public sealed class SaveTransaction : IDisposable
	{
		// Per-thread state of the outermost open transaction.
		[ThreadStatic] private static int _depth;
		[ThreadStatic] private static Dictionary<string, byte[]> _items;
		[ThreadStatic] private static bool _aborted;

		private readonly int _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
		private bool _committed;
		private bool _disposed;

		internal SaveTransaction()
		{
			if (_depth++ == 0)
			{
				_items = new Dictionary<string, byte[]>();
				_aborted = false;
			}
		}

		/// <summary>Mark the changes as complete. They are saved when the block ends.</summary>
		public void Commit()
		{
			if (_disposed)
				throw new InvalidOperationException("SaveTransaction is already closed.");
			_committed = true;
		}

		/// <summary>Add to the open transaction of this thread. False if none is open.</summary>
		internal static bool TryAdd(string key, byte[] bytes)
		{
			if (_depth <= 0 || _items == null)
				return false;

			_items[key] = bytes;
			return true;
		}

		/// <summary>Drop a key from the open transaction (sync writes / Delete must not be overwritten by it).</summary>
		internal static void Remove(string key)
		{
			if (_depth > 0 && _items != null)
				_items.Remove(key);
		}

		internal static void Reset()
		{
			_depth = 0;
			_items = null;
			_aborted = false;
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			// State is per thread: closing elsewhere (e.g. after an await) would leave
			// the opening thread's transaction open forever and silently swallow its saves.
			if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
				throw new InvalidOperationException("SaveTransaction must be disposed on the thread that opened it. Do not await inside a transaction.");

			_disposed = true;

			if (!_committed)
				_aborted = true;

			if (--_depth > 0)
				return;

			var items = _items;
			var aborted = _aborted;
			_items = null;
			_aborted = false;

			if (aborted)
			{
				if (items != null && items.Count > 0)
				{
					Debug.LogWarning($"[SaveManager] Transaction closed without Commit(), {items.Count} change(s) discarded: {string.Join(", ", items.Keys)}");
					Save.RaiseTransactionDiscarded(new List<string>(items.Keys));
				}
				return;
			}

			if (items != null && items.Count > 0)
				SaveQueue.Enqueue(items);
		}
	}
}
