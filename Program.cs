namespace IRLOBSSwitcher
{
    internal static class Program
    {
        private const string ConfigurationFilename = "config.json";

        private static void Main(string[] args)
        {
            Console.WriteLine("IRL OBS Switcher and Proxy");
            Console.WriteLine("(C) Daniel Kirstenpfad 2022 - https://github.com/bietiekay/irl-obs-switcher");
            if (!File.Exists(ConfigurationFilename))
            {
                Console.WriteLine(ConfigurationFilename+" not found. Aborting.");
                return;
            }

            try
            {
                var configuration = ConfigurationManager.ReadConfiguration(ConfigurationFilename);
                if (configuration == null)
                {
                    throw new Exception("config is wrong");
                }
                if (configuration.ProxyConnections != null)
                {
                    var tasks = configuration.ProxyConnections.SelectMany(c => ProxyManager.ProxyFromConfig(c));
                    Task.WhenAll(tasks).Wait();
                }                                   
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred : {ex}");
            }
        }     
    }
}