using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace WebProxyOverIoTHub.ServerSideProxy
{
    class Program
    {
        static async Task Main()
        {
            TaskScheduler.UnobservedTaskException
              += TaskScheduler_UnobservedTaskException;
            var host = new HostBuilder()
                .ConfigureWebJobs(webjobBuilder =>
                {
                    webjobBuilder.AddAzureStorageCoreServices();
                })
                .ConfigureLogging((builderContext, loggingBuilder) =>
                {
                    loggingBuilder.AddConsole();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<IJobHost>(new ServerSideProxy());
                })
                .Build();

            var cancellationToken = new WebJobsShutdownWatcher().Token;
            cancellationToken.Register(() =>
            {
                host.Services.GetService<IJobHost>().StopAsync();
            });

            using (host)
            {
                await host.Services.GetService<IJobHost>().StartAsync(cancellationToken);
            }
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Console.WriteLine(e.Exception.InnerException);
        }
    }
}
