using System.Collections.Generic;

namespace vanhaodev.savemanager.Samples.K_pop_Artists
{
	public class ArtistData : ISaveReadWrite
	{
		public string Name;
		public ArtistStats Stats = new();
		public List<ArtistImage> Images = new();
		public List<ArtistSNS> Sns = new();

		public void Write(SaveWriter w)
		{
			w.AddString(Name);
			Stats.Write(w);
			w.WriteList(Images);
			w.WriteList(Sns);
		}

		public void Read(SaveReader r)
		{
			Name = r.ReadString();
			Stats = new ArtistStats();
			Stats.Read(r);

			Images = r.ReadList<ArtistImage>();
			Sns = r.ReadList<ArtistSNS>();
		}
	}

	public class ArtistStats : ISaveReadWrite
	{
		public string Weight;
		public int Height;

		public void Write(SaveWriter w)
		{
			w.AddString(Weight);
			w.AddInt(Height);
		}

		public void Read(SaveReader r)
		{
			Weight = r.ReadString();
			Height = r.ReadInt();
		}
	}

	public class ArtistSNS : ISaveReadWrite
	{
		public enum SNSType
		{
			Instagram,
			YouTube
		}

		public SNSType Type;
		public string Url;

		public void Write(SaveWriter w)
		{
			w.AddInt((int)Type);
			w.AddString(Url);
		}

		public void Read(SaveReader r)
		{
			Type = (SNSType)r.ReadInt();
			Url = r.ReadString();
		}
	}

	public struct ArtistImage : ISaveReadWrite
	{
		public string Url;

		public void Write(SaveWriter w)
		{
			w.AddString(Url);
		}

		public void Read(SaveReader r)
		{
			Url = r.ReadString();
		}
	}

	public class ArtistListData : ISaveReadWrite
	{
		public List<ArtistData> Artists = new();

		public void Write(SaveWriter w)
		{
			w.WriteList(Artists);
		}

		public void Read(SaveReader r)
		{
			Artists = r.ReadList<ArtistData>();
		}
	}
}