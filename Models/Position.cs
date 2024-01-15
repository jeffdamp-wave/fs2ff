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

    /// <summary>
    /// Wraps the Position data for better tracking (future work)
    /// </summary>
    public class Position
    {
        public const int Priority = 1; // If I end up making a packet priority queue.
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

    /// <summary>
    /// Position helper methods
    /// </summary>
    public static class PositionExtensions
    {
        public const double ValidSecond = 10;

        /// <summary>
        /// Is the position data valid
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public static bool IsValid(this Position pos)
        {
            var span = DateTime.UtcNow - pos.LastUpdate;
            return pos.Pd.Latitude != 0 && pos.Pd.Longitude != 0 && span.TotalSeconds < ValidSecond;
        }
    }
}
