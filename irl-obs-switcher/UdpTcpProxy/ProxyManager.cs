using ConsoleLogger;
using NetProxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRLOBSSwitcher
{
    public static class ProxyManager
    {
        public static IEnumerable<Task> ProxyFromConfig(OBSManager OBS_Manager, ProxyConnection proxyConfig)
        {
            var forwardPort = proxyConfig.forwardPort;
            var localPort = proxyConfig.localPort;
            var forwardHost = proxyConfig.forwardHost;
            var localIp = proxyConfig.localIp;
            var protocol = proxyConfig.protocol;
            var timeOut = proxyConfig.timeOut;
            var sceneSwitchOverTime = proxyConfig.switchToConnectedTime;
            var OBS_SceneManager = OBS_Manager;

            var proxyName = forwardPort.ToString() + "->" + forwardHost + ":" + forwardPort.ToString();
            #region error check
            try
            {
                if (timeOut == null)
                    timeOut = 1;
                if (forwardHost == null)
                {
                    throw new Exception("forwardHost is null");
                }
                if (!forwardPort.HasValue)
                {
                    throw new Exception("forwardPort is null");
                }
                if (!localPort.HasValue)
                {
                    throw new Exception("localPort is null");
                }
                if (protocol != null)
                    protocol = protocol.ToLower();

                if (protocol != "udp" && protocol != "tcp" && protocol != "any" && protocol != "srt" && protocol != "rtmp")
                {
                    throw new Exception($"protocol is not supported {protocol}");
                }
            }
            catch (Exception ex)
            {
                ConsoleLog.WriteLine($"Failed to start {proxyName} : {ex.Message}");
                throw;
            }
            #endregion

            bool protocolHandled = false;
            if (protocol == "udp" || protocol == "srt" || protocol == "any")
            {
                protocolHandled = true;
                Task task;
                try
                {
                    var proxy = new UdpProxy();
                    task = proxy.Start(forwardHost, timeOut.Value, sceneSwitchOverTime.Value, forwardPort.Value, localPort.Value, OBS_SceneManager, localIp);
                }
                catch (Exception ex)
                {
                    ConsoleLog.WriteLine($"Failed to start {proxyName} : {ex.Message}");
                    throw;
                }

                yield return task;
            }

            if (protocol == "tcp" || protocol == "rtmp" || protocol == "any")
            {
                protocolHandled = true;
                Task task;
                try
                {
                    var proxy = new TcpProxy();
                    task = proxy.Start(forwardHost, timeOut.Value, sceneSwitchOverTime.Value, forwardPort.Value, localPort.Value, OBS_SceneManager, localIp);
                }
                catch (Exception ex)
                {
                    ConsoleLog.WriteLine($"Failed to start {proxyName} : {ex.Message}");
                    throw;
                }

                yield return task;
            }

            if (!protocolHandled)
            {
                throw new InvalidOperationException($"protocol not supported {protocol}");
            }
        }
    }
}
