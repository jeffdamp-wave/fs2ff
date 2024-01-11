namespace fs2ff.SimConnect
{
    /// <summary>
    /// Various states that SimConnect can be in
    /// </summary>
    public enum FlightSimState
    {
        Unknown,
        Connected,
        Disconnected,
        ErrorOccurred,
        AutoConnecting,
        Quit
    }
}
