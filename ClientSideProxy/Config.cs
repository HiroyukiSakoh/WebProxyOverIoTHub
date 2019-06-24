namespace WebProxyOverIoTHub.ClientSideProxy
{
    public class Config
    {
        public string IoTHubServiceConnectionString { get; set; }
        public string TargetDeviceId { get; set; }
        public int LocalDeviceStreamPort { get; set; }
        public int LocalInternalWebProxyPort { get; set; }
        public string UpstreamWebProxy { get; set; }
        public string StreamName { get; set; }
    }
}