// ReSharper disable FieldCanBeMadeReadOnly.Global

using System;
using System.Runtime.InteropServices;

namespace fs2ff.Models
{
    /// <summary>
    /// Wraps the TrafficData for extensibility (future work)
    /// </summary>
    public class Traffic
    {
        public TrafficData Td { get; set; }
        public DateTime LastUpdate { get; set; }
        public uint Iaco {  get; set; }
        public Traffic(TrafficData trafficData, uint iaco)
        {
            Td = trafficData;
            LastUpdate = DateTime.UtcNow;
            Iaco = iaco;
        }

        public Traffic(TrafficData trafficData, uint iaco, DateTime lastUpdate)
        {
            Td = trafficData;
            LastUpdate = lastUpdate;
            Iaco = iaco;
        }
    }

    /// <summary>
    /// Traffic structure passed into SimConnect
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct TrafficData
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double PressureAlt;
        public double VerticalSpeed;
        public bool OnGround;
        public double TrueHeading;
        public double GroundVelocity;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string TailNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string Airline;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string FlightNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Category;

        public double MaxGrossWeight;
        public double AirspeedIndicated;
        public double AirspeedTrue;
        public uint TransponderCode;
        public TransponderState TransponderState;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string AtcModel;

        public double MaxMach;
        public double MaxGforceSeen;
        public bool LightBeaconOn;
        public double AltAboveGroundCG;
    }

    /// <summary>
    /// Traffic helper methods
    /// </summary>
    public static class TrafficExtensions
    {
        public const uint MinDistanceNm = 4;
        public const uint MinAltDistanceFeet = 2000;
        public const double ValidSecond = 30;
        private const double _radiusEarth = 6371008.8;

        /// <summary>
        /// Is the traffic data valid
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static bool IsValid(this Traffic t)
        {
            var span = DateTime.UtcNow - t.LastUpdate;
            return t.Td.Latitude != 0 && t.Td.Longitude != 0 &&  span.TotalSeconds < ValidSecond;
        }

        /// <summary>
        /// Converts the provide angle to a radian
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static double ToRadians(this double angle)
        {
            return angle * Math.PI / 180;
        }

        /// <summary>
        /// Determines distance between two traffic objects in Meters
        /// </summary>
        /// <param name="self">Aircraft to measure from</param>
        /// <param name="target">Aircraft to get the distance to</param>
        /// <param name="useAltitude">Adds the altitude difference</param>
        /// <returns></returns>
        public static (double Horizontal, double AltitudeDelta) DistanceMeters(this TrafficData self, TrafficData target)
        {
            var selfLatRad = self.Latitude.ToRadians();
            var selfLongRad = self.Longitude.ToRadians();
            var targetLatRad = target.Latitude.ToRadians();
            var targetLongRad = target.Longitude.ToRadians();

            double dist = Math.Acos(Math.Sin(selfLatRad) * Math.Sin(targetLatRad) + Math.Cos(selfLatRad) * Math.Cos(targetLatRad) * Math.Cos(targetLongRad - selfLongRad)) * _radiusEarth;
            double altDelta = Math.Abs(self.Altitude.FeetToMeters() - target.Altitude.FeetToMeters());

            return (dist, altDelta);
        }

        /// <summary>
        /// Is the traffic close enough to worry about.
        /// </summary>
        /// <param name="self">Self location</param>
        /// <param name="target">Target location</param>
        /// <returns>true if close enough to alert</returns>
        public static bool IsAlertable(this Traffic self, Traffic target)
        {
            bool alert = self.IsValid() && target.IsValid();

            if (alert)
            {
                var distances = self.Td.DistanceMeters(target.Td);
                alert = distances.Horizontal.MetersToNm() < MinDistanceNm && distances.AltitudeDelta.MetersToFeet() < MinAltDistanceFeet;
            }

            return alert;
        }
    }

    /// <summary>
    /// SimConnect Transponder state info.
    /// </summary>
    public enum TransponderState : int
    {
        Off,
        Standby,
        Test,
        On,
        Alt,
        Ground
    }
}
