using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;

namespace yui
{
	public class CdnClientConfig
	{
		public string UserAgent => $"NintendoSDK Firmware/{FirmwareVersion} (platform:{Platform}; did:{DeviceID}; eid:{Env})";

		public string FirmwareVersion { get; set; } = "5.1.0-3";
		public string Platform { get; set; } = "NX";
		public string DeviceID { get; set; } = "DEADCAFEBABEBEEF";
		public string Env { get; set; } = "lp1";
		public bool Tencent { get; set; } = false;
		public X509Certificate2? Certificate { get; set; }

		public CdnClientConfig WithCertFromFile(string path)
		{
			Certificate = CertLoader.FromFile(path);
			return this;
		}

		public CdnClient MakeClient()
		{
			return new CdnClient(this);
		}
	}

	public class CdnClient : IDisposable
	{
		public struct UpdateInfo
		{
			public struct TitleMeta
			{
				public string title_id;
				public ulong title_version;
			};

			public ulong timestamp;
			public TitleMeta[] system_update_metas;
		}

		private readonly string CdnUrl;
		private readonly string SunUrl;
		private readonly CdnClientConfig Config;
		private readonly HttpClient Client;

		public CdnClient(CdnClientConfig Config)
		{
			this.Config = Config;

			if (Config.Certificate == null)
				throw new ArgumentNullException(nameof(Config.Certificate));

			Client = new HttpClient(new HttpClientHandler()
			{
				AutomaticDecompression = DecompressionMethods.GZip,
				ClientCertificates = { Config.Certificate },
				ServerCertificateCustomValidationCallback = (m, c, cc, p) => true
			});
			Client.DefaultRequestHeaders.Add("User-Agent", Config.UserAgent);

			CdnUrl = Config.Tencent ? "https://atumn.hac.lp1.d4c.n.nintendoswitch.cn" : "https://atumn.hac.lp1.d4c.nintendo.net";
			SunUrl = Config.Tencent ? "https://sun.hac.lp1.d4c.n.nintendoswitch.cn/v1" : "https://sun.hac.lp1.d4c.nintendo.net/v1";
		}

		public HttpResponseMessage Get(string url) =>
			Client.GetAsync(url).Result;

		public string GetString(string url) =>
			Get(url).ContentAsString();

		public T GetJson<T>(string url)
		{
			T json = JsonConvert.DeserializeObject<T>(GetString(url));
			return json ?? throw new Exception("Deserialized object is null");
		}

		public dynamic GetJson(string url) => GetJson<dynamic>(url);

		public UpdateInfo GetLatestUpdateInfo() =>
			GetJson<UpdateInfo>($"{this.SunUrl}/system_update_meta?device_id={Config.DeviceID}");

		public HttpResponseMessage GetUpdateMeta(string titleID, string titleVersion) =>
			Get(String.Format("{0}/{1}/{2}/{3}/{4}?device_id={5}", CdnUrl, "t", (titleID == "0100000000000816") ? "s" : "a", titleID, titleVersion, Config.DeviceID));

		public HttpResponseMessage GetContent(string contentID) =>
			Get(String.Format("{0}/c/c/{1}", CdnUrl, contentID));

		public HttpResponseMessage GetMeta(string titleID, string version)
		{
			var contentID = GetUpdateMeta(titleID, version).XNContentID();

			return Get(String.Format("{0}/c/{1}/{2}?device_id={3}", CdnUrl,
				(titleID == "0100000000000816") ? "c" : "a",
				contentID, Config.DeviceID
			));
		}

		public void Dispose()
		{
			// Don't need to properly implement the dispose pattern as the destructor will automatically dispose Client regardless
			Client.Dispose();
		}
	}

	public static class HttpExten
	{
		public static string XNContentID(this HttpResponseMessage m) =>
			m.Headers.GetValues("X-Nintendo-Content-ID").First();

		public static byte[] ContentAsBuffer(this HttpResponseMessage m) =>
			m.Content.ReadAsByteArrayAsync().Result;

		public static string ContentAsString(this HttpResponseMessage m) =>
			m.Content.ReadAsStringAsync().Result;

		public static Stream ContentAsStream(this HttpResponseMessage m) =>
			m.Content.ReadAsStreamAsync().Result;
	}
}