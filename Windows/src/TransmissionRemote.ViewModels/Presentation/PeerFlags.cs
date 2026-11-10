namespace TransmissionRemote.ViewModels;

/// <summary>One letter of Transmission's peer flags. The app explains each in the Flags column's tooltip.</summary>
public enum PeerFlagLetter
{
    /// <summary>O</summary>
    OptimisticUnchoke,

    /// <summary>D</summary>
    DownloadingFrom,

    /// <summary>d: we would download from this peer if they'd let us.</summary>
    WouldDownloadFrom,

    /// <summary>U</summary>
    UploadingTo,

    /// <summary>u: we would upload to this peer if they'd ask.</summary>
    WouldUploadTo,

    /// <summary>K: the peer has unchoked us, but we're not interested.</summary>
    UnchokedUsButNotInterested,

    /// <summary>?: we unchoked this peer, but they're not interested.</summary>
    UnchokedByUsButNotInterested,

    /// <summary>E</summary>
    Encrypted,

    /// <summary>H</summary>
    FoundThroughDht,

    /// <summary>X</summary>
    FoundThroughPex,

    /// <summary>I</summary>
    Incoming,

    /// <summary>T</summary>
    ConnectedOverUtp,
}

public static class PeerFlags
{
    /// <summary>The flags in <paramref name="flags"/> (Transmission's <c>flagStr</c>), in order; unknown letters are skipped.</summary>
    public static IReadOnlyList<PeerFlagLetter> Parse(string flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        List<PeerFlagLetter> result = [];
        foreach (var letter in flags)
        {
            if (FlagFor(letter) is { } flag)
            {
                result.Add(flag);
            }
        }
        return result;
    }

    private static PeerFlagLetter? FlagFor(char letter) => letter switch
    {
        'O' => PeerFlagLetter.OptimisticUnchoke,
        'D' => PeerFlagLetter.DownloadingFrom,
        'd' => PeerFlagLetter.WouldDownloadFrom,
        'U' => PeerFlagLetter.UploadingTo,
        'u' => PeerFlagLetter.WouldUploadTo,
        'K' => PeerFlagLetter.UnchokedUsButNotInterested,
        '?' => PeerFlagLetter.UnchokedByUsButNotInterested,
        'E' => PeerFlagLetter.Encrypted,
        'H' => PeerFlagLetter.FoundThroughDht,
        'X' => PeerFlagLetter.FoundThroughPex,
        'I' => PeerFlagLetter.Incoming,
        'T' => PeerFlagLetter.ConnectedOverUtp,
        _ => null,
    };
}
