namespace yui
{
    class Program
    {
        static void Main(string[] args)
        {    
            using (var ctx = new SysUpdateHandler(args))
            {
                if (ctx.Args.get_info)
                    ctx.PrintLatestSysVersion();
            
                if (ctx.Args.get_latest)
                    ctx.GetLatest();
            }

        }
    }
}
