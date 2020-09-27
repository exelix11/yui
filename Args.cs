using System;
using System.IO;

using LibHac;

namespace yui
{
    class HandlerArgs
    {
        // Modes of operation
        public bool get_info = false;
        public bool get_latest = false;
        public bool get_help = false;

        // Context relevant information
        public bool tencent = false;
        public bool ignore_warnings = false;
        public string cert_loc = "nx_tls_client_cert.pem";
        public string keyset_loc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".switch/prod.keys");
        public Keyset? keyset = null;
        public string? out_path = null;
        public string device_id = "DEADCAFEBABEBEEF";
        public string env = "lp1";
        public string server = "d4c";
        public string platform = "NX";
        public string firmware_version = "5.1.0-3";
        public int max_jobs = 5;
        public string[]? title_filter;
        public bool verbose = false;

        public HandlerArgs(string[] raw_args)
        {
            ParseArgs(raw_args);
            if (this.get_help || !(this.get_latest || this.get_info))
            {
                PrintHelp();
                Environment.Exit(-1);
            }
            InitStuff();
        }

        private void ParseArgs(string[] raw_args)
        {
            for (int i = 0; i < raw_args.Length; ++i)
            {
                switch (raw_args[i])
                {
                case "--cert":
                case "-c":
                    this.cert_loc = raw_args[++i];
                    break;
                case "--keyset":
                case "-k":
                    this.keyset_loc = raw_args[++i];
                    break;
                case "--out":
                case "--out-path":
                case "-o":
                    this.out_path = raw_args[++i];
                    break;
                case "--device-id":
                case "-did":
                    this.device_id = raw_args[++i];
                    break;
                case "--environment":
                case "-env":
                    this.env = raw_args[++i];
                    break;
                case "--server":
                case "-s":
                    this.server = raw_args[++i];
                    break;
                case "--platform":
                case "-p":
                    this.platform = raw_args[++i];
                    break;
                case "--firmware-version":
                case "--fwver":
                case "-fwver":
                    this.firmware_version = raw_args[++i];
                    break;
                case "--tencent":
                case "-t":
                    this.tencent = true;
                    break;
                case "--get-info":
                case "--info":
                case "-i":
                    this.get_info = true;
                    break;
                case "--latest":
                case "--get-latest":
                case "-l":
                    this.get_latest = true;
                    break;
                case "--ignore-warnings":
                case "--no-confirm":
                case "-q":
                    this.ignore_warnings = true;
                    break;
                case "--help":
                case "-h":
                    this.get_help = true;
                    break;
                case "--jobs":
                case "-j":
                    this.max_jobs = Math.Min(int.Parse(raw_args[++i]), 1);
                    break;
                case "--titles":
                    this.title_filter = raw_args[++i].Split(',');
                    break;
                case "-v":
                    this.verbose = true;
                    break;
                }
            }
        }

        private void PrintHelp()
        {
            Console.WriteLine(
                  "yui - a c# nintendo switch system update downloader\n"
                + "Usage: yui [--info] [--latest] [--cert path/to/cert] [...args]\n"
                + "\n"
                + "The relevant arguments are:\n"
                + "--info|--get-info|-i                             Prints info about the latest version on cdn\n"
                + "--latest|--get-latest|-l                         Downloads the latest version from cdn\n"
                + "--help|-h                                        Prints this text and exits\n"
                + "--ignore-warnings|-q                             Ignores warnings and assumes 'y' for prompts\n"
                + "--tencent|-t                                     Use the tencent servers for all requests\n"
                + "--cert|-c    path/to/cert.pem                    Path to a switch ssl certificate.\n"
                + "                                                 Defaults to 'nx_tls_client_cert.pem'\n"
                + "--keyset|-k  path/to/prod.keys                   Path to a switch keyset.\n"
                + "                                                 Defaults to '~/.switch/prod.keys'\n"
                + "--out|--out-path|-o  path                        Outpath for the --latest mode.\n"
                + "                                                 Defaults to 'sysupdate-[intver]-[semver]_bn-[buildnum]'\n"
                + "--jobs|-j    max jobs                            Max concurrent downloads, default is 5\n"
                + "--titles     010000001000,0100000010001...       Only download specified titles, takes a comma separated list of title IDs\n"
                + "-v                                               Verbose mode\n"
            );
        }

        private void InitStuff()
        {
            this.cert_loc = Path.GetFullPath(this.cert_loc);
            this.keyset_loc = Path.GetFullPath(this.keyset_loc);
            if (!String.IsNullOrEmpty(this.out_path))
                this.out_path = Path.GetFullPath(this.out_path);
            
            // keysets are only needed for a full download
            if (this.get_latest)
                this.keyset = ExternalKeyReader.ReadKeyFile(keyset_loc);
        }
    }
}
