using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Pending saves + background worker.<br/>
	/// - Each worker pass takes everything pending and writes it as ONE commit,
	///   so items enqueued together (a batch) are never split across commits.<br/>
	/// - Debounce: saving a key again before it is written only keeps the newest data.<br/>
	/// - A failed commit puts its data back to pending (unless newer data arrived)
	///   and stops; it is retried by the next save / Flush, never in a loop.
	/// </summary>
	internal static class SaveQueue
	{
		// Guards _pending and _isRunning. Held only briefly, never during file IO,
		// so enqueueing from the main thread does not block on disk writes.
		// Lock order is always SaveFileStore.IoLock -> QueueLock.
		private static readonly object QueueLock = new();

		// key -> bytes, or key -> null for "remove key"
		private static Dictionary<string, byte[]> _pending = new();
		private static bool _isRunning;

		internal static void Reset()
		{
			lock (QueueLock)
			{
				_pending = new Dictionary<string, byte[]>();
				_isRunning = false;
			}
		}

		/// <summary>Queue items for the background worker. All items land in the same commit.</summary>
		internal static void Enqueue(IReadOnlyDictionary<string, byte[]> items)
		{
			lock (QueueLock)
			{
				foreach (var kv in items)
					_pending[kv.Key] = kv.Value;

				if (_isRunning) return;
				_isRunning = true;
			}

			Task.Run(ProcessQueue);
		}

		/// <summary>
		/// Commit everything pending plus items on the calling thread.
		/// Waits for an in-flight background commit first. Throws if writing fails
		/// (data stays pending and is retried by the next save).
		/// </summary>
		internal static void CommitNow(IReadOnlyDictionary<string, byte[]> items)
		{
			lock (SaveFileStore.IoLock)
			{
				Dictionary<string, byte[]> batch;
				lock (QueueLock)
				{
					foreach (var kv in items)
						_pending[kv.Key] = kv.Value;
					batch = TakePendingLocked();
				}

				if (batch.Count == 0)
					return;

				try
				{
					SaveManifestStore.Commit(batch);
				}
				catch
				{
					lock (QueueLock)
					{
						RequeueLocked(batch);
					}
					throw;
				}
			}
		}

		private static void ProcessQueue()
		{
			try
			{
				while (true)
				{
					// Take + write under IoLock so CommitNow/Delete never interleave with a commit.
					lock (SaveFileStore.IoLock)
					{
						Dictionary<string, byte[]> batch;
						lock (QueueLock)
						{
							if (_pending.Count == 0)
							{
								_isRunning = false;
								return;
							}

							batch = TakePendingLocked();
						}

						try
						{
							SaveManifestStore.Commit(batch);
						}
						catch (Exception e)
						{
							lock (QueueLock)
							{
								RequeueLocked(batch);
								_isRunning = false;
							}

							Debug.LogError($"[SaveManager] Background save failed, will retry on next save.\n{e}");
							return;
						}
					}
				}
			}
			catch (Exception e)
			{
				// Safety net: never leave the queue stuck in "running" state.
				Debug.LogError($"[SaveManager] Save queue worker crashed\n{e}");
				lock (QueueLock)
				{
					_isRunning = false;
				}
			}
		}

		private static Dictionary<string, byte[]> TakePendingLocked()
		{
			var batch = _pending;
			_pending = new Dictionary<string, byte[]>();
			return batch;
		}

		// Failed data goes back unless the game already queued something newer for that key.
		private static void RequeueLocked(Dictionary<string, byte[]> batch)
		{
			foreach (var kv in batch)
			{
				if (!_pending.ContainsKey(kv.Key))
					_pending[kv.Key] = kv.Value;
			}
		}
	}
}
