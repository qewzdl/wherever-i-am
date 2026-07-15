public enum ConnectionErrorCode
{
    None,

    EmptyIpAddress,
    InvalidIpAddress,

    ConnectionTimeout,
    ConnectionFailed,

    NetworkAlreadyRunning,
    NetworkManagerMissing,
    TransportMissing,

    StrategyNotFound,
    UnsupportedConnectionRole,

    LobbySceneLoadFailed,

    Unknown,
    Cancelled,

    SceneScopeActivationFailed
}
