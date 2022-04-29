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
        public static IEnumerable<Task> ProxyFromConfig(ProxyConnection proxyConfig)
        {
            var forwardPort = proxyConfig.forwardPort;
            var localPort = proxyConfig.localPort;
            var forwardIp = proxyConfig.forwardIp;
            var localIp = proxyConfig.localIp;
            var protocol = proxyConfig.protocol;
            var timeOut = proxyConfig.timeOut;
            var OBSsceneOnConnect = proxyConfig.OBSsceneOnConnect;
            var OBSsceneOnDisconnect = proxyConfig.OBSSceneOnDisconnect;
            var SemaphoreFileWhenConnected = proxyConfig.SemaphoreFileWhenConnected;


            var proxyName = forwardPort.ToString() + "->" + forwardIp + ":" + forwardPort.ToString();

            try
            {
                if (timeOut == null)
                    timeOut = 1;
                if (forwardIp == null)
                {
                    throw new Exception("forwardIp is null");
                }
                if (!forwardPort.HasValue)
                {
                    throw new Exception("forwardPort is null");
                }
                if (!localPort.HasValue)
                {
                    throw new Exception("localPort is null");
                }
                if (protocol != "udp" && protocol != "tcp" && protocol != "any")
                {
                    throw new Exception($"protocol is not supported {protocol}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start {proxyName} : {ex.Message}");
                throw;
            }

            bool protocolHandled = false;
            if (protocol == "udp" || protocol == "srt" || protocol == "any")
            {
                protocolHandled = true;
                Task task;
                try
                {
                    var proxy = new UdpProxy();
                    task = proxy.Start(forwardIp, timeOut.Value, forwardPort.Value, localPort.Value, localIp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to start {proxyName} : {ex.Message}");
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
                    task = proxy.Start(forwardIp, timeOut.Value, forwardPort.Value, localPort.Value, localIp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to start {proxyName} : {ex.Message}");
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
