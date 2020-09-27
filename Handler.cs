using System;
using System.Diagnostics;
using System.IO;
using LibHac.Fs;

namespace yui
{
	class SysUpdateHandler : IDisposable
	{
		public readonly HandlerArgs Args;
		readonly Yui yui;		
		
		bool IgnoreWarnings => Args.ignore_warnings;

		string OutPath;

		public SysUpdateHandler(string[] args)
		{
			Args = new HandlerArgs(args);

			yui = new Yui(new YuiConfig {
				ContentHandler = StoreContent,
				MetaHandler = StoreMeta,
				MaxParallelism = 5,
				Keyset = Args.keyset,
				Client = new CdnClientConfig {
					DeviceID = Args.device_id,
					Env = Args.env,
					FirmwareVersion = Args.firmware_version,
					Platform = Args.platform,
					Tencent = Args.tencent
				}.WithCertFromFile(Args.cert_loc).MakeClient()
			});

			OutPath = Args.out_path ?? "";
		}

		// Url is only for debugging purposes
		void StoreContent(Stream data, string ncaID, string? url)
		{
			string path = FileName(OutPath, ncaID, false);

			Trace.WriteLine($"[GotContent] [{data.Length}] {url ?? ""} => {path}");
			using FileStream fs = File.OpenWrite(path);
			data.CopyTo(fs);
		}

		void StoreMeta(byte[] data, string titleID, string ncaID, string version, string? url)
		{
			string path = FileName(OutPath, ncaID, true);

			Trace.WriteLine($"[GotMeta] {titleID} v{version} [{data.Length}] {url ?? ""} => {path}");
			File.WriteAllBytes(path, data);
		}

		static string FileName(string root, string ncaID, bool isMeta) =>
			Path.Combine(root, $"{ncaID}{(isMeta ? ".cnmt" : "")}.nca");

		private void SafeHandleDirectory(string path)
		{
			if (Directory.Exists(path))
			{
				if (!IgnoreWarnings)
					Console.Write($"[WARNING] '{path}' already exists. \nPlease confirm that it should be overwritten [type 'y' to accept, anything else to abort]: ");
				if (IgnoreWarnings || Console.ReadKey().KeyChar == 'y')
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

		public void GetLatest()
		{
			Console.WriteLine("Getting sysupdate meta...");
			var update = yui.GetSysUpdateMetaNca();

			if (String.IsNullOrEmpty(OutPath))
				OutPath = $"sysupdate-[{update.Version.Value}]-{update.Version}-bn_{update.Version.BuildNumber}";
			SafeHandleDirectory(OutPath);

			// store it to disk as we're downloading the full update
			StoreMeta(update.Data, update.TitleID, update.NcaID, update.Version.Value.ToString(), null);

			Console.WriteLine("Parsing update entries...");
			var metaEntries = yui.GetContentEntries(new MemoryStorage(update.Data));

			Console.WriteLine($"Downloading {metaEntries.Length} meta titles...");
			var contentEntries = yui.ProcessMeta(metaEntries);

			Console.WriteLine($"Downloading {contentEntries.Length} contents...");
			yui.ProcessContent(contentEntries);

			Console.WriteLine($"All done !");
		}

		public void PrintLatestSysVersion()
		{
			var update_meta = yui.GetLatestUpdateInfo();
			var ver = Yui.ParseVersion(update_meta.system_update_metas[0].title_version);

			Console.WriteLine(
				$"Latest version on CDN: {ver} [{ver.Value}] buildnum={ver.BuildNumber}"
			);
		}

		public void Dispose()
		{
			yui.Dispose();
		}
	}
}