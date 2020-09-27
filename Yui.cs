using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibHac;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.FsSystem.NcaUtils;

namespace yui
{
	public class YuiConfig
	{
		public CdnClient Client = null!;
		public Keyset? Keyset = null;
		public int MaxParallelism = 5;

		// Url is passed only for debugging and can be null
		public delegate void ContentHandlerMethod(Stream data, string ncaID, string? url);
		public delegate void MetaHandlerMethod(byte[] data, string titleID, string ncaID, string version, string? url);

		// Handlers for processing NCAs, these should be thread safe if MaxParallelism > 1
		public MetaHandlerMethod? MetaHandler;
		public ContentHandlerMethod? ContentHandler;
	}

	public class Yui : IDisposable
	{
		private readonly CdnClient Client;
		private readonly YuiConfig Config;

		public Yui(YuiConfig config)
		{
			Config = config;
			Client = Config.Client;
		}

		public struct UpdateVersion
		{
			public ulong Value;
			public int BuildNumber => (int)(Value & 0xffff);
			public override string ToString() => $"{(Value >> 26) & 0x1f}.{(Value >> 20) & 0x1f}.{(Value >> 16) & 0xf}";
			public static implicit operator UpdateVersion(ulong v) => new UpdateVersion { Value = v };
			public static implicit operator UpdateVersion(string v) => new UpdateVersion { Value = ulong.Parse(v) };
		}

		public static UpdateVersion ParseVersion(ulong v) => v;
		public static UpdateVersion ParseVersion(string v) => v;

		public struct SysUpdateMeta
		{
			public byte[] Data;
			public string TitleID;
			public string NcaID;
			public UpdateVersion Version;
		}

		public CdnClient.UpdateInfo GetLatestUpdateInfo() =>
			Client.GetLatestUpdateInfo();

		// get the update meta title whose cnmt contains the ncaIDs 
		// of all the other meta ncas
		public SysUpdateMeta GetSysUpdateMetaNca()
		{
			var meta = GetLatestUpdateInfo().system_update_metas[0];
			var response = Client.GetUpdateMeta(meta.title_id, meta.title_version.ToString());

			return new SysUpdateMeta
			{
				Data = response.ContentAsBuffer(),
				TitleID = meta.title_id,
				NcaID = response.XNContentID(),
				Version = meta.title_version
			};
		}

		public struct CnmtInfo
		{
			public string ID => IsMeta ?
				TitleID ?? throw new ArgumentNullException(nameof(TitleID)) :
				NcaID ?? throw new ArgumentNullException(nameof(NcaID));

			public string MetaVersion => !IsMeta ?
				throw new Exception("This field is only for meta NCAs") :
				Version ?? throw new ArgumentNullException(Version);

			public string? TitleID, Version, NcaID;
			public bool IsMeta;
		}

		public CnmtInfo[] GetContentEntries(IStorage NcaStorage)
		{
			if (Config.Keyset is null)
				throw new ArgumentNullException(nameof(Config.Keyset));

			List<CnmtInfo> res = new List<CnmtInfo>();

			var nca = new Nca(Config.Keyset, NcaStorage);
			using IFileSystem fs = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
			foreach (var entry in fs.EnumerateEntries("/", "*.cnmt"))
			{
				fs.OpenFile(out IFile fp, new U8Span(entry.Name), OpenMode.Read);
				Cnmt cnmt = new Cnmt(fp.AsStream());

				foreach (var meta_entry in cnmt.MetaEntries)
					res.Add(new CnmtInfo
					{
						IsMeta = true,
						TitleID = $"0{meta_entry.TitleId:X}",
						Version = meta_entry.Version.Version.ToString()
					});

				foreach (var content_entry in cnmt.ContentEntries)
					res.Add(new CnmtInfo
					{
						IsMeta = false,
						NcaID = content_entry.NcaId.ToHexString().ToLower(),
					});
			}

			return res.ToArray();
		}

		// After downloading meta NCAs we need to parse them, so return the data as well instead of reading it back from disk
		private byte[] DownloadMeta(string titleID, string version)
		{
			var response = Client.GetMeta(titleID, version);
			byte[] data = response.ContentAsBuffer();
			Config.MetaHandler?.Invoke(data, titleID, response.XNContentID(), version, response.RequestMessage.RequestUri.ToString());
			return data;
		}

		// Downloads all meta NCAs and returns list of all content
		public CnmtInfo[] ProcessMeta(CnmtInfo[] info)
		{
			if (info.Any(x => !x.IsMeta))
				throw new Exception("Meta info should not contain any content NCA");

			ConcurrentBag<CnmtInfo> res = new ConcurrentBag<CnmtInfo>();

			Parallel.ForEach(info, new ParallelOptions { MaxDegreeOfParallelism = Config.MaxParallelism }, x => {
				var nca = DownloadMeta(x.ID, x.MetaVersion);

				var info = GetContentEntries(new MemoryStorage(nca));
				if (info.Any(x => x.IsMeta))
					throw new Exception($"Did not expect meta NCAs here: {x.ID} {x.MetaVersion}");

				foreach (var i in info)
					res.Add(i);
			});

			return res.ToArray();
		}

		private void DownloadContent(string ncaID)
		{
			var response = Client.GetContent(ncaID);
			// This is checked in ProcessContent
			Config.ContentHandler!(response.ContentAsStream(), ncaID, response.RequestMessage.RequestUri.ToString());
		}

		public void ProcessContent(CnmtInfo[] info)
		{
			if (info.Any(x => x.IsMeta))
				throw new Exception("Content info should not contain any meta NCA");

			// Don't download content if nothing is going to handle it
			if (Config.ContentHandler is null)
				return;

			Parallel.ForEach(info, new ParallelOptions { MaxDegreeOfParallelism = Config.MaxParallelism }, x => {
				DownloadContent(x.ID);
			});
		}

		public void Dispose()
		{
			Client.Dispose();
		}
	}
}
