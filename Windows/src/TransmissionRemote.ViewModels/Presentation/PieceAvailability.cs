using System.Numerics;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// Reads Transmission's piece bitfield for the General tab's pieces bar. The first piece is the highest bit of the
/// first byte; bits past the last piece are padding.
/// </summary>
public static class PieceAvailability
{
    /// <summary>
    /// For each of <paramref name="columns"/> pixel columns, the share of its pieces we have, from 0 to 1. With more
    /// columns than pieces, neighbouring columns share a piece. The Mac app's <c>PiecesBar</c>.
    /// </summary>
    public static double[] ColumnShares(ReadOnlySpan<byte> bitfield, int pieceCount, int columns)
    {
        var shares = new double[Math.Max(columns, 0)];
        if (pieceCount <= 0)
        {
            return shares;
        }
        for (var column = 0; column < shares.Length; column++)
        {
            var first = (int)((long)column * pieceCount / shares.Length);
            var last = Math.Max((int)((long)(column + 1) * pieceCount / shares.Length), first + 1);
            last = Math.Min(last, pieceCount);
            var have = 0;
            for (var piece = first; piece < last; piece++)
            {
                if (HasPiece(bitfield, piece))
                {
                    have++;
                }
            }
            shares[column] = last > first ? (double)have / (last - first) : 0;
        }
        return shares;
    }

    /// <summary>The share of all pieces we have, from 0 to 1, for Narrator.</summary>
    public static double HaveFraction(ReadOnlySpan<byte> bitfield, int pieceCount)
    {
        if (pieceCount <= 0)
        {
            return 0;
        }
        var fullBytes = Math.Min(pieceCount / 8, bitfield.Length);
        var have = 0;
        foreach (var value in bitfield[..fullBytes])
        {
            have += BitOperations.PopCount(value);
        }
        for (var piece = fullBytes * 8; piece < pieceCount; piece++)
        {
            if (HasPiece(bitfield, piece))
            {
                have++;
            }
        }
        return (double)have / pieceCount;
    }

    private static bool HasPiece(ReadOnlySpan<byte> bitfield, int piece)
    {
        var index = piece / 8;
        return index < bitfield.Length && (bitfield[index] & (0x80 >> (piece % 8))) != 0;
    }
}
