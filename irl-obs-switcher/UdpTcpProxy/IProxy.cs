namespace NetProxy
{
    internal interface IProxy
    {
        Task Start(string remoteServerHostNameOrAddress, ushort timeOut, ushort SceneSwitchOverTime, ushort minimalKbitperSecond, ushort remoteServerPort, ushort localPort, IRLOBSSwitcher.OBSManager OBS_Manager, string? localIp = null);
    }
}