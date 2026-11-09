using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace vanhaodev.savemanager
{
	/// <summary>Data waiting to be written for one key, plus the callers awaiting it.</summary>
	internal sealed class PendingWrite
	{
		public byte[] Data; // null = remove key
		public readonly List<TaskCompletionSource<bool>> Waiters = new();

		public PendingWrite(byte[] data)
		{
			Data = data;
		}

		// Continuations must not run on the worker thread while it holds the IO lock;
		// an awaiting Unity caller resumes on the main thread through its own context.
		internal static TaskCompletionSource<bool> NewWaiter() => new(TaskCreationOptions.RunContinuationsAsynchronously);

		/// <summary>Newer data for the same key: keep the newest bytes, everyone waits for it.</summary>
		public void MergeNewer(PendingWrite newer)
		{
			Data = newer.Data;
			Waiters.AddRange(newer.Waiters);
		}
	}

	/// <summary>
	/// Pending saves + background worker.<br/>
	/// - Each worker pass takes everything pending and writes it as ONE commit,
	///   so items enqueued together (a transaction) are never split across commits.<br/>
	/// - Debounce: saving a key again before it is written only keeps the newest data;
	///   callers awaiting the older data complete when the newer one is written.<br/>
	/// - A failed commit faults its awaiters, puts its data back to pending (unless newer
	///   data arrived) and stops; it is retried by the next save / Flush, never in a loop.
	/// </summary>
	internal static class SaveQueue
	{
		// Guards _pending, _flushWaiters and _isRunning. Held only briefly, never during file IO,
		// so enqueueing from the main thread does not block on disk writes.
		// Lock order is always SaveFileStore.IoLock -> QueueLock.
		private static readonly object QueueLock = new();

		private static Dictionary<string, PendingWrite> _pending = new();
		private static List<TaskCompletionSource<bool>> _flushWaiters = new();
		private static bool _isRunning;

		// WebGL only: the batch currently being written over several frames.
		private static SaveBatchCommit _active;

		// WebGL has no worker threads: writes run on the main thread on a later frame instead,
		// so the SetAsync call itself still never blocks.
		internal static bool RunOnMainThread =
#if UNITY_WEBGL && !UNITY_EDITOR
			true;
#else
			false;
#endif

		internal static void Reset()
		{
			lock (QueueLock)
			{
				_pending = new Dictionary<string, PendingWrite>();
				_flushWaiters = new List<TaskCompletionSource<bool>>();
				_isRunning = false;
				_active = null;
			}
		}

		/// <summary>Queue items for the background worker. All items land in the same commit.</summary>
		internal static void Enqueue(IReadOnlyDictionary<string, PendingWrite> items)
		{
			lock (QueueLock)
			{
				foreach (var kv in items)
					MergeLocked(kv.Key, kv.Value);

				if (!StartWorkerLocked()) return;
			}

			StartWorker();
		}

		/// <summary>Completes when everything pending right now (and in flight) is on disk.</summary>
		internal static Task FlushAsync()
		{
			var waiter = PendingWrite.NewWaiter();

			lock (QueueLock)
			{
				if (!_isRunning && _pending.Count == 0)
					return Task.CompletedTask;

				_flushWaiters.Add(waiter);
				if (!StartWorkerLocked()) return waiter.Task;
			}

			StartWorker();
			return waiter.Task;
		}

		/// <summary>
		/// Commit everything pending plus items on the calling thread.
		/// Waits for an in-flight background commit first. Throws if writing fails
		/// (data stays pending and is retried by the next save).
		/// </summary>
		internal static void CommitNow(IReadOnlyDictionary<string, PendingWrite> items)
		{
			lock (SaveFileStore.IoLock)
			{
				FinishActiveCommit();

				Dictionary<string, PendingWrite> batch;
				lock (QueueLock)
				{
					foreach (var kv in items)
						MergeLocked(kv.Key, kv.Value);
					batch = TakePendingLocked();
				}

				if (batch.Count == 0)
					return;

				CommitBatch(batch);
			}
		}

		private static void StartWorker()
		{
			if (RunOnMainThread)
				Save.RunOnMainThreadLater(ProcessQueueStep);
			else
				Task.Run(ProcessQueue);
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
						Dictionary<string, PendingWrite> batch;
						lock (QueueLock)
						{
							if (_pending.Count == 0)
							{
								StopWorkerLocked(null);
								return;
							}

							batch = TakePendingLocked();
						}

						try
						{
							CommitBatch(batch);
						}
						catch (Exception e)
						{
							lock (QueueLock)
							{
								StopWorkerLocked(e);
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
					StopWorkerLocked(e);
				}
			}
		}

		// Whole commit at once. On failure the data goes back to pending and the exception propagates.
		private static void CommitBatch(Dictionary<string, PendingWrite> batch)
		{
			var commit = new SaveBatchCommit(batch);
			try
			{
				commit.RunToEnd();
			}
			catch (Exception e)
			{
				commit.Fail(e);
				throw;
			}
		}

		// WebGL: a batch spread over frames, one data file per frame; the manifest swap
		// on the last step is the commit point, so a crash mid-way keeps the old save.
		private static void ProcessQueueStep()
		{
			lock (SaveFileStore.IoLock)
			{
				if (_active == null)
				{
					Dictionary<string, PendingWrite> batch;
					lock (QueueLock)
					{
						if (_pending.Count == 0)
						{
							StopWorkerLocked(null);
							return;
						}

						batch = TakePendingLocked();
					}

					_active = new SaveBatchCommit(batch);
				}

				try
				{
					if (_active.Step())
						_active = null;
				}
				catch (Exception e)
				{
					var failed = _active;
					_active = null;
					failed.Fail(e);
					lock (QueueLock)
					{
						StopWorkerLocked(e);
					}

					Debug.LogError($"[SaveManager] Save failed, will retry on next save.\n{e}");
					return;
				}
			}

			Save.RunOnMainThreadLater(ProcessQueueStep);
		}

		// A sync save must not overtake a commit spread over frames: finish it right now.
		private static void FinishActiveCommit()
		{
			if (_active == null)
				return;

			var active = _active;
			_active = null;
			try
			{
				active.RunToEnd();
			}
			catch (Exception e)
			{
				active.Fail(e);
				Debug.LogError($"[SaveManager] Save failed, will retry on next save.\n{e}");
			}
		}

		internal static void Requeue(Dictionary<string, PendingWrite> batch)
		{
			lock (QueueLock)
			{
				RequeueLocked(batch);
			}
		}

		private static void MergeLocked(string key, PendingWrite write)
		{
			if (_pending.TryGetValue(key, out var existing))
				existing.MergeNewer(write);
			else
				_pending[key] = write;
		}

		// True if the caller must start a worker.
		private static bool StartWorkerLocked()
		{
			if (_isRunning) return false;
			_isRunning = true;
			return true;
		}

		// Worker exits: flush awaiters complete (or fail with the error that stopped it).
		private static void StopWorkerLocked(Exception error)
		{
			_isRunning = false;

			var waiters = _flushWaiters;
			_flushWaiters = new List<TaskCompletionSource<bool>>();
			foreach (var waiter in waiters)
			{
				if (error == null) waiter.TrySetResult(true);
				else waiter.TrySetException(error);
			}
		}

		private static Dictionary<string, PendingWrite> TakePendingLocked()
		{
			var batch = _pending;
			_pending = new Dictionary<string, PendingWrite>();
			return batch;
		}

		// Failed data goes back (without its awaiters, they already got the error)
		// unless the game already queued something newer for that key.
		private static void RequeueLocked(Dictionary<string, PendingWrite> batch)
		{
			foreach (var kv in batch)
			{
				if (!_pending.ContainsKey(kv.Key))
					_pending[kv.Key] = new PendingWrite(kv.Value.Data);
			}
		}
	}
}
