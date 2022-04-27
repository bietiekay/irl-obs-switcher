namespace IRLOBSSwitcher
{
    public class ProxyConfig
    {
        public string? protocol { get; set; }
        public ushort? localPort { get; set; }
        public string? localIp { get; set; }
        public string? forwardIp { get; set; }
        public ushort? forwardPort { get; set; }
        public ushort? timeOut { get; set; }
    }
}