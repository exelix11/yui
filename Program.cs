namespace yui
{
    class Program
    {
        static void Main(string[] args)
        {
            var Parsed = new HandlerArgs(args);
            
            if (Parsed.OperationMode is null || Parsed.OperationMode == HandlerArgs.Mode.Help)
            {
                HandlerArgs.PrintHelp();
                return;
            }

            using (var ctx = new SysUpdateHandler(Parsed))
            {
                if (Parsed.OperationMode == HandlerArgs.Mode.GetInfo)
                    ctx.PrintLatestSysVersion();
            
                if (Parsed.OperationMode == HandlerArgs.Mode.GetLatest)
                    ctx.GetLatest();
            }
        }
    }
}
