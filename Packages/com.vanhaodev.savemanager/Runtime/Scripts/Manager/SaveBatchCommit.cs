using System;
using System.Collections.Generic;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// One batch of pending writes going to disk as one commit: hooks, awaiters, and
	/// step-by-step writing (one data file per Step, so WebGL can spread a save over frames).
	/// Caller must hold SaveFileStore.IoLock during each call.
	/// </summary>
	internal sealed class SaveBatchCommit
	{
		private readonly Dictionary<string, PendingWrite> _batch;
		private readonly Dictionary<string, byte[]> _data;
		private readonly List<string> _keys;
		private SaveManifestStore.CommitJob _job;

		internal SaveBatchCommit(Dictionary<string, PendingWrite> batch)
		{
			_batch = batch;
			_keys = new List<string>(batch.Keys);
			_data = new Dictionary<string, byte[]>(batch.Count);
			foreach (var kv in batch)
				_data[kv.Key] = kv.Value.Data;

			Save.RaiseSaveStarted(_keys);
		}

		/// <summary>Write one data file, or swap the manifest when all are written. True when done.</summary>
		internal bool Step()
		{
			_job ??= SaveManifestStore.BeginCommit(_data);

			if (SaveManifestStore.WriteNext(_job))
				return false;

			SaveManifestStore.FinishCommit(_job);
			Complete();
			return true;
		}

		internal void RunToEnd()
		{
			while (!Step()) { }
		}

		/// <summary>Undo new files, keep the data pending for the next save, notify awaiters + hooks.</summary>
		internal void Fail(Exception error)
		{
			if (_job != null)
				SaveManifestStore.AbortCommit(_job);

			SaveQueue.Requeue(_batch);

			foreach (var write in _batch.Values)
				foreach (var waiter in write.Waiters)
					waiter.TrySetException(error);

			Save.RaiseSaveFailed(_keys, error);
		}

		private void Complete()
		{
			foreach (var write in _batch.Values)
				foreach (var waiter in write.Waiters)
					waiter.TrySetResult(true);

			Save.RaiseSaved(_keys);
		}
	}
}
