namespace RlpTool;

internal sealed class RlpParser
{
    private const int MaxDepth = 1024;
    private readonly IReadOnlyList<DecodedByte> _bytes;
    private readonly int _inputLength;
    private readonly List<Diagnostic> _diagnostics = new();

    public RlpParser(IReadOnlyList<DecodedByte> bytes, int inputLength)
    {
        _bytes = bytes;
        _inputLength = inputLength;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public RlpDocument ParseDocument()
    {
        if (_bytes.Count == 0)
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "RLP000",
                new SourceSpan(_inputLength, 0),
                "No bytes were decoded from the input, so there is no RLP item to parse."));
            return new RlpDocument(Array.Empty<RlpNode>(), 0);
        }

        var items = new List<RlpNode>();
        var offset = 0;
        while (offset < _bytes.Count)
        {
            var before = offset;
            items.Add(ParseItem(ref offset, _bytes.Count, 0));

            if (offset <= before)
            {
                AddByteDiagnostic(
                    DiagnosticSeverity.Error,
                    "RLP999",
                    before,
                    1,
                    "Parser recovery did not advance. One byte is skipped to avoid an infinite loop.");
                offset = before + 1;
            }
        }

        return new RlpDocument(items, _bytes.Count);
    }

    private RlpNode ParseItem(ref int offset, int limit, int depth)
    {
        if (depth > MaxDepth)
        {
            AddByteDiagnostic(
                DiagnosticSeverity.Error,
                "RLP090",
                offset,
                1,
                $"Maximum nesting depth of {MaxDepth} was exceeded. Remaining bytes in this boundary are treated as one byte string.");

            var recoveryStart = offset;
            offset = Math.Min(limit, offset + 1);
            return new RlpNode(
                RlpNodeKind.Bytes,
                recoveryStart,
                offset,
                _bytes[recoveryStart].Value,
                0,
                recoveryStart,
                offset - recoveryStart,
                (ulong)(offset - recoveryStart),
                isSingleByte: true,
                lengthOfLength: 0,
                missingLengthBytes: 0,
                missingPayloadBytes: 0);
        }

        var start = offset;
        var prefix = _bytes[start].Value;

        if (prefix <= 0x7f)
        {
            offset++;
            return new RlpNode(
                RlpNodeKind.Bytes,
                start,
                offset,
                prefix,
                headerBytes: 0,
                payloadStartByte: start,
                payloadBytesAvailable: 1,
                declaredPayloadLength: 1,
                isSingleByte: true,
                lengthOfLength: 0,
                missingLengthBytes: 0,
                missingPayloadBytes: 0);
        }

        if (prefix <= 0xb7)
        {
            var length = (ulong)(prefix - 0x80);
            return ParseByteString(ref offset, limit, start, prefix, headerBytes: 1, length, lengthOfLength: 0);
        }

        if (prefix <= 0xbf)
        {
            var lengthOfLength = prefix - 0xb7;
            return ParseLongByteString(ref offset, limit, start, prefix, lengthOfLength);
        }

        if (prefix <= 0xf7)
        {
            var length = (ulong)(prefix - 0xc0);
            return ParseList(ref offset, limit, depth, start, prefix, headerBytes: 1, length, lengthOfLength: 0);
        }

        return ParseLongList(ref offset, limit, depth, start, prefix, prefix - 0xf7);
    }

    private RlpNode ParseLongByteString(ref int offset, int limit, int start, byte prefix, int lengthOfLength)
    {
        if (!TryReadDeclaredLength(start, limit, lengthOfLength, "byte string", out var declared, out var missingLengthBytes))
        {
            offset = Math.Min(limit, start + 1 + (lengthOfLength - missingLengthBytes));
            return new RlpNode(
                RlpNodeKind.Bytes,
                start,
                offset,
                prefix,
                headerBytes: offset - start,
                payloadStartByte: offset,
                payloadBytesAvailable: 0,
                declaredPayloadLength: null,
                isSingleByte: false,
                lengthOfLength,
                missingLengthBytes,
                missingPayloadBytes: 0);
        }

        if (declared < 56)
        {
            AddByteDiagnostic(
                DiagnosticSeverity.Warning,
                "RLP101",
                start,
                1,
                $"Long byte-string form is non-canonical for length {declared}. Lengths below 56 must use the short form.");
        }

        return ParseByteString(ref offset, limit, start, prefix, headerBytes: 1 + lengthOfLength, declared, lengthOfLength);
    }

    private RlpNode ParseByteString(
        ref int offset,
        int limit,
        int start,
        byte prefix,
        int headerBytes,
        ulong length,
        int lengthOfLength)
    {
        var payloadStart = start + headerBytes;
        var available = AvailableBytes(payloadStart, limit);
        var payloadAvailable = length > (ulong)available ? available : (int)length;
        var missing = length > (ulong)available ? length - (ulong)available : 0;

        if (missing > 0)
        {
            AddByteDiagnostic(
                DiagnosticSeverity.Error,
                "RLP201",
                start,
                Math.Max(1, headerBytes),
                $"Byte string declares {length} payload byte(s), but only {payloadAvailable} byte(s) are available before the current boundary. Missing {missing} byte(s).");
        }

        if (lengthOfLength == 0 && length == 1 && payloadAvailable == 1 && _bytes[payloadStart].Value <= 0x7f)
        {
            AddByteDiagnostic(
                DiagnosticSeverity.Warning,
                "RLP102",
                start,
                1,
                $"Single byte value 0x{_bytes[payloadStart].Value:x2} must be encoded as itself, not as a length-prefixed byte string.");
        }

        offset = payloadStart + payloadAvailable;
        return new RlpNode(
            RlpNodeKind.Bytes,
            start,
            offset,
            prefix,
            headerBytes,
            payloadStart,
            payloadAvailable,
            length,
            isSingleByte: false,
            lengthOfLength,
            missingLengthBytes: 0,
            missingPayloadBytes: missing);
    }

    private RlpNode ParseLongList(ref int offset, int limit, int depth, int start, byte prefix, int lengthOfLength)
    {
        if (!TryReadDeclaredLength(start, limit, lengthOfLength, "list", out var declared, out var missingLengthBytes))
        {
            offset = Math.Min(limit, start + 1 + (lengthOfLength - missingLengthBytes));
            return new RlpNode(
                RlpNodeKind.List,
                start,
                offset,
                prefix,
                headerBytes: offset - start,
                payloadStartByte: offset,
                payloadBytesAvailable: 0,
                declaredPayloadLength: null,
                isSingleByte: false,
                lengthOfLength,
                missingLengthBytes,
                missingPayloadBytes: 0,
                children: Array.Empty<RlpNode>());
        }

        if (declared < 56)
        {
            AddByteDiagnostic(
                DiagnosticSeverity.Warning,
                "RLP103",
                start,
                1,
                $"Long list form is non-canonical for payload length {declared}. Lengths below 56 must use the short form.");
        }

        return ParseList(ref offset, limit, depth, start, prefix, headerBytes: 1 + lengthOfLength, declared, lengthOfLength);
    }

    private RlpNode ParseList(
        ref int offset,
        int limit,
        int depth,
        int start,
        byte prefix,
        int headerBytes,
        ulong length,
        int lengthOfLength)
    {
        var payloadStart = start + headerBytes;
        var available = AvailableBytes(payloadStart, limit);
        var payloadAvailable = length > (ulong)available ? available : (int)length;
        var missing = length > (ulong)available ? length - (ulong)available : 0;
        var payloadEnd = payloadStart + payloadAvailable;

        if (missing > 0)
        {
            AddByteDiagnostic(
                DiagnosticSeverity.Error,
                "RLP202",
                start,
                Math.Max(1, headerBytes),
                $"List declares {length} payload byte(s), but only {payloadAvailable} byte(s) are available before the current boundary. Missing {missing} byte(s).");
        }

        var children = new List<RlpNode>();
        var childOffset = payloadStart;
        while (childOffset < payloadEnd)
        {
            var before = childOffset;
            children.Add(ParseItem(ref childOffset, payloadEnd, depth + 1));
            if (childOffset <= before)
            {
                AddByteDiagnostic(
                    DiagnosticSeverity.Error,
                    "RLP998",
                    before,
                    1,
                    "List parser recovery did not advance. One byte is skipped to keep parsing.");
                childOffset = before + 1;
            }
        }

        offset = payloadEnd;
        return new RlpNode(
            RlpNodeKind.List,
            start,
            offset,
            prefix,
            headerBytes,
            payloadStart,
            payloadAvailable,
            length,
            isSingleByte: false,
            lengthOfLength,
            missingLengthBytes: 0,
            missingPayloadBytes: missing,
            children);
    }

    private bool TryReadDeclaredLength(
        int itemStart,
        int limit,
        int lengthOfLength,
        string itemKind,
        out ulong declared,
        out int missingLengthBytes)
    {
        declared = 0;
        var lengthStart = itemStart + 1;
        var available = AvailableBytes(lengthStart, limit);
        var lengthBytesAvailable = Math.Min(lengthOfLength, available);
        missingLengthBytes = lengthOfLength - lengthBytesAvailable;

        if (missingLengthBytes > 0)
        {
            AddByteDiagnostic(
                DiagnosticSeverity.Error,
                "RLP200",
                itemStart,
                1,
                $"Long {itemKind} prefix requires {lengthOfLength} length byte(s), but only {lengthBytesAvailable} are available. Missing {missingLengthBytes} length byte(s).");

            for (var i = 0; i < lengthBytesAvailable; i++)
            {
                declared = (declared << 8) | _bytes[lengthStart + i].Value;
            }

            return false;
        }

        if (lengthOfLength > 1 && _bytes[lengthStart].Value == 0)
        {
            AddByteDiagnostic(
                DiagnosticSeverity.Warning,
                "RLP104",
                lengthStart,
                1,
                "Length has a leading zero byte. RLP length values must be minimally encoded.");
        }

        for (var i = 0; i < lengthOfLength; i++)
        {
            declared = (declared << 8) | _bytes[lengthStart + i].Value;
        }

        return true;
    }

    private static int AvailableBytes(int start, int limit)
    {
        if (start >= limit)
        {
            return 0;
        }

        return limit - start;
    }

    private void AddByteDiagnostic(
        DiagnosticSeverity severity,
        string code,
        int byteOffset,
        int byteLength,
        string message)
    {
        _diagnostics.Add(new Diagnostic(severity, code, SourceForBytes(byteOffset, byteLength), message));
    }

    private SourceSpan SourceForBytes(int byteOffset, int byteLength)
    {
        if (byteOffset < 0 || byteOffset >= _bytes.Count)
        {
            return new SourceSpan(_inputLength, 0);
        }

        var last = Math.Min(_bytes.Count - 1, byteOffset + Math.Max(1, byteLength) - 1);
        var start = _bytes[byteOffset].Source.Start;
        var end = _bytes[last].Source.End;
        return new SourceSpan(start, Math.Max(1, end - start));
    }
}
