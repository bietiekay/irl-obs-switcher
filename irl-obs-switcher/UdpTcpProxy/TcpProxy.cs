#nullable enable
using ConsoleLogger;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetProxy
{
    internal class TcpProxy : IProxy
    {
        /// <summary>
        /// Milliseconds
        /// </summary>
        public int ConnectionTimeout { get; set; } = (4 * 60 * 1000);
        public IRLOBSSwitcher.OBSManager? OBS_SceneManager;

        public async Task Start(string remoteServerHostNameOrAddress, ushort timeOut, ushort sceneSwitchOverTime, ushort minimalKbitperSecond, ushort remoteServerPort, ushort localPort, IRLOBSSwitcher.OBSManager OBS_Manager, string? localIp)
        {
            ConnectionTimeout = timeOut;
            var connections = new ConcurrentBag<TcpConnection>();
            OBS_SceneManager = OBS_Manager;

            IPAddress localIpAddress = string.IsNullOrEmpty(localIp) ? IPAddress.IPv6Any : IPAddress.Parse(localIp);
            var localServer = new TcpListener(new IPEndPoint(localIpAddress, localPort));
            localServer.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            localServer.Start();

            ConsoleLog.WriteLine($"TCP proxy started [{localIpAddress}]:{localPort} -> [{remoteServerHostNameOrAddress}]:{remoteServerPort}");

            var _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

                    var tempConnections = new List<TcpConnection>(connections.Count);
                    while (connections.TryTake(out var connection))
                    {
                        tempConnections.Add(connection);
                    }

                    foreach (var tcpConnection in tempConnections)
                    {
                        if (tcpConnection.LastActivity + ConnectionTimeout < Environment.TickCount64)
                        {
                            tcpConnection.Stop();
                        }
                        else
                        {
                            connections.Add(tcpConnection);
                        }
                    }
                }
            });

            while (true)
            {
                try
                {
                    var ips = await Dns.GetHostAddressesAsync(remoteServerHostNameOrAddress).ConfigureAwait(false);

                    var tcpConnection = await TcpConnection.AcceptTcpClientAsync(OBS_SceneManager, localServer,
                            new IPEndPoint(ips[0], remoteServerPort), sceneSwitchOverTime, minimalKbitperSecond)
                        .ConfigureAwait(false);
                    tcpConnection.Run();
                    connections.Add(tcpConnection);
                }
                catch (Exception ex)
                {
                    ConsoleLog.WriteLine(ex.Message);                    
                }
            }
        }
    }

    internal class TcpConnection
    {
        private readonly TcpClient _localServerConnection;
        private readonly EndPoint? _sourceEndpoint;
        private readonly IPEndPoint _remoteEndpoint;
        private readonly TcpClient _forwardClient;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly EndPoint? _serverLocalEndpoint;
        private EndPoint? _forwardLocalEndpoint;
        private long _totalBytesForwarded;
        private long _totalBytesResponded;
        public long LastActivity { get; private set; } = Environment.TickCount64;
        private IRLOBSSwitcher.OBSManager? OBS_SceneManager;
        private ushort _SceneSwitchOverTime;
        private DateTime _lastDataTransferInterval = DateTime.Now;
        private long _bytesTransferredInInterval = 0;
        public long DataRate { get; private set; } = 0;
        private ushort _minimalKbitperSecond;

        public static async Task<TcpConnection> AcceptTcpClientAsync(IRLOBSSwitcher.OBSManager OBS_Manager, TcpListener tcpListener, IPEndPoint remoteEndpoint, ushort SceneSwitchOverTime, ushort minimalKbitperSecond)
        {
            var localServerConnection = await tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
            localServerConnection.NoDelay = true;
            return new TcpConnection(OBS_Manager, localServerConnection, remoteEndpoint, SceneSwitchOverTime, minimalKbitperSecond);
        }

        private TcpConnection(IRLOBSSwitcher.OBSManager OBS_Manager, TcpClient localServerConnection, IPEndPoint remoteEndpoint, ushort SceneSwitchOverTime, ushort minimalKbitperSecond)
        {
            OBS_SceneManager = OBS_Manager;

            _localServerConnection = localServerConnection;
            _remoteEndpoint = remoteEndpoint;
            _SceneSwitchOverTime = SceneSwitchOverTime;
            _minimalKbitperSecond = minimalKbitperSecond;
            _forwardClient = new TcpClient {NoDelay = true};

            _sourceEndpoint = _localServerConnection.Client.RemoteEndPoint;
            _serverLocalEndpoint = _localServerConnection.Client.LocalEndPoint;
        }

        public void Run()
        {
            RunInternal(_cancellationTokenSource.Token);
        }

        public void Stop()
        {
            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (Exception ex)
            {
                ConsoleLog.WriteLine($"An exception occurred while closing TcpConnection : {ex}");
            }
        }

        private void RunInternal(CancellationToken cancellationToken)
        {
            Task.Run(async () =>
            {
                try
                {
                    using (_localServerConnection)
                    using (_forwardClient)
                    {
                        await _forwardClient.ConnectAsync(_remoteEndpoint.Address, _remoteEndpoint.Port, cancellationToken).ConfigureAwait(false);
                        _forwardLocalEndpoint = _forwardClient.Client.LocalEndPoint;

                        ConsoleLog.WriteLine($"Established TCP {_sourceEndpoint} => {_serverLocalEndpoint} => {_forwardLocalEndpoint} => {_remoteEndpoint}");

                        // Tell OBS Manager about the new connection
                        //OBS_SceneManager?.Connect();

                        using (var serverStream = _forwardClient.GetStream())
                        using (var clientStream = _localServerConnection.GetStream())
                        using (cancellationToken.Register(() =>
                        {
                            serverStream.Close();
                            clientStream.Close();
                        }, true))
                        {
                            await Task.WhenAny(
                                CopyToAsync(clientStream, serverStream, 81920, Direction.Forward, cancellationToken),
                                CopyToAsync(serverStream, clientStream, 81920, Direction.Responding, cancellationToken)
                            ).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleLog.WriteLine($"An exception occurred during TCP stream : {ex}");
                }
                finally
                {
                    ConsoleLog.WriteLine($"Closed TCP {_sourceEndpoint} => {_serverLocalEndpoint} => {_forwardLocalEndpoint} => {_remoteEndpoint}. {_totalBytesForwarded} bytes forwarded, {_totalBytesResponded} bytes responded.");
                    // Tell OBS Manager about the lost connection
                    OBS_SceneManager?.Disconnect();
                }
            });
        }

        private async Task CopyToAsync(Stream source, Stream destination, int bufferSize = 81920, Direction direction = Direction.Unknown, CancellationToken cancellationToken = default)
        {
            DateTime started = DateTime.Now;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                while (true)
                {
                    if (started != DateTime.MaxValue)
                    {
                        // only switch to connected after n seconds of "uptime" of this stream
                        // configurable per UDP connection
                        if ((DateTime.Now - started).TotalMilliseconds >= _SceneSwitchOverTime)
                        {
                            // Tell OBS Manager about the connection
                            OBS_SceneManager?.Connect();
                            started = DateTime.MaxValue;
                        }
                    }

                    int bytesRead = await source.ReadAsync(new Memory<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0) break;
                    LastActivity = Environment.TickCount64;
                    await destination.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, bytesRead), cancellationToken).ConfigureAwait(false);

                    switch (direction)
                    {
                        case Direction.Forward:
                            Interlocked.Add(ref _totalBytesForwarded, bytesRead);
                            // add up the transferred bytes...
                            Interlocked.Add(ref _bytesTransferredInInterval, bytesRead);

                            break;
                        case Direction.Responding:
                            Interlocked.Add(ref _totalBytesResponded, bytesRead);
                            break;
                    }

                    #region data rate calculations                
                    // if we gathered data for at least 1 second we calculate and reset the interval...
                    if ((DateTime.Now - _lastDataTransferInterval).TotalSeconds >= 1)
                    {
                        if (_bytesTransferredInInterval > 0)
                        {
                            // do the calculation of data rate
                            // data rate is the amount of transferred data in the intervall divided by the seconds of the intervall
                            DataRate = Convert.ToInt64(Math.Floor(_bytesTransferredInInterval / (DateTime.Now - _lastDataTransferInterval).TotalSeconds) / 128);
                            //ConsoleLog.WriteLine($"Data Rate: {DataRate} kbit/s");
                            _lastDataTransferInterval = DateTime.Now;
                            _bytesTransferredInInterval = 0;
                        }
                        else DataRate = 0;
                    }
                    #endregion

                    #region switch scenes based on data rate
                    // check if we had been connected long enough
                    if (started == DateTime.MaxValue)
                    {
                        if (DataRate >= _minimalKbitperSecond)
                        {
                            // data rate is high enough, if we are in the disconnect scene, switch...
                            if (OBS_SceneManager != null)
                            {
                                if (!OBS_SceneManager.CurrentlyConnected)
                                {
                                    // switch
                                    ConsoleLog.WriteLine($"Data Rate is high enough ({DataRate} kbit/s) - switching to connected scene.");
                                    OBS_SceneManager?.Connect();
                                }
                            }
                            OBS_SceneManager?.Connect();
                        } else
                        {
                            // data rate is not high enough
                            if (OBS_SceneManager != null)
                            {
                                if (OBS_SceneManager.CurrentlyConnected)
                                {
                                    // switch
                                    ConsoleLog.WriteLine($"Data Rate is too low ({DataRate} kbit/s) - switching to disconnected scene.");
                                    OBS_SceneManager?.Disconnect();
                                }
                            }
                        }
                    }
                    #endregion
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    internal enum Direction
    {
        Unknown = 0,
        Forward,
        Responding,
    }
}
