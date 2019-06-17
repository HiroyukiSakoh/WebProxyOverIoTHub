using Microsoft.Azure.Devices;
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
        private readonly Config config;
        private readonly ServiceClient serviceClient;
        private readonly DeviceStream deviceStream;
        private readonly InternalWebProxyServer internalWebProxyServer;

        public ClientSideProxy(IOptions<Config> config)
        {
            this.config = config.Value;
            if (this.config.UpstreamWebProxy != null)
            {
                this.serviceClient = ServiceClient.CreateFromConnectionString(
                    this.config.IoTHubServiceConnectionString
                    , TransportType.Amqp_WebSocket_Only
                    , new ServiceClientTransportSettings
                    {
                        AmqpProxy = new WebProxy(this.config.UpstreamWebProxy),
                        HttpProxy = new WebProxy(this.config.UpstreamWebProxy),
                    });
            }
            else
            {
                this.serviceClient = ServiceClient.CreateFromConnectionString(
                    this.config.IoTHubServiceConnectionString
                    , TransportType.Amqp_WebSocket_Only);
            }
            HackServiceClientProxy();
            //ローカルのWebProxyを立てる
            internalWebProxyServer = new InternalWebProxyServer(this.config.LocalInternalWebProxyPort, this.config.LocalDeviceStreamPort);

            this.deviceStream = new DeviceStream(serviceClient
                , this.config.TargetDeviceId
                , this.config.LocalDeviceStreamPort
                , this.config.UpstreamWebProxy
                , this.config.StreamName);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            internalWebProxyServer.Start();
            Console.WriteLine($"InternalWebProxyServer start port:{this.config.LocalDeviceStreamPort}");

            await serviceClient.OpenAsync();
            Console.WriteLine("IoTHubへのサービス接続完了");

            await this.deviceStream.StartAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                this.deviceStream.Stop();
                await serviceClient?.CloseAsync();
                internalWebProxyServer?.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
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
