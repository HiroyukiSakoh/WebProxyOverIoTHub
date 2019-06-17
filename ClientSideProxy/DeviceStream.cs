using Microsoft.Azure.Devices;
using System;
using System.Collections.Concurrent;
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
        private readonly string upstreamWebProxy;
        private readonly string streamName;
        private readonly TcpListener tcpListener;

        public DeviceStream(ServiceClient serviceClient, string deviceId, int localPort, string upstreamWebProxy,string streamName)
        {
            this.serviceClient = serviceClient;
            this.deviceId = deviceId;
            this.upstreamWebProxy = upstreamWebProxy;
            this.streamName = streamName;
            this.tcpListener = new TcpListener(IPAddress.Loopback, localPort);
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            tcpListener.Start();
            Console.WriteLine($"TCP接続待受開始 port:{((IPEndPoint)tcpListener.LocalEndpoint).Port}");

            var streamRequest = new DeviceStreamRequest(streamName: this.streamName);
            Console.WriteLine($"デバイスストリーム接続要求 送信 Name:{streamRequest.StreamName}");
            DeviceStreamResponse result = await serviceClient.CreateStreamAsync(deviceId, streamRequest, CancellationToken.None).ConfigureAwait(false);
            if (!result.IsAccepted)
            {
                Console.WriteLine($"デバイスストリーム接続要求 拒否応答 Name:{result.StreamName}");
                tcpListener.Stop();
                Console.WriteLine("TcpListener.Stop");
                return;
            }
            Console.WriteLine($"デバイスストリーム接続要求 受諾応答受信 Name:{result.StreamName}");
            var webSocket = await GetStreamingClientAsync(result.Url, result.AuthorizationToken, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"デバイスストリーム(WebSocket)接続 開始 Name:{result.StreamName}");
            _ = Task.Run(() => HandleResponseAsync(webSocket, cancellationToken));
            _ = Task.Run(() => ReportStatus(cancellationToken));

            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var tcpClient = await tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
                        _ = Task.Run(() => HandleRequestAsync(tcpClient, webSocket, cancellationToken));
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            });
        }

        public void Stop()
        {
            foreach (var streamIndex in TcpConnectionDictionary.Keys)
            {
                CloseTcpConnection(streamIndex);
            }
            tcpListener.Stop();
            Console.WriteLine("TCP接続待受停止");
        }

        public void ReportStatus(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"ReportStatus-{TcpConnectionDictionary.Keys.Count}");

                Thread.Sleep(10 * 1000);
            }
        }


        private const int StreamIndexInfra = 0;
        private int StreamCount = 0;
        private int GetStreamCount()
        {
            var count = Interlocked.Increment(ref StreamCount);
            if (count == 0)
            {
                return Interlocked.Increment(ref StreamCount);
            }
            return count;
        }

        private readonly ConcurrentDictionary<int, TcpConnection> TcpConnectionDictionary = new ConcurrentDictionary<int, TcpConnection>();

        private class TcpConnection
        {
            public TcpClient TcpClient { get; set; }
            public NetworkStream NetworkStream { get; set; }
        }

        private async Task HandleRequestAsync(TcpClient tcpClient, ClientWebSocket webSocket, CancellationToken cancellationToken)
        {
            var streamIndex = GetStreamCount();
            TcpConnectionDictionary.TryAdd(streamIndex, new TcpConnection()
            {
                TcpClient = tcpClient,
                NetworkStream = tcpClient.GetStream(),
            });
            var remoteAddress = ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address;
            var remotePort = ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Port;
            Console.WriteLine($"TCP接続 開始 Id:{streamIndex},RemoteAddress:{remoteAddress},RemotePort:{remotePort}");
            byte[] buffer = new byte[10240];
            BitConverter.TryWriteBytes(new ArraySegment<byte>(buffer, 0, 4), streamIndex);

            try
            {
                while (webSocket.State == WebSocketState.Open
                    || TcpConnectionDictionary[streamIndex].NetworkStream.CanRead)
                {
                    int receiveCount = await TcpConnectionDictionary[streamIndex].NetworkStream.ReadAsync(buffer, 4, buffer.Length - 4).ConfigureAwait(false);
                    if (receiveCount == 0)
                    {
                        await CloseTcpConnectionAndSendCloseMessageAsync(streamIndex, webSocket, cancellationToken);
                        break;
                    }
                    await webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, receiveCount + 4), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (WebSocketException ex) when(ex.WebSocketErrorCode==WebSocketError.InvalidState)
            {
                //abort
            }
            catch (System.IO.IOException ex)
                when ((ex.InnerException as SocketException)?.SocketErrorCode == SocketError.OperationAborted
                    || (ex.InnerException as SocketException)?.SocketErrorCode == SocketError.ConnectionAborted
                    || (ex.InnerException as SocketException)?.SocketErrorCode == SocketError.ConnectionReset)
            {
                //abort
            }
            catch (System.ObjectDisposedException)
            {
                //abort
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Got an exception {nameof(HandleRequestAsync)}-{streamIndex}: {{0}}", ex);
            }
            finally
            {
                await CloseTcpConnectionAndSendCloseMessageAsync(streamIndex, webSocket, cancellationToken);
            }
            Console.WriteLine($"TCP接続 終了 Id:{streamIndex},RemoteAddress:{remoteAddress},RemotePort:{remotePort}");
        }

        private async Task HandleResponseAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
        {
            try
            {
                byte[] receiveBuffer = new byte[10240];
                while (!cancellationToken.IsCancellationRequested)
                {
                    var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer, 0, receiveBuffer.Length), cancellationToken).ConfigureAwait(false);

                    if (receiveResult.Count != 0)
                    {

                        var streamIndex = BitConverter.ToInt32(receiveBuffer, 0);
                        if (streamIndex == StreamIndexInfra)
                        {
                            CloseTcpConnection(BitConverter.ToInt32(receiveBuffer, 4));
                            continue;
                        }
                        if (!TcpConnectionDictionary.TryGetValue(streamIndex, out var tcpConnection))
                        {
                            break;
                        }

                        try
                        {
                            await tcpConnection.NetworkStream.WriteAsync(receiveBuffer, 4, receiveResult.Count - 4).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Got an exception {nameof(HandleResponseAsync)}-{streamIndex}: {{0}}", ex);
                            await CloseTcpConnectionAndSendCloseMessageAsync(streamIndex, webSocket, cancellationToken);
                        }
                    }
                }
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                Console.WriteLine("WebSocketがサーバー側から切断されました");
                this.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private async Task CloseTcpConnectionAndSendCloseMessageAsync(int streamIndex, ClientWebSocket webSocket, CancellationToken cancellationToken)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    var message = new Byte[8];
                    BitConverter.TryWriteBytes(new ArraySegment<byte>(message, 0, 4), StreamIndexInfra);
                    BitConverter.TryWriteBytes(new ArraySegment<byte>(message, 4, 4), streamIndex);
                    await webSocket.SendAsync(message, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                    Console.WriteLine($"\t{nameof(CloseTcpConnectionAndSendCloseMessageAsync)}-{streamIndex}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Got an exception {nameof(CloseTcpConnectionAndSendCloseMessageAsync)}-{streamIndex}: {{0}}", ex);
            }
            finally
            {
                CloseTcpConnection(streamIndex);
            }
        }

        private void CloseTcpConnection(int streamIndex)
        {
            try
            {
                if (TcpConnectionDictionary.TryGetValue(streamIndex, out var connection))
                {
                    Console.WriteLine($"TCP接続 終了 Id:{streamIndex},RemoteAddress:{((IPEndPoint)connection.TcpClient.Client.RemoteEndPoint).Address},RemotePort:{((IPEndPoint)connection.TcpClient.Client.RemoteEndPoint).Port}");
                    connection.NetworkStream.Dispose();
                    connection.TcpClient.Dispose();
                    TcpConnectionDictionary.TryRemove(streamIndex, out var value);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Got an exception {nameof(CloseTcpConnection)}-{streamIndex}: {{0}}", ex);
            }
        }

        public async Task<ClientWebSocket> GetStreamingClientAsync(Uri url, string authorizationToken, CancellationToken cancellationToken)
        {
            ClientWebSocket wsClient = new ClientWebSocket();
            wsClient.Options.SetRequestHeader("Authorization", "Bearer " + authorizationToken);
            if (upstreamWebProxy != null)
            {
                wsClient.Options.Proxy = new WebProxy(upstreamWebProxy);
            }
            Console.WriteLine($"Connect to:{url.ToString()}");
            await wsClient.ConnectAsync(url, cancellationToken).ConfigureAwait(false);

            return wsClient;
        }
    }
}
