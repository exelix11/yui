using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibHac.Fs;

namespace yui
{
	class ProgressReporter : IDisposable
	{
		readonly int Max;
		readonly (int, int) Cursor;
		readonly Timer UpdateTimer;

		int Cur = 0;
		bool Complete = false;
		int MaxWrittenLen = 0;

		public ProgressReporter(int max)
		{
			if (Console.IsOutputRedirected)
				throw new Exception("Can't reposition the console cursor when output is redirected");

			Max = max;
			Cursor = (Console.CursorLeft, Console.CursorTop);
			UpdateTimer = new Timer(TimerCallback, null, 1000, 1000);
		}

		private void TimerCallback(object? _) 
		{
			if (!Complete)
				UpdateVal($"{Cur}/{Max}");
		}

		private void UpdateVal(string s)
		{
			var pos = (Console.CursorLeft, Console.CursorTop);
			(Console.CursorLeft, Console.CursorTop) = Cursor;
			Console.Write(s);
			(Console.CursorLeft, Console.CursorTop) = pos;
			MaxWrittenLen = Math.Max(s.Length, MaxWrittenLen);
		}

		public void Increment()
		{
			if (Complete)
				throw new Exception("Can't increment a completed process");

			Interlocked.Increment(ref Cur);
		}

		// Should be called once all threaded operations are finished
		public void MarkComplete()
		{
			UpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);
			Complete = true;
			UpdateVal("Done." + new string(' ', Math.Min(MaxWrittenLen - 5, 1)));
		}

		public void Dispose()
		{
			UpdateTimer.Dispose();
		}
	}

	class SysUpdateHandler : IDisposable
	{
		public readonly HandlerArgs Args;
		readonly Yui yui;
		string OutPath;

		public SysUpdateHandler(HandlerArgs args)
		{
			Args = args;

			if (Args.ConsoleVerbose)
				Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));

			if (Args.FileVerbose != null)
				Trace.Listeners.Add(new TextWriterTraceListener(File.OpenWrite(Args.FileVerbose)));

			yui = new Yui(new YuiConfig(
				ContentHandler: StoreContent,
				MetaHandler: StoreMeta,
				MaxParallelism: Args.MaxJobs,
				Keyset: Args.Keyset,
				Client: new CdnClientConfig {
					DeviceID = Args.DeviceID,
					Env = Args.Env,
					FirmwareVersion = Args.FirmwareVersion,
					Platform = Args.Platform,
					Tencent = Args.Tencent
				}.WithCertFromFile(Args.CertPath).MakeClient()
			));

			OutPath = Args.OutPath ?? "";
		}

		ProgressReporter? CurrentReporter;

		// Url is only for debugging purposes
		void StoreContent(Stream data, string ncaID, string? url)
		{
			string path = FileName(OutPath, ncaID, false);

			Trace.WriteLine($"[GotContent] [{data.Length}] {url ?? ""} => {path}");
			using FileStream fs = File.OpenWrite(path);
			data.CopyTo(fs, 16 * 1024 * 1024);
			data.Dispose();

			CurrentReporter?.Increment();
		}

		void StoreMeta(byte[] data, string titleID, string ncaID, string version, string? url)
		{
			string path = FileName(OutPath, ncaID, true);

			Trace.WriteLine($"[GotMeta] {titleID} v{version} [{data.Length}] {url ?? ""} => {path}");
			File.WriteAllBytes(path, data);

			CurrentReporter?.Increment();
		}

		static string FileName(string root, string ncaID, bool isMeta) =>
			Path.Combine(root, $"{ncaID}{(isMeta ? ".cnmt" : "")}.nca");

		private void SafeHandleDirectory(string path)
		{
			if (Directory.Exists(path))
			{
				if (!Args.IgnoreWarnings)
					Console.Write($"[WARNING] '{path}' already exists. \nPlease confirm that it should be overwritten [type 'y' to accept, anything else to abort]: ");
				if (Args.IgnoreWarnings || Console.ReadKey().KeyChar == 'y')
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

		private void BeginProgressReport(string message, int max)
		{
			Console.Write(message, max);
			// With verbose args progress reporting is useless
			if (!Args.ConsoleVerbose && !Console.IsOutputRedirected)
				CurrentReporter = new ProgressReporter(max);
			Console.WriteLine();
		}

		private void CompleteProgressReport() 
		{
			CurrentReporter?.MarkComplete();
			StopProgressReport();
		}

		private void StopProgressReport() 
		{
			CurrentReporter?.Dispose();
			CurrentReporter = null;
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

			if (Args.TitleFilter != null)
				metaEntries = metaEntries.Where(x => Args.TitleFilter.Contains(x.ID)).ToArray();

			BeginProgressReport("Downloading {0} meta title(s)... ", metaEntries.Length);
			var contentEntries = yui.ProcessMeta(metaEntries);
			CompleteProgressReport();

			if (Args.OnlyMeta)
			{
				Console.WriteLine("Downloading content has been skipped as requested.");
			}
			else
			{
				BeginProgressReport("Downloading {0} content(s)... ", contentEntries.Length);
				yui.ProcessContent(contentEntries);
				CompleteProgressReport();
			}

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
			StopProgressReport();
			yui.Dispose();
		}
	}
}