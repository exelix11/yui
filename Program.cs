namespace yui
{
    class Program
    {
        static void Main(string[] args)
        {    
            using (var ctx = new SysUpdateHandler(args))
            {
                if (ctx.ParsedArgs.get_info)
                    ctx.PrintLatestSysVersion();
            
                if (ctx.ParsedArgs.get_latest)
                    ctx.GetLatest(ctx.ParsedArgs.out_path);
            }

        }
    }
}
