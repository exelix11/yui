using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibHac;

namespace yui
{
    class HandlerArgs
    {
        // Modes of operation
        public enum Mode { GetInfo, Help, GetLatest };
        public Mode? OperationMode      { get; private set; } = null;

        // Context relevant information
        public bool Tencent             { get; private set; } = false;
        public bool IgnoreWarnings      { get; private set; } = false;
        public string CertPath          { get; private set; } = "nx_tls_client_cert.pem";
        public string KeysetPath        { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".switch/prod.keys");
        public Keyset? Keyset           { get; private set; } = null;
        public string? OutPath          { get; private set; } = null;
        public string DeviceID          { get; private set; } = "DEADCAFEBABEBEEF";
        public string Env               { get; private set; } = "lp1";
        public string Server            { get; private set; } = "d4c";
        public string Platform          { get; private set; } = "NX";
        public string FirmwareVersion   { get; private set; } = "5.1.0-3";
        public int MaxJobs              { get; private set; } = 5;
        public string[]? TitleFilter    { get; private set; } = null;
        public bool ConsoleVerbose      { get; private set; } = false;
        public string? FileVerbose      { get; private set; } = null;
        public bool OnlyMeta            { get; private set; } = false;

        class Handler
        {
            public string[] Aliases;

            public bool RequiresArg => FnArg != null;

            private Action<string>? FnArg;
            private Action? Fn;

            public Handler(string[] Aliases, Action<string> Fn)
            {
                this.Aliases = Aliases;
                this.FnArg = Fn;
            }

            public Handler(string[] Aliases, Action Fn)
            {
                this.Aliases = Aliases;
                this.Fn = Fn;
            }

            public void Invoke(string arg) 
            {
                if (FnArg != null)
                    FnArg(arg);
                else
                    Fn!();
            }
        }

        public HandlerArgs(string[] raw_args)
        {
            Handler[] Handlers = new []{
            // Operation modes
                new Handler ( Aliases: new[] { "--get-info", "--info", "-i" },     Fn: () => OperationMode = Mode.GetInfo ),
                new Handler ( Aliases: new[] { "--get-latest", "--latest", "-l" }, Fn: () => OperationMode = Mode.GetLatest ),
                new Handler ( Aliases: new[] { "--help", "-h" },                   Fn: () => OperationMode = Mode.Help ),
            // Args         
                new Handler (Aliases: new[] { "--cert", "-c" },                  Fn: x => CertPath = x ),
                new Handler (Aliases: new[] { "--keyset", "-k" },                Fn: x => KeysetPath = x ),
                new Handler (Aliases: new[] { "--out-path", "--out", "-o" },     Fn: x => OutPath = x ),
                new Handler (Aliases: new[] { "--device-id", "-did" },           Fn: x => DeviceID = x ),
                new Handler (Aliases: new[] { "--environment", "-env" },         Fn: x => Env = x ),
                new Handler (Aliases: new[] { "--server", "-s" },                Fn: x => Server = x ),
                new Handler (Aliases: new[] { "--platform", "-p" },              Fn: x => Platform = x ),
                new Handler (Aliases: new[] { "--firmware-version", "-fwver" },  Fn: x => FirmwareVersion = x ),
                new Handler (Aliases: new[] { "--jobs", "-j" },                  Fn: x => MaxJobs = Math.Min(int.Parse(x), 1) ),
                new Handler (Aliases: new[] { "--titles" },                      Fn: x => TitleFilter = x.Split(',') ),
                new Handler (Aliases: new[] { "-vf" },                           Fn: x => FileVerbose = x ),
            // Flags            
                new Handler (Aliases: new[] { "-v" },                            Fn: () => ConsoleVerbose = true ),
                new Handler (Aliases: new[] { "--tencent", "-t" },               Fn: () => Tencent = true ),
                new Handler (Aliases: new[] { "--ignore-warnings",
                                               "--no-confirm", "-q" },           Fn: () => IgnoreWarnings = true ),
                new Handler (Aliases: new[] { "--only-meta" },                   Fn: () => OnlyMeta = true)
            };

            for (int i = 0; i < raw_args.Length; ++i)
            {
                var handler = Handlers.FirstOrDefault(x => x.Aliases.Contains(raw_args[i]));

                if (handler is null)
                    Console.WriteLine($"Warning: unknown arg {raw_args[i]}");
                else
                    handler.Invoke(handler.RequiresArg ? raw_args[++i] : null!);
            }

            if (OperationMode is null)
                return;

            CertPath = Path.GetFullPath(CertPath);
            KeysetPath = Path.GetFullPath(KeysetPath);
            if (!String.IsNullOrEmpty(OutPath))
                OutPath = Path.GetFullPath(OutPath);

            // keysets are only needed for a full download
            if (OperationMode == Mode.GetLatest)
                Keyset = ExternalKeyReader.ReadKeyFile(KeysetPath);
        }

        public static void PrintHelp()
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
                + "-vf          path/to/log.txt                     Verbose log to file\n"
                + "--only-meta                                      Only download and parse meta entries\n"
            );
        }
    }
}
