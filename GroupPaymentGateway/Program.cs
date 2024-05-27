using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Web;

namespace lk.Server.GroupPaymentGateway
{
    public class Program
    {
        static string[] Urls = new string[] { "http://*:5005" };

        public static async Task Main(string[] args)
        {
            NLogBuilder.ConfigureNLog("Nlogs/nlog.config").GetCurrentClassLogger();

            await CreateHostBuilder(args).Build().RunAsync();

            LogManager.Shutdown();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder => 
        { 
            webBuilder.UseUrls(Urls).UseStartup<Startup>().UseNLog(); 
        });
    }
}
