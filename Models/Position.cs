// ReSharper disable FieldCanBeMadeReadOnly.Global

using System;
using System.Runtime.InteropServices;

namespace fs2ff.Models
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PositionData
    {
        public double Latitude;
        public double Longitude;
        public double AltitudeFeet;
        public double AltitudeMeters;
        public double GroundTrack;
        public double GroundSpeedMps;
    }

    public class Position
    {
        public PositionData Pd { get; set; }
        public DateTime LastUpdate { get; set; }
        public uint Iaco {  get; set; }

        public Position(PositionData pd, uint iaco)
        {
            Pd = pd;
            Iaco = iaco;
            LastUpdate = DateTime.UtcNow;
        }

        public Position(PositionData pd, uint iaco, DateTime lastUpdate)
        {
            Pd = pd;
            Iaco = iaco;
            LastUpdate = lastUpdate;
        }

    }

    public static class PositionExtensions
    {
        public const double ValidSecond = 10;

        public static bool IsValid(this Position pos)
        {
            var span = DateTime.UtcNow - pos.LastUpdate;
            return pos.Pd.Latitude != 0 && pos.Pd.Longitude != 0 && span.TotalSeconds < ValidSecond;
        }

        public static Traffic ToTraffic(this Position pos)
        {
            var td = new TrafficData
            {
                Latitude = pos.Pd.Latitude,
                Longitude = pos.Pd.Longitude,
                Altitude = pos.Pd.AltitudeFeet,
                AirspeedTrue = pos.Pd.GroundSpeedMps.MetersToKnots(),
            };

            return new Traffic(td, pos.Iaco, pos.LastUpdate);
        }
    }
}
