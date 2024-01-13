using System;

namespace fs2ff.Converters
{
    public static class DistanceConverter
    {
        private const double _meters = 1 / 1852;
        private const uint _nMiles = 1852;
        public static double MetersToNm(this double meters)
        {
            return meters * _meters;
        }
        public static uint MetersToNm(this uint meters)
        {
            return Convert.ToUInt32(meters * _meters);
        }

        public static uint NmToMeters(this uint nauticalMiles)
        {
            return Convert.ToUInt32(nauticalMiles * _nMiles);
        }
    }
}
