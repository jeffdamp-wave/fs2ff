using fs2ff.SimConnect;
using System;
using System.Text;

namespace fs2ff.Models
{
    public class Gdl90Traffic : Gdl90Base
    {
        public static uint SelfIaco = (uint)(new Random().Next(0xA00000, 0xAFFFFF));
        /// <summary>
        /// Used for both ownership report and td report 28 bytes
        /// As Ownership message spec'ed at 5hz
        /// As Traffic report spec'ed 1hz
        /// </summary>
        /// <param name="td">Traffic object to convert</param>
        /// <param name="iaco">1 for owner otherwise iaco</param>
        public Gdl90Traffic(Traffic traffic, uint iaco) : base(28)
        {
            var td = traffic.Td;
            var isOwner = iaco == SimConnectAdapter.OBJECT_ID_USER_RESULT;

            // If current Traffic is not ourself then get the owner td for altering later
            var owner = isOwner ? traffic : ViewModelLocator.Main.OwnerInfo;
            // 0x0A (10) Ownership message
            // 0x14 (20) Standard td
            if (isOwner)
            {
                Msg[0] = 0xA;
                iaco = SelfIaco;
            }
            else
            {
                Msg[0] = 0x14;
            }

            var code = BitConverter.GetBytes(iaco);
            if (code[2] != 0xF0 && code[2] != 0x00)
            {
                Msg[1] = 0x00; // ADS-B Out with ICAO
                Msg[2] = code[2]; // Mode S address.
                Msg[3] = code[1]; // Mode S address.
                Msg[4] = code[0]; // Mode S address.
            }
            else
            {
                Msg[1] = 0x01; // ADS-B Out with self-assigned code
                // Reserved dummy code.
                Msg[2] = 0xF0;
                Msg[3] = 0x00;
                Msg[4] = 0x00;
            }

            // Convert double lat to 3 bytes
            var tmp = td.Latitude.MakeLatLng();
            Array.Copy(tmp, 0, Msg, 5, tmp.Length);

            // Convert double longitude to 3 bytes
            tmp = td.Longitude.MakeLatLng();
            Array.Copy(tmp, 0, Msg, 8, tmp.Length);

            // Altitude. LSB = 25ft starting at -1000 (0x00)
            var altf = (td.Altitude + 1000) / 25;

            // Range -1000 -> 101,350 feet so doesn't work with the space shuttle
            ushort alt = (ushort)((altf < -1000 || altf > 101350) ? 0x0FFF : Convert.ToUInt16(altf));
            Msg[11] = (byte)((alt & 0xFF0) >> 4);
            Msg[12] = (byte)((alt & 0x00F) << 4);

            Msg[12] |= 0x01;

            // MSFS has a desire to show stationary planes on not on ground
            // VATSIM likes to have some jitter and not put the plane on the ground
            if (!td.OnGround && td.AltAboveGroundCG > 10 && (td.GroundVelocity != 0 || td.VerticalSpeed != 0))
            {
                Msg[12] = (byte)(Msg[12] | 1 << 3);

                if (!isOwner && owner.IsAlertable(traffic))
                {
                    // Set the alert bit. I haven't found an EFB that uses this
                    Msg[1] |= 0x10;
                }
            }

            // GPS NIC and NACp
            // Both values range from 0 (unknown) to 0XB (11) lower than 4 means degraded target.
            Msg[13] = 0xB0 | (0x0B & 0x0F);

            // Ground speed 12 bits LSB = 1kts
            var knots = Convert.ToUInt16(td.GroundVelocity);
            Msg[14] = (byte)((knots & 0x0FF0) >> 4);
            Msg[15] = (byte)((knots & 0x000F) << 4);

            // Vertical Speed 12 bits LSB = 64ft/m
            var verticalVelocity = Convert.ToInt16(td.VerticalSpeed.RoundBy(64) / 64);
            Msg[15] |= (byte)((verticalVelocity & 0x0F00) >> 8);
            Msg[16] = (byte)(verticalVelocity & 0x00FF);

            // Truncate Heading to 359 to not overflow the convert below
            var trk = Math.Min(td.TrueHeading, 359);
            // Heading is 360/256 to fit into 1 byte
            trk /= Gdl90Util.TRACK_RESOLUTION;
            Msg[17] = Convert.ToByte(trk);

            if (td.MaxMach > 5)
            {
                // Space or trans-atmospheric vehicle (Dark Star)
                Msg[18] = 15;
            }
            else if (td.MaxMach > 1.1 || td.MaxGforceSeen > 5)
            {
                // Highly Maneuverable > 5G acceleration and high speed (F18 and other High G aircraft)
                Msg[18] = 6;
            }
            else if (td.Category == "Helicopter")
            {
                Msg[18] = 0x7;
            }
            else if (td.Category == "Airplane")
            {
                if (td.MaxGrossWeight < 15500)
                {
                    // Light
                    Msg[18] = 1;
                }
                else if (td.MaxGrossWeight < 75000)
                {
                    // Small
                    Msg[18] = 2;
                }
                else if (td.MaxGrossWeight < 300000)
                {
                    // Large (B737,B777, A350, etc.)
                    Msg[18] = 3;
                }
                else
                {
                    // Heavy (B747,A380)
                    Msg[18] = 5;
                }

                // TODO: Add more aircraft types (glider, High Vortex, etc.)
                // Could use the AtcModel to do a string lookup for a given set of aircraft etc.
            }
            else
            {
                Msg[18] = 0;
            }

            var tail = "None";
            if (!string.IsNullOrEmpty(td.TailNumber))
            {
                tail = td.TailNumber.Trim();
            }

            // Max length 8 bytes
            var tailBytes = Encoding.ASCII.GetBytes(tail);
            for (int i = 0; i < tailBytes.Length && i < 8; i++)
            {
                var c = tailBytes[i];
                // Remove special characters See p.24, FAA ref.
                if (c != 0x20 && !((c >= 48) && (c <= 57)) && !((c >= 65) && (c <= 90)) && c != 'e' && c != 'u' && c != 'a' && c != 'r' && c != 't')
                {
                    c = 0x20;
                }

                Msg[19 + i] = c;
            }

            // Priority status 0-6, 0 is normal
            // I haven't found an EFB that uses this
            switch (td.TransponderCode)
            {
                // Emergency
                case 7700:
                    Msg[27] = 1 << 4;
                    // TODO: Decide if I want to set alert on td in these cases
                    Msg[0] |= 0x10;
                    break;
                // Lost communication
                case 7600:
                    Msg[27] = 4 << 4;
                    Msg[0] |= 0x10;
                    break;
                // Hijack
                case 7500:
                    Msg[27] = 5 << 4;
                    Msg[0] |= 0x10;
                    break;
                default:
                    Msg[27] = 0;
                    break;
            };
        }
    }
}
