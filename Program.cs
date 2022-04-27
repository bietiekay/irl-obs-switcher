namespace IRLOBSSwitcher
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                var configJson = System.IO.File.ReadAllText("config.json");
                Dictionary<string, ProxyConfig>? configs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProxyConfig>>(configJson);
                if (configs == null)
                {
                    throw new Exception("configs is null");
                }

                var tasks = configs.SelectMany(c => ProxyFromConfig(c.Key, c.Value));
                Task.WhenAll(tasks).Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred : {ex}");
            }
        }
        #region Configuration
        private static IEnumerable<Task> ProxyFromConfig(string proxyName, ProxyConfig proxyConfig)
        {
            var forwardPort = proxyConfig.forwardPort;
            var localPort = proxyConfig.localPort;
            var forwardIp = proxyConfig.forwardIp;
            var localIp = proxyConfig.localIp;
            var protocol = proxyConfig.protocol;
            var timeOut = proxyConfig.timeOut;
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
            if (protocol == "udp" || protocol == "any")
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

            if (protocol == "tcp" || protocol == "any")
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
        #endregion
    }
}