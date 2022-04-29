using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRLOBSSwitcher
{ 
    public class ProxyConnection
    {
        public string? localIp { get; set; }
        public ushort? localPort { get; set; }
        public string? protocol { get; set; }
        public string? forwardIp { get; set; }
        public ushort? forwardPort { get; set; }
        public ushort? timeOut { get; set; }
        public string? OBSsceneOnConnect { get; set; }
        public string? OBSSceneOnDisconnect { get; set; }
        public string? SemaphoreFileWhenConnected { get; set; }
    }

    public class ConfigurationRoot
    {
        public List<ProxyConnection>? ProxyConnections { get; set; }
    }

    public static class ConfigurationManager
    {
        public static ConfigurationRoot? ReadConfiguration(String ConfigurationFileName)
        {
            if (File.Exists(ConfigurationFileName))
            {
                return JsonConvert.DeserializeObject<ConfigurationRoot>(File.ReadAllText(ConfigurationFileName)) ?? new ConfigurationRoot();
            }
            else 
                return null;
        }
    }
}
