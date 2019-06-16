using Microsoft.Azure.Devices;
using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WebProxyOverIoTHub.ClientSideProxy
{
    public class DeviceStream
    {
        private readonly ServiceClient serviceClient;
        private readonly string deviceId;
        private readonly int localWebProxyPort;
        private readonly string upstreamWebProxy;
        private readonly TcpListener tcpListener;

        public DeviceStream(ServiceClient serviceClient, string deviceId, int localWebProxyPort, string upstreamWebProxy)
        {
            this.serviceClient = serviceClient;
            this.deviceId = deviceId;
            this.localWebProxyPort = localWebProxyPort;
            this.upstreamWebProxy = upstreamWebProxy;
            this.tcpListener = new TcpListener(IPAddress.Loopback, localWebProxyPort);
        }
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            tcpListener.Start();
            Console.WriteLine("TcpListener.Start");
            while (true)
            {
                var tcpClient = await tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
                HandleIncomingConnectionsAndCreateStreams(cancellationToken,tcpClient);

                if (cancellationToken.IsCancellationRequested)
                {
                    tcpListener.Stop();
                    Console.WriteLine("TcpListener.Stop");
                    break;
                }
            }
        }

        private async Task HandleIncomingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] receiveBuffer = new byte[10240];

            while (localStream.CanRead)
            {
                var receiveResult = await remoteStream.ReceiveAsync(receiveBuffer, cancellationToken).ConfigureAwait(false);

                if (receiveResult.Count > 0)
                {
                    Console.WriteLine($"incoming:{receiveResult.Count}byte");
                }

                await localStream.WriteAsync(receiveBuffer, 0, receiveResult.Count).ConfigureAwait(false);
            }
        }

        private async Task HandleOutgoingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[10240];

            while (remoteStream.State == WebSocketState.Open)
            {
                int receiveCount = await localStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

                if (receiveCount > 0)
                {
                    Console.WriteLine($"outgoing:{receiveCount}byte");
                }

                await remoteStream.SendAsync(new ArraySegment<byte>(buffer, 0, receiveCount), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
        }

        private async void HandleIncomingConnectionsAndCreateStreams(CancellationToken cancellationToken,TcpClient tcpClient)
        {
            DeviceStreamRequest deviceStreamRequest = new DeviceStreamRequest(
                streamName: "TestStream"
            );

            using (var localStream = tcpClient.GetStream())
            {
                DeviceStreamResponse result = await serviceClient.CreateStreamAsync(deviceId, deviceStreamRequest, CancellationToken.None).ConfigureAwait(false);

                Console.WriteLine($"Stream response received: Name={deviceStreamRequest.StreamName} IsAccepted={result.IsAccepted}");

                if (result.IsAccepted)
                {
                    try
                    {
                        using (var remoteStream = await GetStreamingClientAsync(result.Url, result.AuthorizationToken, cancellationToken).ConfigureAwait(false))
                        {
                            Console.WriteLine("Starting streaming");

                            await Task.WhenAny(
                                HandleIncomingDataAsync(localStream, remoteStream, cancellationToken),
                                HandleOutgoingDataAsync(localStream, remoteStream, cancellationToken)).ConfigureAwait(false);
                        }

                        Console.WriteLine("Done streaming");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Got an exception: {0}", ex);
                    }
                }
            }
            tcpClient.Close();
        }

        public async Task<ClientWebSocket> GetStreamingClientAsync(Uri url, string authorizationToken, CancellationToken cancellationToken)
        {
            ClientWebSocket wsClient = new ClientWebSocket();
            wsClient.Options.SetRequestHeader("Authorization", "Bearer " + authorizationToken);
            wsClient.Options.Proxy = new WebProxy(upstreamWebProxy);
            Console.WriteLine($"connect to:{url.ToString()}");
            await wsClient.ConnectAsync(url, cancellationToken).ConfigureAwait(false);

            return wsClient;
        }
    }
}
