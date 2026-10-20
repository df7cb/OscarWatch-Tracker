using PortAudioSharp;

namespace OscarWatch.Terminal;

/// <summary>
/// PortAudio reports every failed stream open as "Error opening PortAudio Stream", including when
/// the sound card is already open. Callers turn that into an operator message.
/// </summary>
internal static class PacketAudioOpenFailure
{
    public const string PortAudioMessage = "Error opening PortAudio Stream";

    public static bool IsStreamOpenFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PortAudioException || IsStreamOpenFailure(current.Message))
                return true;
        }

        return false;
    }

    public static bool IsStreamOpenFailure(string? message) =>
        message is not null && message.Contains(PortAudioMessage, StringComparison.Ordinal);
}
