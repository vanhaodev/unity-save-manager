using System;
using System.Collections.Generic;
using System.Text;

namespace vanhaodev.savemanager
{
	public class SaveReader
	{
		private byte[] _data;
		private int _offset;

		public SaveReader(byte[] data)
		{
			_data = data;
			_offset = 0;
		}

		public byte ReadByte()
		{
			return _data[_offset++];
		}

		public byte[] ReadBytes(int length)
		{
			var result = new byte[length];
			Buffer.BlockCopy(_data, _offset, result, 0, length);
			_offset += length;
			return result;
		}

		public int ReadInt()
		{
			int value = BitConverter.ToInt32(_data, _offset);
			_offset += 4;
			return value;
		}

		public long ReadLong()
		{
			long value = BitConverter.ToInt64(_data, _offset);
			_offset += 8;
			return value;
		}

		public float ReadFloat()
		{
			float value = BitConverter.ToSingle(_data, _offset);
			_offset += 4;
			return value;
		}

		public bool ReadBool()
		{
			return _data[_offset++] == 1;
		}

		public List<T> ReadList<T>() where T : ISaveable, new()
		{
			int count = ReadInt();

			if (count == -1)
				return null;

			var list = new List<T>(count);

			for (int i = 0; i < count; i++)
			{
				var item = new T();
				item.ReadSave(this);
				list.Add(item);
			}

			return list;
		}

		public string ReadString()
		{
			int length = ReadInt();
			if (length == -1) return null;

			string value = Encoding.UTF8.GetString(_data, _offset, length);
			_offset += length;
			return value;
		}
	}
}