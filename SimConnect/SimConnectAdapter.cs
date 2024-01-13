// ReSharper disable InconsistentNaming

using fs2ff.Converters;
using fs2ff.Models;
using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SimConnectImpl = Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace fs2ff.SimConnect
{
    public class SimConnectAdapter : IDisposable
    {
        /// <summary>
        /// Any data having to do with the players aircraft will have this value.
        /// </summary>
        public const uint OBJECT_ID_USER_RESULT = 1;

        /// <summary>
        /// SIM_FRAME latency tested on my machine (7800X3d) to be ~10ms not sure if this is different on other machines
        /// </summary>
        public const uint SimFrameLatencyMs = 10;

        // The following *Rate consts are multipliers of SIM_FRAME events. The *Rate values below increase the latency
        // by a multiple of SIM_FRAME. i.e. 1 (or 0) == 10ms, 2 == 20ms 3 == 30ms and so on.

        // TODO: Make some sort of user settable inputs for the Default*Rate values.
        private const uint DefaultTrafficRate = 90;

        // GDL90 Spec is 1hz This ties to your self position on the map
        private const uint DefaultPositionRate = 10;

        private const string AppName = "fs2ff";
        private const uint WM_USER_SIMCONNECT = 0x0402;


        private SimConnectImpl? _simConnect;

        private uint _lastSetFrequency = Preferences.Default.att_freq;

        public event Func<Attitude, Task>? AttitudeReceived;
        public event Func<Position, Task>? PositionReceived;
        public event Action<FlightSimState>? StateChanged;
        public event Func<Traffic, uint, Task>? TrafficReceived;
        public event Func<Traffic, uint, Task>? OwnerReceived;

        public bool Connected => _simConnect != null;

        public void Connect(IntPtr hwnd)
        {
            try
            {
                UnsubscribeEvents();

                _simConnect?.Dispose();

                _simConnect = new SimConnectImpl(AppName, hwnd, WM_USER_SIMCONNECT, null, 0);
                //// TODO: Remove this
                //_attitudeTimer = new Timer(RequestAttitudeData, null, 100, 1000 / attitudeFrequency);

                SubscribeEvents();

                StateChanged?.Invoke(FlightSimState.Connected);
            }
            catch (COMException e)
            {
                Console.Error.WriteLine("Exception caught: " + e);
                StateChanged?.Invoke(FlightSimState.ErrorOccurred);
            }
        }

        public void Disconnect() => DisconnectInternal(FlightSimState.Disconnected);

        public void Dispose() => DisconnectInternal(FlightSimState.Disconnected);

        public void ReceiveMessage()
        {
            try
            {
                _simConnect?.ReceiveMessage();
            }
            catch (COMException e)
            {
                Console.Error.WriteLine("Exception caught: " + e);
                DisconnectInternal(FlightSimState.ErrorOccurred);
            }
        }

        public void SetTrafficMaxRadius(uint radius)
        {
            if (Connected)
            {
                var radiusMeters = radius.NmToMeters();
                _simConnect?.RequestDataOnSimObjectType(REQUEST.TrafficAircraft, DEFINITION.Traffic, radiusMeters, SIMCONNECT_SIMOBJECT_TYPE.AIRCRAFT);
                // Right now set Rotary craft 1/2 the distance of Aircraft
                _simConnect?.RequestDataOnSimObjectType(REQUEST.TrafficHelicopter, DEFINITION.Traffic, radiusMeters / 2, SIMCONNECT_SIMOBJECT_TYPE.HELICOPTER);
            }
        }

        // GP ties Attitude, Ownership and Position (to a lesser extent) together in Synthetic Vision
        // The spec on the GDL90 Spec on these is lower but a faster than spec rate
        // seems to smooth out the SV. Garmin needs to decouple Owner from the SV pitch/roll/yaw values in SV.
        public void SetAttitudeFrequency(uint frequency)
        {
            if (_lastSetFrequency != frequency && Connected)
            {
                SetAttitudeDataRate(frequency);
            }
        }

        private void SetAttitudeDataRate(uint frequency)
        {
            var rate = (1000 / frequency) / SimFrameLatencyMs;
            rate = rate.AdjustToBounds<uint>(1, 1000);
            uint posRate;

            if (ViewModelLocator.Main.DataGdl90Enabled)
            {
                // Required for GDL90. Owner report tightly tide to Attitude in GP Position not so much
                _simConnect?.RequestDataOnSimObject(
                    REQUEST.Owner, DEFINITION.Traffic,
                    SimConnectImpl.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SIM_FRAME,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0, rate, 0);
                posRate = (rate * 2).AdjustToBounds<uint>(5, 50);
            }
            else
            {
                // For XTRAFFIC position is very closely coupled.
                posRate = rate;
            }

            // GDL90 specs calls for just 1hz but the EFBs I use work better at this rate
            _simConnect?.RequestDataOnSimObject(
                REQUEST.Position, DEFINITION.Position,
                SimConnectImpl.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, posRate, 0);

            _simConnect?.RequestDataOnSimObject(
                REQUEST.Attitude, DEFINITION.Attitude,
                SimConnectImpl.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, rate, 0);
        }


        private void AddToDataDefinition(DEFINITION defineId, string datumName, string? unitsName, SIMCONNECT_DATATYPE datumType = SIMCONNECT_DATATYPE.FLOAT64)
        {
            _simConnect?.AddToDataDefinition(defineId, datumName, unitsName, datumType, 0, SimConnectImpl.SIMCONNECT_UNUSED);
        }

        private void DisconnectInternal(FlightSimState state)
        {
            UnsubscribeEvents();

            _simConnect?.Dispose();
            _simConnect = null;

            StateChanged?.Invoke(state);
        }

        private void RegisterAttitudeStruct()
        {
            AddToDataDefinition(DEFINITION.Attitude, "PLANE PITCH DEGREES", "Degrees");
            AddToDataDefinition(DEFINITION.Attitude, "PLANE BANK DEGREES", "Degrees");
            AddToDataDefinition(DEFINITION.Attitude, "PLANE HEADING DEGREES TRUE", "Degrees");
            AddToDataDefinition(DEFINITION.Attitude, "TURN COORDINATOR BALL", "Degrees");
            AddToDataDefinition(DEFINITION.Attitude, "DELTA HEADING RATE", "Degrees");
            AddToDataDefinition(DEFINITION.Attitude, "AIRSPEED INDICATED", "Knots");
            AddToDataDefinition(DEFINITION.Attitude, "AIRSPEED TRUE", "Knots");
            AddToDataDefinition(DEFINITION.Attitude, "PRESSURE ALTITUDE", "Feet");
            AddToDataDefinition(DEFINITION.Attitude, "VELOCITY WORLD Y", "Feet per minute");
            AddToDataDefinition(DEFINITION.Attitude, "G FORCE", "Gforce");

            _simConnect?.RegisterDataDefineStruct<Attitude>(DEFINITION.Attitude);
        }

        private void RegisterPositionStruct()
        {
            AddToDataDefinition(DEFINITION.Position, "PLANE LATITUDE", "Degrees");
            AddToDataDefinition(DEFINITION.Position, "PLANE LONGITUDE", "Degrees");
            AddToDataDefinition(DEFINITION.Position, "PLANE ALTITUDE", "Feet");
            // Because X-Plane protocol uses self position in meters
            AddToDataDefinition(DEFINITION.Position, "PLANE ALTITUDE", "meters");
            AddToDataDefinition(DEFINITION.Position, "GPS GROUND TRUE TRACK", "Degrees");
            AddToDataDefinition(DEFINITION.Position, "GPS GROUND SPEED", "Meters per second");

            _simConnect?.RegisterDataDefineStruct<Position>(DEFINITION.Position);
        }

        private void RegisterTrafficStruct()
        {
            AddToDataDefinition(DEFINITION.Traffic, "PLANE LATITUDE", "Degrees");
            AddToDataDefinition(DEFINITION.Traffic, "PLANE LONGITUDE", "Degrees");
            AddToDataDefinition(DEFINITION.Traffic, "PLANE ALTITUDE", "Feet");
            // BUGBUG: All other traffic is reporting the Owners Pressure altitude (SU7) Use Plane AltitudeFeet instead
            AddToDataDefinition(DEFINITION.Traffic, "PRESSURE ALTITUDE", "Feet");
            AddToDataDefinition(DEFINITION.Traffic, "VELOCITY WORLD Y", "Feet per minute");
            AddToDataDefinition(DEFINITION.Traffic, "SIM ON GROUND", "Bool", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(DEFINITION.Traffic, "PLANE HEADING DEGREES TRUE", "Degrees");
            AddToDataDefinition(DEFINITION.Traffic, "GROUND VELOCITY", "Knots");
            AddToDataDefinition(DEFINITION.Traffic, "ATC ID", null, SIMCONNECT_DATATYPE.STRING64);
            AddToDataDefinition(DEFINITION.Traffic, "ATC AIRLINE", null, SIMCONNECT_DATATYPE.STRING64);
            AddToDataDefinition(DEFINITION.Traffic, "ATC FLIGHT NUMBER", null, SIMCONNECT_DATATYPE.STRING8);
            AddToDataDefinition(DEFINITION.Traffic, "Category", null, SIMCONNECT_DATATYPE.STRING32);
            AddToDataDefinition(DEFINITION.Traffic, "MAX GROSS WEIGHT", "Pounds");
            AddToDataDefinition(DEFINITION.Traffic, "AIRSPEED INDICATED", "Knots");
            AddToDataDefinition(DEFINITION.Traffic, "AIRSPEED TRUE", "Knots");
            AddToDataDefinition(DEFINITION.Traffic, "TRANSPONDER CODE:1", null, SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(DEFINITION.Traffic, "TRANSPONDER STATE:1", null, SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(DEFINITION.Traffic, "ATC MODEL", null, SIMCONNECT_DATATYPE.STRING32);
            AddToDataDefinition(DEFINITION.Traffic, "MACH MAX OPERATE", "Mach");
            AddToDataDefinition(DEFINITION.Traffic, "MAX G FORCE", "Gforce");
            AddToDataDefinition(DEFINITION.Traffic, "LIGHT BEACON", "Bool", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(DEFINITION.Traffic, "PLANE ALT ABOVE GROUND MINUS CG", "Feet");

            _simConnect?.RegisterDataDefineStruct<Traffic>(DEFINITION.Traffic);
        }


        /// <summary>
        /// Tracks Traffic added after the initial connection
        /// TODO: This causes a lot of traffic and some EFBs can't handle high X-PLane protocal traffic need to fix
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="data"></param>
        private void SimConnect_OnRecvEventObjectAddRemove(SimConnectImpl sender, SIMCONNECT_RECV_EVENT_OBJECT_ADDREMOVE data)
        {
            if (data.uEventID == (uint)EVENT.ObjectAdded &&
                (data.eObjType == SIMCONNECT_SIMOBJECT_TYPE.AIRCRAFT ||
                 data.eObjType == SIMCONNECT_SIMOBJECT_TYPE.HELICOPTER) &&
                data.dwData != OBJECT_ID_USER_RESULT)
            {
                _simConnect?.RequestDataOnSimObject(
                    REQUEST.TrafficObjectBase + data.dwData,
                    DEFINITION.Traffic, data.dwData,
                    SIMCONNECT_PERIOD.SIM_FRAME,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0, DefaultTrafficRate, 0);
            }
        }

        private void SimConnect_OnRecvException(SimConnectImpl sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            Console.Error.WriteLine("Exception caught: " + data.dwException);
            DisconnectInternal(FlightSimState.ErrorOccurred);
        }

        private void SimConnect_OnRecvOpen(SimConnectImpl sender, SIMCONNECT_RECV data)
        {
            RegisterPositionStruct();
            RegisterAttitudeStruct();
            RegisterTrafficStruct();

            this.SetAttitudeDataRate(ViewModelLocator.Main.AttitudeFrequency);

            SetTrafficMaxRadius(ViewModelLocator.Main.TrafficRadiusNm);

            _simConnect?.SubscribeToSystemEvent(EVENT.ObjectAdded, "ObjectAdded");
            _simConnect?.SubscribeToSystemEvent(EVENT.SixHz, "6Hz");

            // TODO: will use the Airport data for setting up FIS-B weather reports These are broken right now,
            // they return the same 1234 airports 31 times.
            // _simConnect?.RequestFacilitiesList(SIMCONNECT_FACILITY_LIST_TYPE.AIRPORT, REQUEST.Airport);
            //_simConnect?.SubscribeToFacilities(SIMCONNECT_FACILITY_LIST_TYPE.AIRPORT, REQUEST.Airport);
        }

        private void SimConnect_OnRecvQuit(SimConnectImpl sender, SIMCONNECT_RECV data)
        {
            DisconnectInternal(FlightSimState.Quit);
        }

        private async void SimConnect_OnRecvSimobjectData(SimConnectImpl sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if (data.dwRequestID == (uint)REQUEST.TrafficObjectBase)
            {
                return;
            }

            var dwObjectID = data.dwObjectID;

            if (data.dwRequestID == (uint)REQUEST.Position &&
                data.dwDefineID == (uint)DEFINITION.Position &&
                data.dwData?.FirstOrDefault() is Position pos)
            {
                await PositionReceived.RaiseAsync(pos).ConfigureAwait(false);
                return;
            }

            if (data.dwRequestID == (uint)REQUEST.Attitude &&
                data.dwDefineID == (uint)DEFINITION.Attitude &&
                data.dwData?.FirstOrDefault() is Attitude att)
            {
                await AttitudeReceived.RaiseAsync(att).ConfigureAwait(false);
                return;
            }

            if (data.dwRequestID == (uint)REQUEST.Owner &&
                data.dwDefineID == (uint)DEFINITION.Traffic)
            {
                if ((dwObjectID == OBJECT_ID_USER_RESULT
                || dwObjectID == SimConnectImpl.SIMCONNECT_OBJECT_ID_USER) &&
                data.dwData?.FirstOrDefault() is Traffic owner)
                {
                    await OwnerReceived.RaiseAsync(owner, OBJECT_ID_USER_RESULT).ConfigureAwait(false);
                    return;
                }

                Debug.WriteLine($"Owner Request Bad: dwID: {data.dwID}, dwObjectID: {dwObjectID}");
                return;
            }

            if (data.dwRequestID == (uint)REQUEST.TrafficObjectBase + dwObjectID &&
                data.dwDefineID == (uint)DEFINITION.Traffic &&
                dwObjectID != OBJECT_ID_USER_RESULT &&
                dwObjectID != SimConnectImpl.SIMCONNECT_OBJECT_ID_USER &&
                data.dwData?.FirstOrDefault() is Traffic tfk)
            {
                // Prevents all the parked aircraft from showing up on ADS-B
                // Modified to work better with VATSIM since it doesn't report Transponder state
                if (!ViewModelLocator.Main.DataHideTrafficEnabled || !tfk.OnGround || tfk.TransponderState != TransponderState.Off || tfk.LightBeaconOn)
                {
                    try
                    {
                        await TrafficReceived.RaiseAsync(tfk, data.dwRequestID).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // been getting some bad traffic lately just extra protection
                        Console.Error.WriteLine($"Exception: {ex}");
                    }
                }

                return;
            }

            Debug.WriteLine($"Unhandled event: {data.dwID}");
        }

        //TODO: instead of asking doing a one second add for every traffic object
        // we could just do a pump on The traffic Object Type. There is some SDK documents that
        // state Traffic can have issues with this type of data pump.
        private void SimConnect_OnRecvSimObjectDataByType(SimConnectImpl sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
        {
            var dwObjectID = data.dwObjectID;
            if ((data.dwRequestID == (uint)REQUEST.TrafficAircraft ||
                 data.dwRequestID == (uint)REQUEST.TrafficHelicopter) &&
                data.dwDefineID == (uint)DEFINITION.Traffic &&
                dwObjectID != OBJECT_ID_USER_RESULT)
            {
                _simConnect?.RequestDataOnSimObject(
                    REQUEST.TrafficObjectBase + dwObjectID,
                    DEFINITION.Traffic, dwObjectID,
                    SIMCONNECT_PERIOD.SECOND,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0, 0, 0);
            }
        }

        private void SubscribeEvents()
        {
            if (_simConnect != null)
            {
                _simConnect.OnRecvOpen += SimConnect_OnRecvOpen;
                _simConnect.OnRecvQuit += SimConnect_OnRecvQuit;
                _simConnect.OnRecvException += SimConnect_OnRecvException;
                _simConnect.OnRecvSimobjectData += SimConnect_OnRecvSimobjectData;
                _simConnect.OnRecvSimobjectDataBytype += SimConnect_OnRecvSimObjectDataByType;
                _simConnect.OnRecvEventObjectAddremove += SimConnect_OnRecvEventObjectAddRemove;
                //                _simConnect.OnRecvAirportList += _simConnect_OnRecvAirportList;
                //                _simConnect.OnRecvCloudState += _simConnect_OnRecvCloudState;
            }
        }

        private void UnsubscribeEvents()
        {
            if (_simConnect != null)
            {
                _simConnect.OnRecvEventObjectAddremove -= SimConnect_OnRecvEventObjectAddRemove;
                _simConnect.OnRecvSimobjectDataBytype -= SimConnect_OnRecvSimObjectDataByType;
                _simConnect.OnRecvSimobjectData -= SimConnect_OnRecvSimobjectData;
                _simConnect.OnRecvException -= SimConnect_OnRecvException;
                _simConnect.OnRecvQuit -= SimConnect_OnRecvQuit;
                _simConnect.OnRecvOpen -= SimConnect_OnRecvOpen;
                //                _simConnect.OnRecvAirportList -= _simConnect_OnRecvAirportList;
                //                _simConnect.OnRecvCloudState += _simConnect_OnRecvCloudState;
            }
        }

        //private void _simConnect_OnRecvCloudState(SimConnectImpl sender, SIMCONNECT_RECV_CLOUD_STATE data)
        //{
        //    Debug.WriteLine($"Data: {data.dwArraySize}");
        //}

        //private Dictionary<string, SIMCONNECT_DATA_FACILITY_AIRPORT> airports = new Dictionary<string, SIMCONNECT_DATA_FACILITY_AIRPORT>();
        //private void _simConnect_OnRecvAirportList(SimConnectImpl sender, SIMCONNECT_RECV_AIRPORT_LIST data)
        //{
        //    Debug.WriteLine($"Data: {data.dwArraySize}");
        //    var owner = ViewModelLocator.Main.OwnerInfo;
        //    foreach (SIMCONNECT_DATA_FACILITY_AIRPORT airport in data.rgData)
        //    {
        //        airports.TryAdd(airport.Icao, airport);
        //        if (Math.Round(airport.Latitude) == Math.Round(owner.Latitude) && Math.Round(airport.Longitude) == Math.Round(owner.Longitude))
        //        {
        //            _simConnect?.WeatherRequestCloudState(REQUEST.Weather, (float)airport.Latitude - 1, (float)airport.Longitude + 1, 100, (float)airport.Latitude + 1, (float)airport.Longitude + 1, 10000, 0);
        //        }
        //    }
        //}

    }
}
