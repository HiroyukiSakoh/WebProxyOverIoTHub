using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace WebProxyOverIoTHub.ServerSideProxy
{
    public class InternalWebProxyServer
    {
        private ProxyServer proxyServer;
        private ExplicitProxyEndPoint endPoint;

        public InternalWebProxyServer(int localPort)
        {
            proxyServer = new ProxyServer(false, false, false);
            proxyServer.BeforeRequest += OnRequest;
            proxyServer.BeforeResponse += OnResponse;
            HackTitaniumWebProxy();

            proxyServer.ServerCertificateValidationCallback += OnCertificateValidation;
            proxyServer.ClientCertificateSelectionCallback += OnCertificateSelection;
            endPoint = new ExplicitProxyEndPoint(IPAddress.Any, localPort, false);
            endPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequest;
            proxyServer.AddEndPoint(endPoint);
        }

        public void Start()
        {
            proxyServer.Start();
        }

        public void Stop()
        {
            if (proxyServer != null)
            {
                endPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
                proxyServer.BeforeRequest -= OnRequest;
                proxyServer.BeforeResponse -= OnResponse;
                proxyServer.ServerCertificateValidationCallback -= OnCertificateValidation;
                proxyServer.ClientCertificateSelectionCallback -= OnCertificateSelection;
                proxyServer.Stop();
            }
        }

        #region ProxyServer CallBack Methods
#pragma warning disable CS1998
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private async Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e)
        {
            Console.WriteLine($"OnBeforeTunnelConnectRequest:{e.HttpClient.Request.RequestUri.Host}");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        public async Task OnRequest(object sender, SessionEventArgs e)
        {
            Console.WriteLine($"OnRequest:{e.HttpClient.Request.Url}");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        public async Task OnResponse(object sender, SessionEventArgs e)
        {
            Console.WriteLine($"OnResponse:{e.HttpClient.Response.StatusCode}");
        }
#pragma warning restore CS1998

        /// <summary>
        ///  Allows overriding default certificate validation logic
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        public Task OnCertificateValidation(object sender, CertificateValidationEventArgs e)
        {
            e.IsValid = true;
            return Task.FromResult(0);
        }

        /// <summary>
        /// Allows overriding default client certificate selection logic during mutual authentication
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        public Task OnCertificateSelection(object sender, CertificateSelectionEventArgs e)
        {
            return Task.FromResult(0);
        }
        #endregion


        /// <summary>
        /// システムプロキシ関連のレジストリアクセスを抑止する為のハック
        /// </summary>
        private void HackTitaniumWebProxy()
        {
            //https://github.com/justcoding121/Titanium-Web-Proxy/issues/478
            var type = proxyServer.GetType();
            var property = proxyServer.GetType().GetProperty("systemProxySettingsManager", BindingFlags.NonPublic | BindingFlags.Instance);
            var backingField = type
              .GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
              .FirstOrDefault(field =>
                field.Attributes.HasFlag(FieldAttributes.Private) &&
                field.Attributes.HasFlag(FieldAttributes.InitOnly) &&
                field.CustomAttributes.Any(attr => attr.AttributeType == typeof(CompilerGeneratedAttribute)) &&
                (field.DeclaringType == property.DeclaringType) &&
                field.FieldType.IsAssignableFrom(property.PropertyType) &&
                field.Name.StartsWith("<" + property.Name + ">")
              );
            backingField.SetValue(proxyServer, null);
        }
    }
}
