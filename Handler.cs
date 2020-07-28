using System;
using System.IO;

using LibHac;
using LibHac.Common;
using LibHac.Fs;
using LibHac.FsSystem;
using LibHac.FsSystem.NcaUtils;

namespace yui
{
    class SysUpdateHandler: System.IDisposable
    {
        private string CDN_URL;
        private string SUN_URL;
        private Session session;
        private string CDN_TEMPLATE = "{0}/{1}/{2}/{3}/{4}?device_id={4}";
        public HandlerArgs args;
        public SysUpdateHandler(string[] args)
        {
            this.args = new HandlerArgs(args);
            this.session = new Session(ref this.args);

            if (this.args.tencent) {
                this.CDN_URL = "https://atumn.hac.lp1.d4c.n.nintendoswitch.cn";
                this.SUN_URL = "https://sun.hac.lp1.d4c.n.nintendoswitch.cn/v1";
            }
            else {
                this.CDN_URL = "https://atumn.hac.lp1.d4c.nintendo.net";
                this.SUN_URL = "https://sun.hac.lp1.d4c.nintendo.net/v1";
            
            }
        }
        public void Dispose() {}

        public dynamic GetLatestUpdateInfo()
        {
            string url = $"{this.SUN_URL}/system_update_meta?device_id={this.args.device_id}";
            return this.session.GetJson(url);
        }
        public static Tuple<string, int, long> PrettyPrintVersion(long v)
        {
            string str_ver = $"{(v >> 26) & 0x1f}.{(v >> 20) & 0x1f}.{(v >> 16) & 0xf}";
            int bn = (int)v & 0xffff;

            return Tuple.Create(str_ver, bn, v);
        }

        private void SafeHandleDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                if (!this.args.ignore_warnings)
                    Console.WriteLine($"[WARNING] '{path}' already exists. \nPlease confirm that it should be overwritten [type 'y' to accept, anything else to abort]:");
                if (this.args.ignore_warnings || Console.ReadKey().KeyChar == 'y')
                {
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

        public void GetLatestFull(string out_path)
        {
            var update_meta = GetLatestUpdateInfo()["system_update_metas"][0];
            var ver_tuple = PrettyPrintVersion(Convert.ToInt64(update_meta["title_version"]));

            if (String.IsNullOrEmpty(out_path))
                out_path = $"sysupdate-[{ver_tuple.Item3}]-{ver_tuple.Item1}-bn_{ver_tuple.Item2}";
            SafeHandleDirectory(out_path);

            // get the update meta title whose cnmt contains the contentids 
            // of all the other meta ncas
            string update_meta_path = this.session.GetStreamNcaToFile(
                out_path,
                String.Format(
                    this.CDN_TEMPLATE,
                    this.CDN_URL,
                    "t", // magic
                    "s", // *sparkles*
                    update_meta["title_id"],
                    update_meta["title_version"],
                    this.args.device_id
                ),
                true // is_meta
            );

            this.DownloadEachNcaInNcasCnmtRecursive(out_path, update_meta_path);

        }
        public void DownloadEachNcaInNcasCnmtRecursive(string out_folder, string nca_path)
        {
            using (IStorage i_fp = new LocalStorage(nca_path, FileAccess.Read))
            {
                var nca = new Nca(this.args.keyset, i_fp);
                using (IFileSystem fs = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid))
                {
                    foreach (var entry in fs.EnumerateEntries("/", "*.cnmt"))
                    {
                        fs.OpenFile(out IFile fp, new U8Span(entry.Name), OpenMode.Read);
                        Cnmt cnmt = new Cnmt(fp.AsStream());

                        foreach (var meta_entry in cnmt.MetaEntries)
                        {
                            string title_id = TidString(meta_entry.TitleId);
                            string version = meta_entry.Version.Version.ToString();
                        
                            string meta_nca_path = this.session.GetStreamNcaToFile(
                                out_folder,
                                String.Format(
                                    "{0}/c/{1}/{2}?device_id={3}",
                                    this.CDN_URL,
                                    (title_id == "0100000000000816") ? "c" : "a",
                                    this.GetContentId(title_id, version),
                                    this.args.device_id
                                ),
                                true
                            );
                            this.DownloadEachNcaInNcasCnmtRecursive(out_folder, meta_nca_path);
                        }

                        foreach (var content_entry in cnmt.ContentEntries)
                        {
                            string content_id = content_entry.NcaId.ToHexString().ToLower();
                            this.session.GetStreamNcaToFile(
                                out_folder,
                                String.Format(
                                    "{0}/c/c/{1}",
                                    this.CDN_URL,
                                    content_id
                                ),
                                false,
                                content_id
                            );
                        }
                    }
                }
            }
        }
        public string GetContentId(string title_id, string title_version)
        {
            return this.session.Head(
                String.Format(
                    this.CDN_TEMPLATE,
                    this.CDN_URL,
                    "t",
                    (title_id == "0100000000000816") ? "s" : "a",
                    title_id,
                    title_version,
                    this.args.device_id
                )
            )["X-Nintendo-Content-ID"];
        } 
        static string TidString(ulong tid)
        {
            return $"0{tid:X}";
        }

        public void PrintLatestSysVersion()
        {
            var update_meta = GetLatestUpdateInfo()["system_update_metas"][0];
            var ver_tuple = PrettyPrintVersion(Convert.ToInt64(update_meta["title_version"]));

            Console.WriteLine(
                $"Latest version on CDN: {ver_tuple.Item1} [{ver_tuple.Item3}] buildnum={ver_tuple.Item2}"
            );
        }
    }
}

