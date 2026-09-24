using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed record MissionContinuationResult(
    [property: JsonPropertyName("context")] ContextCaptureResult Context,
    [property: JsonPropertyName("relay")] MissionRelayEntry Relay);