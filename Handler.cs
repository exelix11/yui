using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using LibHac;
using LibHac.Common;
using LibHac.Fs;
using LibHac.FsSystem;
using LibHac.FsSystem.NcaUtils;

namespace yui
{
	class SysUpdateHandler : IDisposable
	{
		private readonly CdnClient Client;
		public readonly HandlerArgs ParsedArgs;

		public SysUpdateHandler(string[] args)
		{
			Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));

			ParsedArgs = new HandlerArgs(args);

			this.Client = new CdnClientConfig()
			{
				DeviceID = ParsedArgs.device_id,
				Env = ParsedArgs.env,
				FirmwareVersion = ParsedArgs.firmware_version,
				Platform = ParsedArgs.platform,
				Tencent = ParsedArgs.tencent
			}.WithCertFromFile(ParsedArgs.cert_loc).MakeClient();
		}

		struct Version
		{
			public ulong Value;
			public int BuildNumber => (int)(Value & 0xffff);
			public override string ToString() => $"{(Value >> 26) & 0x1f}.{(Value >> 20) & 0x1f}.{(Value >> 16) & 0xf}";
			public static implicit operator Version(ulong v) => new Version { Value = v };
			public static implicit operator Version(string? v) => new Version { Value = ulong.Parse(v) };
		}

		private void SafeHandleDirectory(string path)
		{
			if (Directory.Exists(path))
			{
				if (!ParsedArgs.ignore_warnings)
					Console.Write($"[WARNING] '{path}' already exists. \nPlease confirm that it should be overwritten [type 'y' to accept, anything else to abort]: ");
				if (ParsedArgs.ignore_warnings || Console.ReadKey().KeyChar == 'y')
				{
					Console.WriteLine();
					Directory.Delete(path, true);
				}
				else
				{
					Console.WriteLine("Aborting...");
					Environment.Exit(-2);
				}
			}
			Directory.CreateDirectory(path);
		}

		// Url is only for debugging purposes
		private void StreamToFile(Stream s, string path, string? url = null)
		{
			Trace.WriteLine($"[StreamToDisk] [{s.Length}] {url ?? ""} => {path}");
			using FileStream fs = File.OpenWrite(path);
			s.CopyTo(fs);
		}

		private void StoreToFile(byte[] s, string path, string? url = null)
		{
			Trace.WriteLine($"[StoreToFile] [{s.Length}] {url ?? ""} => {path}");
			File.WriteAllBytes(path, s);
		}

		private string FileName(string Root, string content_id, bool is_meta) =>
			Path.Combine(Root, $"{content_id}{(is_meta ? ".cnmt" : "")}.nca");

		// get the update meta title whose cnmt contains the contentids 
		// of all the other meta ncas
		private (byte[], string, string, ulong) GetSysUpdateMetaNca()
		{
			var meta = Client.GetLatestUpdateInfo().system_update_metas[0];

			var response = Client.GetUpdateMeta(meta.title_id, meta.title_version.ToString());
			var nca = response.ContentAsBuffer();

			return (nca, meta.title_id, response.XNContentID(), meta.title_version);
		}

		public void GetLatest(string outPath)
		{
			Console.WriteLine("Getting sysupdate meta...");
			var (nca, titleId, contentId, nca_ver) = GetSysUpdateMetaNca();
			Version ver = nca_ver;

			if (String.IsNullOrEmpty(outPath))
				outPath = $"sysupdate-[{nca_ver}]-{ver}-bn_{ver.BuildNumber}";
			SafeHandleDirectory(outPath);

			// store it to disk as we're downloading the full update
			File.WriteAllBytes(FileName(outPath, contentId, true), nca);

			Console.WriteLine("Parsing sysupdte entries...");
			var metaEntries = GetContentEntries(new MemoryStorage(nca));

			Console.WriteLine($"Downloading {metaEntries.Length} meta titles...");
			var contentEntries = ProcessAllMeta(metaEntries, outPath);

			Console.WriteLine($"Downloading {contentEntries.Length} contents...");
			ProcessAllContent(contentEntries, outPath);

			Console.WriteLine($"All done !");
		}

		// After downloading meta NCAs we need to parse them, so return the data as well instead of reading it back from the disk
		private byte[] DownloadMeta(string titleID, string version, string outPath)
		{
			var response = Client.GetMeta(titleID, version);
			byte[] data = response.ContentAsBuffer();
			StoreToFile(data, FileName(outPath, response.XNContentID(), true), response.RequestMessage.RequestUri.ToString());
			return data;
		}

		private void DownloadContent(string contentID, string outPath)
		{
			var response = Client.GetContent(contentID);
			StreamToFile(
				response.ContentAsStream(),
				FileName(outPath, contentID, false),
				response.RequestMessage.RequestUri.ToString()
			);
		}

		struct ContentInfo
		{
			public string? TitleID, Version, ContentID;
			public bool IsMeta;
		}

		private ContentInfo[] GetContentEntries(IStorage NcaStorage)
		{
			List<ContentInfo> res = new List<ContentInfo>();

			var nca = new Nca(ParsedArgs.keyset, NcaStorage);
			using IFileSystem fs = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
			foreach (var entry in fs.EnumerateEntries("/", "*.cnmt"))
			{
				fs.OpenFile(out IFile fp, new U8Span(entry.Name), OpenMode.Read);
				Cnmt cnmt = new Cnmt(fp.AsStream());

				foreach (var meta_entry in cnmt.MetaEntries)
					res.Add(new ContentInfo
					{
						IsMeta = true,
						TitleID = $"0{meta_entry.TitleId:X}",
						Version = meta_entry.Version.Version.ToString()
					});

				foreach (var content_entry in cnmt.ContentEntries)
					res.Add(new ContentInfo
					{
						IsMeta = false,
						ContentID = content_entry.NcaId.ToHexString().ToLower()
					});
			}

			return res.ToArray();
		}

		// Downloads all meta NCAs and returns list of all content
		private ContentInfo[] ProcessAllMeta(ContentInfo[] info, string outPath)
		{
			if (info.Any(x => !x.IsMeta))
				throw new Exception("There should be no content NCAs here");

			ConcurrentBag<ContentInfo> res = new ConcurrentBag<ContentInfo>();

			Parallel.ForEach(info, new ParallelOptions { MaxDegreeOfParallelism = 5 }, x => {
				var nca = DownloadMeta(
					x.TitleID ?? throw new Exception("TitleID is null"),
					x.Version ?? throw new Exception("Version is null"),
					outPath
				);

				var info = GetContentEntries(new MemoryStorage(nca));
				if (info.Any(x => x.IsMeta))
					throw new Exception("There should be no meta NCAs here");

				foreach (var i in info)
					res.Add(i);
			});

			return res.ToArray();
		}

		private void ProcessAllContent(ContentInfo[] info, string outPath)
		{
			if (info.Any(x => x.IsMeta))
				throw new Exception("There should be no meta NCAs here");

			Parallel.ForEach(info, new ParallelOptions { MaxDegreeOfParallelism = 5 }, x => {
				DownloadContent(x.ContentID ?? throw new Exception("ContentID is null"), outPath);
			});
		}

		public void PrintLatestSysVersion()
		{
			var update_meta = Client.GetLatestUpdateInfo();
			Version ver = update_meta.system_update_metas[0].title_version;

			Console.WriteLine(
				$"Latest version on CDN: {ver} [{ver.Value}] buildnum={ver.BuildNumber}"
			);
		}

		public void Dispose()
		{
			Client.Dispose();
		}
	}
}