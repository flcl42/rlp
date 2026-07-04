using System.Text;

namespace RlpTool;

internal sealed class ConsoleRenderer
{
    private const int HorizontalPadding = 5;
    private readonly string _input;
    private readonly IReadOnlyList<DecodedByte> _bytes;
    private readonly IReadOnlyList<Diagnostic> _diagnostics;

    public ConsoleRenderer(string input, IReadOnlyList<DecodedByte> bytes, IReadOnlyList<Diagnostic> diagnostics)
    {
        _input = input;
        _bytes = bytes;
        _diagnostics = diagnostics;
    }

    public void Print(RlpDocument document)
    {
        PrintEncoded(document);
        Console.WriteLine();
        PrintSummary(document);
        Console.WriteLine();
        PrintTree(document);
    }

    private void PrintEncoded(RlpDocument document)
    {
        Console.WriteLine(Ansi.Bold("Encoded"));
        Console.WriteLine($"     {Ansi.Magenta("list marker")} {Ansi.Yellow("byte marker")} {FormatValueLegend()} {Ansi.Red("recovered/error byte")}");

        if (_bytes.Count == 0)
        {
            Console.WriteLine("     " + Ansi.Dim("<empty>"));
            PrintUnmappedDiagnostics();
            return;
        }

        var styles = BuildByteStyles(document);
        var diagnostics = _diagnostics.Select(MapDiagnostic).ToArray();
        var maxHexChars = GetEncodedLineWidth();
        var totalHexChars = _bytes.Count * 2;

        for (var lineStart = 0; lineStart < totalHexChars; lineStart += maxHexChars)
        {
            var lineLength = Math.Min(maxHexChars, totalHexChars - lineStart);
            Console.WriteLine(new string(' ', HorizontalPadding) + ColorHexRange(lineStart, lineLength, styles));

            foreach (var diagnostic in diagnostics.Where(d => d.Overlaps(lineStart, lineLength)))
            {
                PrintEncodedDiagnostic(diagnostic, lineStart, lineLength);
            }
        }

        foreach (var diagnostic in diagnostics.Where(d => d.NormalizedStart >= totalHexChars))
        {
            PrintEncodedDiagnostic(diagnostic, totalHexChars, 1);
        }
    }

    private void PrintSummary(RlpDocument document)
    {
        var errors = _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warnings = _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        var status = errors == 0
            ? Ansi.Green("ok")
            : Ansi.Red($"{errors} error(s)");
        if (warnings > 0)
        {
            status += ", " + Ansi.Yellow($"{warnings} warning(s)");
        }

        Console.WriteLine(Ansi.Bold("Summary"));
        Console.WriteLine($"  decoded bytes: {_bytes.Count}");
        Console.WriteLine($"  top-level items: {document.Items.Count}");
        Console.WriteLine($"  status: {status}");
    }

    private void PrintTree(RlpDocument document)
    {
        Console.WriteLine(Ansi.Bold("Parsed"));
        if (document.Items.Count == 0)
        {
            Console.WriteLine(Ansi.Red("  <no RLP items>"));
            return;
        }

        Console.WriteLine($"  stream bytes={document.ByteLength} top-level-items={document.Items.Count}");
        for (var i = 0; i < document.Items.Count; i++)
        {
            PrintNode(document.Items[i], depth: 1, label: $"[{i}]");
        }
    }

    private void PrintNode(RlpNode node, int depth, string label)
    {
        var indent = new string(' ', depth * 2);
        var labelText = Ansi.Cyan(label.PadRight(7));
        var range = FormatByteRange(node.StartByte, node.EndByteExclusive);
        var problem = node.HasMissingData ? Ansi.Red(" !") : string.Empty;

        if (node.Kind == RlpNodeKind.List)
        {
            var listSize = FormatSize(node, "inside");
            Console.WriteLine($"{indent}{labelText} {range,-13} list  elements={node.Children.Count} {listSize}{problem}");

            if (node.MissingLengthBytes > 0)
            {
                Console.WriteLine($"{indent}  {Ansi.Red($"<missing {node.MissingLengthBytes} length byte(s)>")}");
            }

            for (var i = 0; i < node.Children.Count; i++)
            {
                PrintNode(node.Children[i], depth + 1, $"[{i}]");
            }

            if (node.MissingPayloadBytes > 0)
            {
                Console.WriteLine($"{indent}  {Ansi.Red($"<missing {node.MissingPayloadBytes} list payload byte(s)>")}");
            }

            return;
        }

        var value = FullHex(node.PayloadStartByte, node.PayloadBytesAvailable);
        if (node.MissingPayloadBytes > 0)
        {
            value += Ansi.Red($"<missing {node.MissingPayloadBytes} byte(s)>");
        }

        var ascii = FullAscii(node.PayloadStartByte, node.PayloadBytesAvailable);
        var valueSize = FormatSize(node, "length");
        Console.WriteLine($"{indent}{labelText} {range,-13} value {value} {valueSize}{problem}");
        if (ascii.Length > 0)
        {
            Console.WriteLine($"{indent}        ascii \"{ascii}\"");
        }

        if (node.MissingLengthBytes > 0)
        {
            Console.WriteLine($"{indent}  {Ansi.Red($"<missing {node.MissingLengthBytes} length byte(s)>")}");
        }
    }

    private string FormatSize(RlpNode node, string payloadLabel)
    {
        var prefix = node.HeaderBytes;
        if (node.DeclaredPayloadLength is not { } declared)
        {
            return $"({payloadLabel}={Ansi.Red("<missing>")} + prefix={prefix} => total={Ansi.Red("<unknown>")})";
        }

        var total = declared + (ulong)prefix;
        var available = (ulong)node.PayloadBytesAvailable == declared
            ? string.Empty
            : $" available={node.PayloadBytesAvailable}";

        return $"({payloadLabel}={declared} + prefix={prefix} => total={total}{available})";
    }

    private ByteVisual[] BuildByteStyles(RlpDocument document)
    {
        var styles = Enumerable.Repeat(new ByteVisual(ByteVisualKind.Unclassified), _bytes.Count).ToArray();
        var nextShade = 0;
        foreach (var item in document.Items)
        {
            MarkNode(item, styles, ref nextShade);
        }

        return styles;
    }

    private void MarkNode(RlpNode node, ByteVisual[] styles, ref int nextShade)
    {
        if (node.Kind == RlpNodeKind.List)
        {
            MarkRange(node.StartByte, node.HeaderBytes, new ByteVisual(ByteVisualKind.ListMarker), styles);
            foreach (var child in node.Children)
            {
                MarkNode(child, styles, ref nextShade);
            }

            return;
        }

        var payloadStyle = new ByteVisual(ByteVisualKind.ByteString, nextShade++);
        if (node.IsSingleByte)
        {
            MarkRange(node.StartByte, 1, payloadStyle, styles);
            return;
        }

        MarkRange(node.StartByte, node.HeaderBytes, new ByteVisual(ByteVisualKind.ByteMarker), styles);
        MarkRange(node.PayloadStartByte, node.PayloadBytesAvailable, payloadStyle, styles);
    }

    private void MarkRange(int startByte, int byteLength, ByteVisual style, ByteVisual[] styles)
    {
        for (var i = 0; i < byteLength; i++)
        {
            var byteOffset = startByte + i;
            if (byteOffset >= 0 && byteOffset < styles.Length)
            {
                styles[byteOffset] = style;
            }
        }
    }

    private string ColorHexRange(int normalizedStart, int normalizedLength, IReadOnlyList<ByteVisual> styles)
    {
        var builder = new StringBuilder(normalizedLength + 32);
        var normalizedEnd = normalizedStart + normalizedLength;
        for (var hexIndex = normalizedStart; hexIndex < normalizedEnd; hexIndex += 2)
        {
            var byteOffset = hexIndex / 2;
            var hex = _bytes[byteOffset].Value.ToString("x2");

            if (_bytes[byteOffset].HasHexError)
            {
                builder.Append(Ansi.Red(hex));
                continue;
            }

            builder.Append(styles[byteOffset].Kind switch
            {
                ByteVisualKind.ListMarker => Ansi.Magenta(hex),
                ByteVisualKind.ByteMarker => Ansi.Yellow(hex),
                ByteVisualKind.ByteString => FormatValueHex(hex, styles[byteOffset].Shade),
                _ => Ansi.Dim(hex)
            });
        }

        return builder.ToString();
    }

    private static string FormatValueLegend()
    {
        var parts = new[]
        {
            FormatValueHex("value1", 0),
            FormatValueHex("value2", 1)
        };
        return string.Join(" ", parts);
    }

    private static string FormatValueHex(string text, int shade)
    {
        var color = ValuePalette[Math.Abs(shade) % ValuePalette.Length];
        return Ansi.Foreground256(text, color);
    }

    private NormalizedDiagnostic MapDiagnostic(Diagnostic diagnostic)
    {
        var first = -1;
        var last = -1;
        for (var i = 0; i < _bytes.Count; i++)
        {
            if (!Overlaps(_bytes[i].Source, diagnostic.Source))
            {
                continue;
            }

            first = first == -1 ? i : first;
            last = i;
        }

        if (first >= 0)
        {
            return new NormalizedDiagnostic(diagnostic, first * 2, (last - first + 1) * 2);
        }

        if (_bytes.Count == 0)
        {
            return new NormalizedDiagnostic(diagnostic, 0, 1);
        }

        var closest = 0;
        while (closest < _bytes.Count && _bytes[closest].Source.End <= diagnostic.Source.Start)
        {
            closest++;
        }

        return new NormalizedDiagnostic(diagnostic, Math.Min(closest * 2, _bytes.Count * 2), Math.Max(1, diagnostic.Source.Length));
    }

    private void PrintEncodedDiagnostic(NormalizedDiagnostic diagnostic, int lineStart, int lineLength)
    {
        var overlapStart = Math.Max(diagnostic.NormalizedStart, lineStart);
        var overlapEnd = Math.Min(diagnostic.NormalizedEnd, lineStart + lineLength);
        var offset = Math.Max(0, overlapStart - lineStart);
        var length = Math.Max(1, overlapEnd - overlapStart);

        var pointer = new string(' ', HorizontalPadding + offset) + "^" + new string('~', length - 1);
        pointer = diagnostic.Diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => Ansi.Red(pointer),
            DiagnosticSeverity.Warning => Ansi.Yellow(pointer),
            _ => Ansi.Cyan(pointer)
        };

        var sourceChar = Math.Min(diagnostic.Diagnostic.Source.Start + 1, _input.Length + 1);
        var message = $"{diagnostic.Diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Diagnostic.Code} char {sourceChar}: {diagnostic.Diagnostic.Message}";
        Console.WriteLine($"{pointer} {message}");
    }

    private void PrintUnmappedDiagnostics()
    {
        foreach (var diagnostic in _diagnostics)
        {
            var marker = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => Ansi.Red("^"),
                DiagnosticSeverity.Warning => Ansi.Yellow("^"),
                _ => Ansi.Cyan("^")
            };
            var sourceChar = Math.Min(diagnostic.Source.Start + 1, _input.Length + 1);
            Console.WriteLine($"     {marker} {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Code} char {sourceChar}: {diagnostic.Message}");
        }
    }

    private string FullHex(int startByte, int byteLength)
    {
        var builder = new StringBuilder(2 + byteLength * 2);
        builder.Append("0x");
        for (var i = 0; i < byteLength && startByte + i < _bytes.Count; i++)
        {
            builder.Append(_bytes[startByte + i].Value.ToString("x2"));
        }

        return builder.ToString();
    }

    private string FullAscii(int startByte, int byteLength)
    {
        var builder = new StringBuilder(byteLength);
        for (var i = 0; i < byteLength && startByte + i < _bytes.Count; i++)
        {
            var value = _bytes[startByte + i].Value;
            builder.Append(value is >= 0x20 and <= 0x7e ? (char)value : '.');
        }

        return builder.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static int GetEncodedLineWidth()
    {
        var width = 120;
        try
        {
            if (!Console.IsOutputRedirected && Console.WindowWidth > 0)
            {
                width = Console.WindowWidth;
            }
        }
        catch (IOException)
        {
            width = 120;
        }

        var max = Math.Max(16, width - HorizontalPadding * 2);
        return max % 2 == 0 ? max : max - 1;
    }

    private static bool Overlaps(SourceSpan left, SourceSpan right)
    {
        var leftEnd = left.End;
        var rightEnd = right.End;
        if (left.Length == 0)
        {
            leftEnd++;
        }

        if (right.Length == 0)
        {
            rightEnd++;
        }

        return left.Start < rightEnd && right.Start < leftEnd;
    }

    private static string FormatByteRange(int start, int end) => $"@{start:x4}..{end:x4}";

    private enum ByteVisualKind
    {
        Unclassified,
        ListMarker,
        ByteMarker,
        ByteString
    }

    private readonly record struct ByteVisual(ByteVisualKind Kind, int Shade = 0);

    private static readonly int[] ValuePalette =
    [
        34,  // green
        120  // light green
    ];

    private readonly record struct NormalizedDiagnostic(Diagnostic Diagnostic, int NormalizedStart, int NormalizedLength)
    {
        public int NormalizedEnd => NormalizedStart + Math.Max(1, NormalizedLength);

        public bool Overlaps(int start, int length) => NormalizedStart < start + length && start < NormalizedEnd;
    }
}
