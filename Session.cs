using System;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;

using Newtonsoft.Json;

namespace yui
{
    class Session
    {
        private HandlerArgs args;
        private X509Certificate2 cert;
        private string user_agent;
        public Session(ref HandlerArgs args)
        {
            this.args = args;
            this.user_agent = $"NintendoSDK Firmware/{args.firmware_version} (platform:{args.platform}; did:{args.device_id}; eid:{args.env})";
            this.cert = LoadCert(args.cert_loc);
        }

        public X509Certificate2 LoadCert(string cert_path) =>
            CertLoader.FromFile(cert_path);

        public HttpWebResponse Request(string method, string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.AutomaticDecompression = DecompressionMethods.GZip;
            req.ClientCertificates.Add(this.cert);
            req.ServerCertificateValidationCallback = 
                (httpRequestMessage, cert, cetChain, policyErrors) =>
                {
                    return true;
                };
            req.Method = method;
            req.Headers.Clear();
            req.Headers.Add(
                "user-agent", this.user_agent
            );

            return (HttpWebResponse)req.GetResponse();
        }

        public string GetString(string url)
        {
            string r = null;
            var resp = Request("GET", url);
            Console.WriteLine($"StatusCode: {resp.StatusCode} [{(int)resp.StatusCode}]");

            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                r = reader.ReadToEnd();
            }
            return r;
        }

        public dynamic GetJson(string url)
        {
            string text = GetString(url);
            dynamic json = JsonConvert.DeserializeObject(text);
            return json;
        }
    
        public WebHeaderCollection Head(string url)
        {
            return Request("GET", url).Headers;
        }

        public string GetStreamNcaToFile(string dest, string url, bool is_meta, string content_id=null, int buffer_size=4096)
        {
            var resp = Request("GET", url);
            if (String.IsNullOrEmpty(content_id))
                content_id = resp.GetResponseHeader("X-Nintendo-Content-ID");
            dest += $"/{content_id}{(is_meta ? ".cnmt" : "")}.nca";

            using (var fp = File.Create(dest))
            using (var stream = resp.GetResponseStream())
            {
                Console.WriteLine($"[GetStreamToFile] [{resp.GetResponseHeader("Content-Length")}] {url} ==> {dest}");
                stream.CopyTo(fp);
            }
            return dest;
        }
    }
}
