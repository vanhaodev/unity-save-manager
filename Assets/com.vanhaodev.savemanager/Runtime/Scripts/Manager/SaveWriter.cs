using System;
using System.Collections.Generic;
using System.Text;

namespace vanhaodev.savemanager
{
	public class SaveWriter
	{
		private List<byte> _buffer = new List<byte>(256);

		public void AddByte(byte value)
		{
			_buffer.Add(value);
		}

		public void AddBytes(byte[] data)
		{
			_buffer.AddRange(data);
		}

		public void AddInt(int value)
		{
			_buffer.AddRange(BitConverter.GetBytes(value));
		}

		public void AddFloat(float value)
		{
			_buffer.AddRange(BitConverter.GetBytes(value));
		}

		public void AddBool(bool value)
		{
			_buffer.Add((byte)(value ? 1 : 0));
		}

		public void AddString(string value)
		{
			if (value == null)
			{
				AddInt(-1);
				return;
			}

			var bytes = Encoding.UTF8.GetBytes(value);
			AddInt(bytes.Length);
			_buffer.AddRange(bytes);
		}

		public byte[] ToArray()
		{
			return _buffer.ToArray();
		}
	}
}