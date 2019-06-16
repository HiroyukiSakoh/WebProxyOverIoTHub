namespace WebProxyOverIoTHub.ClientSideProxy
{
    public class LocalSettings
    {
        public string IoTHubServiceConnectionString { get; set; }
        public string TargetDeviceId { get; set; }
        public int LocalWebProxyPort { get; set; }
        public string UpstreamWebProxy { get; set; }
    }
}