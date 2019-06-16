using Microsoft.Azure.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace WebProxyOverIoTHub.ClientSideProxy
{
    public class ClientSideProxy : IHostedService
    {
        private readonly IApplicationLifetime applicationLifetime;
        private readonly LocalSettings localSettings;
        private readonly ServiceClient serviceClient;

        public ClientSideProxy(IApplicationLifetime applicationLifetime, IOptions<LocalSettings> localSettings)
        {
            this.applicationLifetime = applicationLifetime;
            this.localSettings = localSettings.Value;

            this.serviceClient = ServiceClient.CreateFromConnectionString(
                this.localSettings.IoTHubServiceConnectionString
                , TransportType.Amqp_WebSocket_Only
                , new ServiceClientTransportSettings
                {
                    AmqpProxy = new WebProxy(this.localSettings.UpstreamWebProxy),
                    HttpProxy = new WebProxy(this.localSettings.UpstreamWebProxy),
                });
            HackServiceClientProxy();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("StartAsync");

            await serviceClient.OpenAsync();
            Console.WriteLine("IoTHubへのサービス接続完了");

            await new DeviceStream(serviceClient
                , localSettings.TargetDeviceId
                , localSettings.LocalWebProxyPort
                , localSettings.UpstreamWebProxy).RunAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("StopAsync");
            await serviceClient.CloseAsync();
        }

        private void HackServiceClientProxy()
        {
            var httpClientHelper = serviceClient.GetType().GetField("httpClientHelper", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(serviceClient);
            var srcHttpClient = httpClientHelper.GetType().GetField("httpClientObj", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(httpClientHelper) as HttpClient;
            var destHttpClient = httpClientHelper.GetType().GetField("httpClientObjWithPerRequestTimeout", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(httpClientHelper) as HttpClient;
            var field_HttpMessageInvoker = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance);
            var srcHttpClientHandler = field_HttpMessageInvoker.GetValue(srcHttpClient) as HttpClientHandler;
            var destHttpClientHandler = field_HttpMessageInvoker.GetValue(destHttpClient) as HttpClientHandler;
            destHttpClientHandler.Proxy = srcHttpClientHandler.Proxy;
        }
    }
}
