using System.Collections.Generic;

namespace vanhaodev.savemanager.Samples.K_pop_Artists
{
	public class KpopArtistsManager
	{
		private const string KEY = "kpop_artists";

		public ArtistListData Data { get; private set; }

		public void LoadFromDisk()
		{
			Data = Save.Get<ArtistListData>(KEY) ?? new ArtistListData();
		}

		public void SaveToDisk()
		{
			Save.Set(KEY, Data);
		}

		public void Add(ArtistData artist)
		{
			if (artist == null) return;
			if (string.IsNullOrEmpty(artist.Name)) return;

			Data.Artists.Add(artist);
		}

		public void Update(int index, ArtistData artist)
		{
			if (artist == null) return;
			if (index < 0 || index >= Data.Artists.Count) return;

			Data.Artists[index] = artist;
		}

		public void Delete(int index)
		{
			if (index < 0 || index >= Data.Artists.Count) return;

			Data.Artists.RemoveAt(index);
		}

		public void Clear()
		{
			Data.Artists.Clear();
		}

		public static ArtistData CloneModel(ArtistData src)
		{
			return new ArtistData
			{
				Name = src.Name,
				Stats = new ArtistStats
				{
					Weight = src.Stats.Weight,
					Height = src.Stats.Height
				},
				Images = new List<ArtistImage>(src.Images),
				Sns = new List<ArtistSNS>(src.Sns)
			};
		}

		public static ArtistData CloneArtist(ArtistData src)
		{
			return new ArtistData
			{
				Name = src.Name,

				Stats = new ArtistStats
				{
					Weight = src.Stats.Weight,
					Height = src.Stats.Height
				},

				Images = new System.Collections.Generic.List<ArtistImage>(src.Images),
				Sns = new System.Collections.Generic.List<ArtistSNS>(src.Sns)
			};
		}
	}
}