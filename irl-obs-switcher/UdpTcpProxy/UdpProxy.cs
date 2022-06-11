#nullable enable
using ConsoleLogger;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetProxy
{
    internal class UdpProxy : IProxy
    {
        /// <summary>
        /// Milliseconds
        /// </summary>
        public int ConnectionTimeout { get; set; } = (4 * 60 * 1000);
        public IRLOBSSwitcher.OBSManager? OBS_SceneManager;

        public async Task Start(string remoteServerHostNameOrAddress, ushort timeOut, ushort SceneSwitchOverTime, ushort minimalKbitperSecond, ushort remoteServerPort, ushort localPort, IRLOBSSwitcher.OBSManager OBS_Manager, string? localIp = null)
        {
            OBS_SceneManager = OBS_Manager;
            ConnectionTimeout = timeOut; // Connection Timeout in milliseconds in which activity on this port needs to happen or this would be considered a "dead connection" and closed / scene switched
            var connections = new ConcurrentDictionary<IPEndPoint, UdpConnection>();

            // TCP will lookup every time while this is only once.
            var ips = await Dns.GetHostAddressesAsync(remoteServerHostNameOrAddress).ConfigureAwait(false);
            var remoteServerEndPoint = new IPEndPoint(ips[0], remoteServerPort);

            var localServer = new UdpClient(AddressFamily.InterNetworkV6);
            localServer.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            IPAddress localIpAddress = string.IsNullOrEmpty(localIp) ? IPAddress.IPv6Any : IPAddress.Parse(localIp);
            localServer.Client.Bind(new IPEndPoint(localIpAddress, localPort));

            ConsoleLog.WriteLine($"UDP proxy started [{localIpAddress}]:{localPort} -> [{remoteServerHostNameOrAddress}]:{remoteServerPort}");

            var _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                    foreach (var connection in connections.ToArray())
                    {
                        if (connection.Value.LastActivity + (ConnectionTimeout) < Environment.TickCount64)
                        {
                            connections.TryRemove(connection.Key, out UdpConnection? c);
                            connection.Value.Stop();
                        }
                    }
                }
            });

            while (true)
            {
                try
                {
                    var message = await localServer.ReceiveAsync().ConfigureAwait(false);
                    var sourceEndPoint = message.RemoteEndPoint;
                    var client = connections.GetOrAdd(sourceEndPoint,
                        ep =>
                        {
                            var udpConnection = new UdpConnection(OBS_SceneManager, localServer, sourceEndPoint, remoteServerEndPoint, SceneSwitchOverTime, ConnectionTimeout, minimalKbitperSecond);
                            udpConnection.Run();
                            return udpConnection;
                        });
                    await client.SendToServerAsync(message.Buffer).ConfigureAwait(false);

                }
                catch (Exception ex)
                {
                    //ConsoleLog.WriteLine($"an exception occurred on receiving a client datagram: {ex}");
                    ConsoleLog.WriteLine("An exception occurred while trying to forward the data.");
                    OBS_SceneManager?.Disconnect();
                }
            }
        }
    }

    internal class UdpConnection
    {
        private readonly UdpClient _localServer;
        private readonly UdpClient _forwardClient;
        public long LastActivity { get; private set; } = Environment.TickCount64;
        private readonly IPEndPoint _sourceEndpoint;
        private readonly IPEndPoint _remoteEndpoint;
        private readonly EndPoint? _serverLocalEndpoint;
        private EndPoint? _forwardLocalEndpoint;
        private bool _isRunning;
        private long _totalBytesForwarded;
        private long _totalBytesResponded;
        private readonly TaskCompletionSource<bool> _forwardConnectionBindCompleted = new TaskCompletionSource<bool>();
        private IRLOBSSwitcher.OBSManager? OBS_SceneManager;
        private ushort _SceneSwitchOverTime;
        private ushort _minimalKbitperSecond;

        private DateTime _lastDataTransferInterval = DateTime.Now;
        private long _bytesTransferredInInterval = 0;
        public long DataRate { get; private set; } = 0;

        public UdpConnection(IRLOBSSwitcher.OBSManager OBS_Manager, UdpClient localServer, IPEndPoint sourceEndpoint, IPEndPoint remoteEndpoint, ushort SceneSwitchOverTime, int ConnectionTimeout, ushort minimalKbitperSecond)
        {
            OBS_SceneManager = OBS_Manager;
            _localServer = localServer;
            _serverLocalEndpoint = _localServer.Client.LocalEndPoint;

            _isRunning = true;
            _remoteEndpoint = remoteEndpoint;
            _sourceEndpoint = sourceEndpoint;
            _SceneSwitchOverTime = SceneSwitchOverTime;
            _minimalKbitperSecond = minimalKbitperSecond;
            _forwardClient = new UdpClient(AddressFamily.InterNetworkV6);
            
            // defaults 1000
            _forwardClient.Client.SendTimeout = ConnectionTimeout;
            _forwardClient.Client.ReceiveTimeout = ConnectionTimeout;
            
            _forwardClient.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        }

        public async Task SendToServerAsync(byte[] message)
        {
            LastActivity = Environment.TickCount64;

            await _forwardConnectionBindCompleted.Task.ConfigureAwait(false);
            var sent = await _forwardClient.SendAsync(message, message.Length, _remoteEndpoint).ConfigureAwait(false);
            Interlocked.Add(ref _totalBytesForwarded, sent);
            // add up the transferred bytes...
            Interlocked.Add(ref _bytesTransferredInInterval, sent);

        }

        public void Run()
        {
            Task.Run(async () =>
            {
                DateTime started = DateTime.Now;

                using (_forwardClient)
                {
                    _forwardClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                    _forwardLocalEndpoint = _forwardClient.Client.LocalEndPoint;
                    _forwardConnectionBindCompleted.SetResult(true);
                    //ConsoleLog.WriteLine($"Established UDP {_sourceEndpoint} => {_serverLocalEndpoint} => {_forwardLocalEndpoint} => {_remoteEndpoint}");
                    ConsoleLog.WriteLine($"Established UDP {_sourceEndpoint} => {_remoteEndpoint} - will switch scene after {_SceneSwitchOverTime}ms");
                    //ConsoleLog.WriteLine($"Will switch scene after {_SceneSwitchOverTime}");

                    while (_isRunning)
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

                        try
                        {
                            var result = await _forwardClient.ReceiveAsync().ConfigureAwait(false);
                            LastActivity = Environment.TickCount64;
                            var sent = await _localServer.SendAsync(result.Buffer, result.Buffer.Length, _sourceEndpoint).ConfigureAwait(false);

                            #region data rate calculations                
                            // if we gathered data for at least 1 second we calculate and reset the interval...
                            if ((DateTime.Now - _lastDataTransferInterval).TotalSeconds >= 1)
                            {
                                if (_bytesTransferredInInterval > 0)
                                {
                                    // do the calculation of data rate
                                    // data rate is the amount of transferred data in the intervall divided by the seconds of the intervall
                                    DataRate = Convert.ToInt64(Math.Floor(_bytesTransferredInInterval / (DateTime.Now - _lastDataTransferInterval).TotalSeconds)/128);
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
                                }
                                else
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

                            Interlocked.Add(ref _totalBytesResponded, sent);
                        }
                        catch (Exception ex)
                        {
                            if (_isRunning)
                            {
                                //ConsoleLog.WriteLine($"An exception occurred while receiving a server datagram : {ex}");
                                //ConsoleLog.WriteLine("An exception occurred while receiving a server datagram");
                            }
                        }
                    }
                }
            });
        }

        public void Stop()
        {
            try
            {
                //ConsoleLog.WriteLine($"Closed UDP {_sourceEndpoint} => {_serverLocalEndpoint} => {_forwardLocalEndpoint} => {_remoteEndpoint}. {_totalBytesForwarded} bytes forwarded, {_totalBytesResponded} bytes responded.");
                ConsoleLog.WriteLine($"Closed UDP => {_totalBytesForwarded} bytes forwarded, {_totalBytesResponded} bytes responded.");
                _forwardClient.Dispose();
                
                // Tell OBS Manager about the lost connection
                OBS_SceneManager?.Disconnect();

                _isRunning = false;
                _forwardClient.Close();
            }
            catch (Exception ex)
            {
                //ConsoleLog.WriteLine($"An exception occurred while closing UdpConnection : {ex}");
                ConsoleLog.WriteLine("An exception occurred while closing the UDP connection");
            }
        }
    }
}
