namespace NetProxy
{
    internal interface IProxy
    {
        Task Start(string remoteServerHostNameOrAddress, ushort timeOut, ushort remoteServerPort, ushort localPort, string? localIp = null);
    }
}