using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Threading.Tasks;

namespace WebProxyOverIoTHub.ClientSideProxy
{
    class Program
    {

        static async Task Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureHostConfiguration(config =>
                {
                    config.SetBasePath(Directory.GetCurrentDirectory());
                    config.AddJsonFile("localSettings.json", optional: true);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<LocalSettings>(options => hostContext.Configuration.Bind(options));
                    services.AddHostedService<ClientSideProxy>();
                })
                .Build();
            using (host)
            {
                await host.RunAsync();
            }
        }
    }
}
