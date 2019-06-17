```RUN.cmd
dotnet WebProxyOverIoTHub.ClientSideProxy.dll  ^
--IoTHubServiceConnectionString=HostName=<HOST>.azure-devices.net;SharedAccessKeyName=<KEY_NAME>;SharedAccessKey=<KEY> ^
--TargetDeviceId=<DEVICE_ID> ^
--LocalDeviceStreamPort=<PORT_DEVICE_STREAM> ^
--LocalInternalWebProxyPort=<PORT_WEB_PROXY> ^
--UpstreamWebProxy=<UPSTREAM_WEB_PROXY> ^
--StreamName=<YOUR_CLIENT>
```