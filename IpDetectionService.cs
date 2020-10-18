using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Microsoft.Extensions.Hosting;

namespace fs2ff
{
    public class IpDetectionService : IHostedService
    {
        private const int Port = 63093;

        public event Action<IPAddress>? NewIpDetected;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await Task.Run(async () =>
            {
                try
                {
                    using var udpClient = new UdpClient(Port, IPAddress.Any.AddressFamily);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var result = await udpClient.ReceiveAsync();
                        var text = Encoding.ASCII.GetString(result.Buffer);

                        if (IsForeFlightGdl90(text))
                        {
                            NewIpDetected?.Invoke(result.RemoteEndPoint.Address);
                        }
                    }
                }
                catch (SocketException e)
                {
                    Console.Error.WriteLine(e);
                }
            }, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private static bool IsForeFlightGdl90(string text) =>
            JsonObject.TryParse(text, out var json) &&
            json.TryGetValue("App", out var app) &&
            app.Stringify() == "\"ForeFlight\"";
    }
}