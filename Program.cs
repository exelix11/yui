namespace yui
{
    class Program
    {
        static void Main(string[] args)
        {    
            using (var ctx = new SysUpdateHandler(args))
            {
                if (ctx.args.get_info)
                    ctx.PrintLatestSysVersion();
            
                if (ctx.args.get_latest)
                    ctx.GetLatestFull(ctx.args.out_path);
            }

        }
    }
}
