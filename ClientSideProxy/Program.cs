using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace WebProxyOverIoTHub.ClientSideProxy
{
    class Program
    {

        static async Task Main(string[] args)
        {
            TaskScheduler.UnobservedTaskException
              += TaskScheduler_UnobservedTaskException;
            await new HostBuilder()
                .ConfigureHostConfiguration(config =>
                {
                    config.AddCommandLine(args);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<Config>(options => hostContext.Configuration.Bind(options));
                    services.AddHostedService<ClientSideProxy>();
                })
                .RunConsoleAsync();
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Console.WriteLine(e.Exception.InnerException);
        }
    }
}
