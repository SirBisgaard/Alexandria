namespace Alexandria.DataAccess.Files.Books.Comics;

/// <summary>
/// Orders strings the way a person reads page numbers: runs of digits compare by numeric value,
/// so "page 2" sorts before "page 10" and "v1-001" before "v1-010" regardless of zero padding.
/// Non-digit runs compare case-insensitively. Archive entries are frequently stored out of order
/// (RAR especially), so page order must never rely on entry order.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        var i = 0;
        var j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var xStart = i;
                var yStart = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                    i++;
                while (j < y.Length && char.IsAsciiDigit(y[j]))
                    j++;

                var xDigits = x.AsSpan(xStart, i - xStart).TrimStart('0');
                var yDigits = y.AsSpan(yStart, j - yStart).TrimStart('0');
                if (xDigits.Length != yDigits.Length)
                    return xDigits.Length.CompareTo(yDigits.Length);
                var digitComparison = xDigits.SequenceCompareTo(yDigits);
                if (digitComparison != 0)
                    return digitComparison;
                continue;
            }

            var charComparison = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (charComparison != 0)
                return charComparison;
            i++;
            j++;
        }

        var lengthComparison = (x.Length - i).CompareTo(y.Length - j);
        return lengthComparison != 0 ? lengthComparison : string.CompareOrdinal(x, y);
    }
}
