using Microsoft.Azure.Devices.Client;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.WebSockets;
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

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("デバイスストリーム接続要求 待受開始");
            while (!cancellationToken.IsCancellationRequested)
            {
                DeviceStreamRequest streamRequest = await deviceClient.WaitForDeviceStreamRequestAsync(cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"デバイスストリーム接続要求 受信 Name:{streamRequest.Name},Id{streamRequest.RequestId}");
                await deviceClient.AcceptDeviceStreamRequestAsync(streamRequest, cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleDeviceStream(streamRequest, cancellationToken));
            }
        }

        public void Stop()
        {
            foreach (var streamIndex in TcpConnectionDictionary.Keys)
            {
                CloseTcpConnection(streamIndex);
            }
        }

        private async Task HandleDeviceStream(DeviceStreamRequest streamRequest, CancellationToken cancellationToken)
        {
            var webSocket = await GetStreamingClientAsync(streamRequest.Url, streamRequest.AuthorizationToken, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"デバイスストリーム(WebSocket)接続 開始 Name:{streamRequest.Name},Id{streamRequest.RequestId}");
            _ = Task.Run(() => HandleRequestAsync(streamRequest.Name,webSocket, cancellationToken));

        }
        private const int StreamIndexInfra = 0;

        private readonly Dictionary<ConnectionKey, TcpConnection> TcpConnectionDictionary = new Dictionary<ConnectionKey, TcpConnection>();
        private class TcpConnection
        {
            public TcpClient TcpClient { get; set; }
            public NetworkStream NetworkStream { get; set; }
        }
        private struct ConnectionKey
        {
            public string StreamName;
            public int StreamIndex;
        }

        private async Task HandleRequestAsync(string streamName,ClientWebSocket webSocket, CancellationToken cancellationToken)
        {
            try
            {
                byte[] receiveBuffer = new byte[10240];
                while (webSocket.State == WebSocketState.Open)
                {
                    var receiveResult = await webSocket.ReceiveAsync(receiveBuffer, cancellationToken).ConfigureAwait(false);
                    var key = new ConnectionKey
                    {
                        StreamName = streamName,
                        StreamIndex = BitConverter.ToInt32(receiveBuffer, 0)
                    };
                    if (key.StreamIndex == StreamIndexInfra)
                    {
                        key.StreamIndex = BitConverter.ToInt32(receiveBuffer, 4);
                        CloseTcpConnection(key);
                        continue;
                    }
                    try
                    {
                        if (!TcpConnectionDictionary.ContainsKey(key))
                        {
                            var tcpClient = new TcpClient();
                            await tcpClient.ConnectAsync(host, port).ConfigureAwait(false);
                            TcpConnectionDictionary.Add(key, new TcpConnection()
                            {
                                TcpClient = tcpClient,
                                NetworkStream = tcpClient.GetStream(),
                            });
                            Console.WriteLine($"\t NewStream-{key.StreamName}-{key.StreamIndex}");
                            _ = Task.Run(() => HandleResponseAsync(key, webSocket, cancellationToken));
                        }
                        if (receiveResult.Count != 0)
                        {
                            if (!TcpConnectionDictionary[key].TcpClient.Connected)
                            {
                                Console.WriteLine($"TcpClient closed {nameof(HandleResponseAsync)}-{key.StreamName}-{key.StreamIndex}");
                                await CloseTcpConnectionAndSendCloseMessageAsync(key, webSocket, cancellationToken);
                                continue;
                            }
                            await TcpConnectionDictionary[key].NetworkStream.WriteAsync(receiveBuffer, 4, receiveResult.Count - 4).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Got an exception {nameof(HandleRequestAsync)}-{key.StreamName}-{key.StreamIndex}: {{0}}", ex);
                        await CloseTcpConnectionAndSendCloseMessageAsync(key, webSocket, cancellationToken);
                    }
                }

            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                Console.WriteLine($"WebSocketがクライアント側から切断されました:{streamName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private async Task HandleResponseAsync(ConnectionKey key, ClientWebSocket webSocket, CancellationToken cancellationToken)
        {
            try
            {
                byte[] receiveBuffer = new byte[10240];
                BitConverter.TryWriteBytes(new ArraySegment<byte>(receiveBuffer, 0, 4), key.StreamIndex);
                while (TcpConnectionDictionary[key].NetworkStream.CanRead)
                {
                    int receiveCount = await TcpConnectionDictionary[key].NetworkStream.ReadAsync(receiveBuffer, 4, receiveBuffer.Length - 4).ConfigureAwait(false);
                    if (receiveCount == 0)
                    {
                        await CloseTcpConnectionAndSendCloseMessageAsync(key, webSocket, cancellationToken);
                        break;
                    }
                    await webSocket.SendAsync(new ArraySegment<byte>(receiveBuffer, 0, receiveCount + 4), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                }

            }
            catch (System.IO.IOException ex)
                when ((ex.InnerException as SocketException)?.SocketErrorCode == SocketError.OperationAborted
                    || (ex.InnerException as SocketException)?.SocketErrorCode == SocketError.ConnectionAborted
                    || (ex.InnerException as SocketException)?.SocketErrorCode == SocketError.ConnectionReset)
            {
                //abort
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Got an exception {nameof(HandleResponseAsync)}-{key.StreamName}-{key.StreamIndex}:{{0}}", ex);
                await CloseTcpConnectionAndSendCloseMessageAsync(key, webSocket, cancellationToken);
            }
        }
        private async Task CloseTcpConnectionAndSendCloseMessageAsync(ConnectionKey key, ClientWebSocket webSocket, CancellationToken cancellationToken)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    var message = new Byte[8];
                    BitConverter.TryWriteBytes(new ArraySegment<byte>(message, 0, 4), StreamIndexInfra);
                    BitConverter.TryWriteBytes(new ArraySegment<byte>(message, 4, 4), key.StreamIndex);
                    await webSocket.SendAsync(message, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                    Console.WriteLine($"\t{nameof(CloseTcpConnectionAndSendCloseMessageAsync)}-{key.StreamName}-{key.StreamIndex}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Got an exception {nameof(CloseTcpConnectionAndSendCloseMessageAsync)}-{key.StreamName}-{key.StreamIndex}:{{0}}", ex);
            }
            finally
            {
                CloseTcpConnection(key);
            }
        }

        private void CloseTcpConnection(ConnectionKey key)
        {
            try
            {
                if (TcpConnectionDictionary.ContainsKey(key))
                {
                    TcpConnectionDictionary[key].NetworkStream.Dispose();
                    TcpConnectionDictionary[key].TcpClient.Dispose();
                    TcpConnectionDictionary.Remove(key);
                    Console.WriteLine($"\t{nameof(CloseTcpConnection)}-{key.StreamName}-{key.StreamIndex}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Got an exception {nameof(CloseTcpConnection)}-{key.StreamName}-{key.StreamIndex}:{{0}}", ex);
            }
        }
        public static async Task<ClientWebSocket> GetStreamingClientAsync(Uri url, string authorizationToken, CancellationToken cancellationToken)
        {
            ClientWebSocket wsClient = new ClientWebSocket();
            wsClient.Options.SetRequestHeader("Authorization", "Bearer " + authorizationToken);
#if DEBUG
            wsClient.Options.Proxy = new System.Net.WebProxy("localhost:8888");
#endif
            Console.WriteLine($"Connect to:{url.ToString()}");
            await wsClient.ConnectAsync(url, cancellationToken).ConfigureAwait(false);

            return wsClient;
        }
    }
}
