using ConsoleLogger;

namespace IRLOBSSwitcher
{
    internal static class Program
    {
        private const string ConfigurationFilename = "config.json";

        private static void Main(string[] args)
        {
            ConsoleLog.WriteLine("IRL OBS Switcher and Proxy");
            ConsoleLog.WriteLine("(C) Daniel Kirstenpfad 2022 - https://github.com/bietiekay/irl-obs-switcher");

            if (!File.Exists(ConfigurationFilename))
            {
                ConsoleLog.WriteLine(ConfigurationFilename+" not found. Aborting.");
                return;
            }

            try
            {
                // Read the configuration file...
                var configuration = ConfigurationManager.ReadConfiguration(ConfigurationFilename);
                if (configuration == null)
                {
                    throw new Exception("config is wrong");
                }

                // instantiate the OBS WebSocket Connection Object...
                if (configuration.OBSWebSocketConnection != null)
                {
                    OBSManager OBS_SceneManager = new OBSManager(configuration.OBSWebSocketConnection);

                    if (configuration.ProxyConnections != null)
                    {
                        var tasks = configuration.ProxyConnections.SelectMany(c => ProxyManager.ProxyFromConfig(OBS_SceneManager,c));
                        Task.WhenAll(tasks).Wait();
                    }
                }
                else
                {
                    throw new Exception("OBS WebSocket configuration not correct or not found.");
                }

            }
            catch (Exception ex)
            {
                ConsoleLog.WriteLine($"An error occurred : {ex}");
            }
        }     
    }
}