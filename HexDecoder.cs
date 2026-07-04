namespace RlpTool;

internal static class HexDecoder
{
    public static HexDecodeResult Decode(string input)
    {
        var diagnostics = new List<Diagnostic>();
        var nibbles = new List<Nibble>();
        var start = FirstHexPosition(input);

        for (var i = start; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            var value = HexValue(c);
            var hasError = value < 0;
            if (hasError)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "HEX001",
                    new SourceSpan(i, 1),
                    $"Invalid hex symbol '{Printable(c)}'. It is interpreted as nibble 0 so parsing can continue."));
                value = 0;
            }

            nibbles.Add(new Nibble(value, i, hasError));
        }

        var bytes = new List<DecodedByte>();
        for (var i = 0; i < nibbles.Count; i += 2)
        {
            var high = nibbles[i];
            var hasError = high.HasError;
            int value;
            SourceSpan source;

            if (i + 1 >= nibbles.Count)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "HEX002",
                    new SourceSpan(high.SourceIndex, 1),
                    "Odd number of hex digits. The final byte is missing its low nibble; 0 is used for recovery."));

                value = high.Value << 4;
                source = new SourceSpan(high.SourceIndex, 1);
                hasError = true;
            }
            else
            {
                var low = nibbles[i + 1];
                value = (high.Value << 4) | low.Value;
                source = new SourceSpan(high.SourceIndex, low.SourceIndex - high.SourceIndex + 1);
                hasError |= low.HasError;
            }

            bytes.Add(new DecodedByte((byte)value, bytes.Count, source, hasError));
        }

        return new HexDecodeResult(bytes, diagnostics);
    }

    private static int FirstHexPosition(string input)
    {
        var start = 0;
        while (start < input.Length && char.IsWhiteSpace(input[start]))
        {
            start++;
        }

        if (start + 1 < input.Length &&
            input[start] == '0' &&
            (input[start + 1] == 'x' || input[start + 1] == 'X'))
        {
            return start + 2;
        }

        return start;
    }

    private static int HexValue(char c)
    {
        if (c is >= '0' and <= '9')
        {
            return c - '0';
        }

        if (c is >= 'a' and <= 'f')
        {
            return c - 'a' + 10;
        }

        if (c is >= 'A' and <= 'F')
        {
            return c - 'A' + 10;
        }

        return -1;
    }

    private static string Printable(char c) => c switch
    {
        '\r' => "\\r",
        '\n' => "\\n",
        '\t' => "\\t",
        _ => c.ToString()
    };

    private readonly record struct Nibble(int Value, int SourceIndex, bool HasError);
}
