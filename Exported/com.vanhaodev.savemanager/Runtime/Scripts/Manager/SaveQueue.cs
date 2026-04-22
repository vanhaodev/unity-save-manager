using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace vanhaodev.savemanager
{
	public static class SaveQueue
	{
		private static ConcurrentDictionary<string, byte[]> _latest = new();
		private static bool _isRunning = false;

		public static void Enqueue(string path, byte[] data)
		{
			// overwrite key (debounce)
			_latest[path] = data;

			if (!_isRunning)
			{
				_isRunning = true;
				ProcessQueue();
			}
		}

		private static async void ProcessQueue()
		{
			while (_latest.Count > 0)
			{
				foreach (var kv in _latest)
				{
					if (_latest.TryRemove(kv.Key, out var data))
					{
						await WriteFileAsync(kv.Key, data);
					}
				}
			}

			_isRunning = false;
		}

		private static Task WriteFileAsync(string path, byte[] data)
		{
			return Task.Run(() =>
			{
				var tempPath = path + ".tmp";

				// atomic write
				File.WriteAllBytes(tempPath, data);

				if (File.Exists(path))
					File.Delete(path);

				File.Move(tempPath, path);
			});
		}
	}
}