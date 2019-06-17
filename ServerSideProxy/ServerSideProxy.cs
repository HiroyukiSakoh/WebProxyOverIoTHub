using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.WebJobs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WebProxyOverIoTHub.ServerSideProxy
{

    public class ServerSideProxy : IJobHost
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="arguments"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
#pragma warning disable CS1998
        public async Task CallAsync(string name, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
#pragma warning restore CS1998
        {
        }

        /// <summary>
        /// 内部WebProxyのポート
        /// </summary>
        /// <returns></returns>
        private int GetInternalWebProxyPort()
        {
            return int.Parse(Environment.GetEnvironmentVariable("WEBJOBS_PORT"));
        }

        /// <summary>
        /// IoTHub接続文字列
        /// </summary>
        /// <returns></returns>
        private string GetIoTHubConnectionString()
        {
            return Environment.GetEnvironmentVariable("APPSETTING_IOTHUB_CONNECTION_STRING");
        }

        private InternalWebProxyServer internalWebProxyServer;
        private DeviceClient deviceClient;
        private DeviceStream deviceStream;

        /// <summary>
        /// WebJob 開始
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                //ローカルのWebProxyを立てる
                internalWebProxyServer = new InternalWebProxyServer(GetInternalWebProxyPort());
                internalWebProxyServer.Start();
                Console.WriteLine($"InternalWebProxyServer start port:{GetInternalWebProxyPort()}");

                //IoTHubにDeviceClientとして接続
#if DEBUG
                deviceClient = DeviceClient.CreateFromConnectionString(GetIoTHubConnectionString(), new ITransportSettings[] { new AmqpTransportSettings(TransportType.Amqp_WebSocket_Only){
                    Proxy = new System.Net.WebProxy("localhost:8888"),
                }
                });
#else
                deviceClient = DeviceClient.CreateFromConnectionString(GetIoTHubConnectionString(), new ITransportSettings[] { new AmqpTransportSettings(TransportType.Amqp_Tcp_Only) });
#endif
                await deviceClient.OpenAsync();
                Console.WriteLine("IoTHubへのデバイス接続完了");

                deviceStream = new DeviceStream(deviceClient, "localhost", GetInternalWebProxyPort());
                await deviceStream.StartAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        /// <summary>
        /// WebJob 停止
        /// </summary>
        /// <returns></returns>
#pragma warning disable CS1998
        public async Task StopAsync()
#pragma warning restore CS1998
        {
            try
            {
                deviceStream.Stop();
                deviceClient?.Dispose();
                internalWebProxyServer?.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
