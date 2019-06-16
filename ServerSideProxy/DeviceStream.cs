using Microsoft.Azure.Devices.Client;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebProxyOverIoTHub.ServerSideProxy
{
    class DeviceStream
    {
        private readonly DeviceClient deviceClient;
        private readonly string host;
        private readonly int port;

        public DeviceStream(DeviceClient deviceClient, string host, int port)
        {
            this.deviceClient = deviceClient;
            this.host = host;
            this.port = port;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                DeviceStreamRequest streamRequest = await deviceClient.WaitForDeviceStreamRequestAsync(cancellationToken).ConfigureAwait(false);
                if (streamRequest != null)
                {
                    Handle(cancellationToken, streamRequest);
                }
            }
        }

        private static async Task HandleIncomingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[10240];

            while (remoteStream.State == WebSocketState.Open)
            {
                var receiveResult = await remoteStream.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (receiveResult.Count > 0)
                {
                    Console.WriteLine($"incoming:{receiveResult.Count}byte");
                }

                await localStream.WriteAsync(buffer, 0, receiveResult.Count).ConfigureAwait(false);
            }
        }

        private static async Task HandleOutgoingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[10240];

            while (localStream.CanRead)
            {
                int receiveCount = await localStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

                if (receiveCount > 0)
                {
                    Console.WriteLine($"outgoing:{receiveCount}byte");
                }

                await remoteStream.SendAsync(new ArraySegment<byte>(buffer, 0, receiveCount), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
        }

        private async void Handle(CancellationToken cancellationToken, DeviceStreamRequest streamRequest)
        {
            try
            {
                await deviceClient.AcceptDeviceStreamRequestAsync(streamRequest, cancellationToken).ConfigureAwait(false);

                using (ClientWebSocket webSocket = await GetStreamingClientAsync(streamRequest.Url, streamRequest.AuthorizationToken, cancellationToken).ConfigureAwait(false))
                {
                    using (TcpClient tcpClient = new TcpClient())
                    {
                        await tcpClient.ConnectAsync(host, port).ConfigureAwait(false);

                        using (NetworkStream localStream = tcpClient.GetStream())
                        {
                            Console.WriteLine("Starting streaming");

                            await Task.WhenAny(
                                HandleIncomingDataAsync(localStream, webSocket, cancellationToken),
                                HandleOutgoingDataAsync(localStream, webSocket, cancellationToken)).ConfigureAwait(false);

                            localStream.Close();
                        }
                    }

                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Got an exception: {0}", ex);
            }
        }
        public static async Task<ClientWebSocket> GetStreamingClientAsync(Uri url, string authorizationToken, CancellationToken cancellationToken)
        {
            ClientWebSocket wsClient = new ClientWebSocket();
            wsClient.Options.SetRequestHeader("Authorization", "Bearer " + authorizationToken);
            Console.WriteLine($"connect to:{url.ToString()}");
            await wsClient.ConnectAsync(url, cancellationToken).ConfigureAwait(false);

            return wsClient;
        }
    }
}
