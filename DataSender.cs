using fs2ff.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace fs2ff
{
    /// <summary>
    /// Transmits the data over ethernet to the EFB
    /// </summary>
    public class DataSender : IDisposable
    {
        private const int FlightSimPort = 49002;
        private const int Gdl90Port = 4000;
        private const string SimId = "MSFS";

        private List<IPEndPoint> _endPoints = new List<IPEndPoint>();
        private Socket? _socket;

        /// <summary>
        /// Binds to a socket for transmission
        /// </summary>
        /// <param name="ip"></param>
        public void Connect(IDictionary<string, IPAddress> ips)
        {
            Disconnect();
            int port = ViewModelLocator.Main.DataGdl90Enabled ? Gdl90Port : FlightSimPort;

            foreach (var ip in ips)
            {
                var endPoint = new IPEndPoint(ip.Value, port);
                _endPoints.Add(endPoint);
            }

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }

        public void Disconnect() => _socket?.Dispose();

        public void Dispose() => _socket?.Dispose();

        /// <summary>
        /// Converts and sends an Attitude update packet
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        public async Task Send(Attitude a)
        {
            if (ViewModelLocator.Main.DataGdl90Enabled)
            {
                if (ViewModelLocator.Main.DataStratuxEnabled)
                {
                    var ahrs = new Gdl90Ahrs(a);
                    var data = ahrs.ToGdl90Message();
                    await Send(data).ConfigureAwait(false);
                }
                else
                {
                    var ffAhrs = new Gdl90FfmAhrs(a);
                    var data = ffAhrs.ToGdl90Message();
                    await Send(data).ConfigureAwait(false);
                }
            }
            else
            {
                // Using a slip value between -127 and +127. .005 converts GP to be similar to the in game G1000 slip indicator
                var slipDeg = a.SkidSlip * -0.005;
                var data = string.Format(CultureInfo.InvariantCulture,
                    $"XATT{SimId},{a.TrueHeading:0.##},{-a.Pitch:0.##},{-a.Bank:0.##},,,{a.TurnRate:0.##},,,,{slipDeg:0.###},,");

                await Send(data).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Converts and sends a Position packet
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public async Task Send(Position p)
        {
            if (!ViewModelLocator.Main.DataGdl90Enabled)
            {

                var data = string.Format(CultureInfo.InvariantCulture,
                "XGPS{0},{1:0.#####},{2:0.#####},{3:0.##},{4:0.###},{5:0.##}",
                SimId, p.Pd.Longitude, p.Pd.Latitude, p.Pd.AltitudeMeters, p.Pd.GroundTrack, p.Pd.GroundSpeedMps);

                await Send(data).ConfigureAwait(false);
            }
            else
            {
                Gdl90GeoAltitude geoAlt = new Gdl90GeoAltitude(p);
                var data = geoAlt.ToGdl90Message();
                await Send(data).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Converts and sends a traffic packet. This can also be an Ownership report to the EFB
        /// </summary>
        /// <param name="t"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task Send(Traffic t)
        {
            if (!t.IsValid())
            {
                return;
            }

            if (ViewModelLocator.Main.DataGdl90Enabled)
            {
                var traffic = new Gdl90Traffic(t);
                var data = traffic.ToGdl90Message();
                await Send(data).ConfigureAwait(false);
            }
            else
            {
                var data = string.Format(CultureInfo.InvariantCulture,
                    "XTRAFFIC{0},{1},{2:0.#####},{3:0.#####},{4:0.#},{5:0.#},{6},{7:0.###},{8:0.#},{9}",
                    SimId, t.Iaco, t.Td.Latitude, t.Td.Longitude, t.Td.Altitude, t.Td.VerticalSpeed, t.Td.OnGround ? 0 : 1,
                    t.Td.TrueHeading, t.Td.GroundVelocity, TryGetFlightNumber(t) ?? t.Td.TailNumber);

                await Send(data).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Encodes and sends a string
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private async Task Send(string data)
        {
            if (_socket != null)
            {
                var byteData = new ArraySegment<byte>(Encoding.ASCII.GetBytes(data));
                foreach (var endPoint in _endPoints)
                {
                    await _socket
                        .SendToAsync(byteData, SocketFlags.None, endPoint)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Sends the give byte array
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task Send(byte[] data)
        {
            if (_socket != null)
            {
                foreach (var endPoint in _endPoints)
                {
                    await _socket
                        .SendToAsync(data, SocketFlags.None, endPoint)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// If the plane has an airline ID then use that has the flight
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private static string? TryGetFlightNumber(Traffic t) =>
            !string.IsNullOrEmpty(t.Td.Airline) && !string.IsNullOrEmpty(t.Td.FlightNumber)
                ? $"{t.Td.Airline} {t.Td.FlightNumber}"
                : null;
    }
}
