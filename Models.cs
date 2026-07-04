namespace RlpTool;

internal enum DiagnosticSeverity
{
    Error = 0,
    Warning = 1,
    Info = 2
}

internal readonly record struct SourceSpan(int Start, int Length)
{
    public int End => Start + Length;
}

internal sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    SourceSpan Source,
    string Message);

internal sealed record DecodedByte(
    byte Value,
    int ByteOffset,
    SourceSpan Source,
    bool HasHexError);

internal sealed record HexDecodeResult(
    IReadOnlyList<DecodedByte> Bytes,
    IReadOnlyList<Diagnostic> Diagnostics);

internal enum RlpNodeKind
{
    Bytes,
    List
}

internal sealed class RlpDocument
{
    public RlpDocument(IReadOnlyList<RlpNode> items, int byteLength)
    {
        Items = items;
        ByteLength = byteLength;
    }

    public IReadOnlyList<RlpNode> Items { get; }

    public int ByteLength { get; }
}

internal sealed class RlpNode
{
    public RlpNode(
        RlpNodeKind kind,
        int startByte,
        int endByteExclusive,
        byte prefix,
        int headerBytes,
        int payloadStartByte,
        int payloadBytesAvailable,
        ulong? declaredPayloadLength,
        bool isSingleByte,
        int lengthOfLength,
        int missingLengthBytes,
        ulong missingPayloadBytes,
        IReadOnlyList<RlpNode>? children = null)
    {
        Kind = kind;
        StartByte = startByte;
        EndByteExclusive = endByteExclusive;
        Prefix = prefix;
        HeaderBytes = headerBytes;
        PayloadStartByte = payloadStartByte;
        PayloadBytesAvailable = payloadBytesAvailable;
        DeclaredPayloadLength = declaredPayloadLength;
        IsSingleByte = isSingleByte;
        LengthOfLength = lengthOfLength;
        MissingLengthBytes = missingLengthBytes;
        MissingPayloadBytes = missingPayloadBytes;
        Children = children ?? Array.Empty<RlpNode>();
    }

    public RlpNodeKind Kind { get; }

    public int StartByte { get; }

    public int EndByteExclusive { get; }

    public byte Prefix { get; }

    public int HeaderBytes { get; }

    public int PayloadStartByte { get; }

    public int PayloadBytesAvailable { get; }

    public ulong? DeclaredPayloadLength { get; }

    public bool IsSingleByte { get; }

    public int LengthOfLength { get; }

    public int MissingLengthBytes { get; }

    public ulong MissingPayloadBytes { get; }

    public IReadOnlyList<RlpNode> Children { get; }

    public bool HasMissingData => MissingLengthBytes > 0 || MissingPayloadBytes > 0;
}
