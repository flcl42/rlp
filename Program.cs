using System.Text;
using RlpTool;

Console.OutputEncoding = Encoding.UTF8;

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    PrintUsage();
    return 0;
}

Ansi.Enabled = !options.NoColor && !Console.IsOutputRedirected;

var input = options.Input ?? ReadInput();
if (string.IsNullOrWhiteSpace(input))
{
    PrintUsage();
    return 2;
}

var hex = HexDecoder.Decode(input);
var parser = new RlpParser(hex.Bytes, input.Length);
var document = parser.ParseDocument();

var diagnostics = hex.Diagnostics
    .Concat(parser.Diagnostics)
    .OrderBy(d => d.Source.Start)
    .ThenBy(d => d.Severity)
    .ToArray();

var renderer = new ConsoleRenderer(input, hex.Bytes, diagnostics);
renderer.Print(document);
return 0;

static string ReadInput()
{
    if (Console.IsInputRedirected)
    {
        return Console.In.ReadToEnd();
    }

    Console.Write("hex> ");
    return Console.ReadLine() ?? string.Empty;
}

static void PrintUsage()
{
    Console.WriteLine("RLP tolerant parser");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  rlp <hex>");
    Console.WriteLine("  rlp 0x<hex>");
    Console.WriteLine("  dotnet run -- <hex>");
    Console.WriteLine("  dotnet run -- 0x<hex>");
    Console.WriteLine("  Get-Content input.hex | rlp");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --no-color     Disable ANSI colors");
    Console.WriteLine("  --help         Show this help");
}

internal sealed record CliOptions(string? Input, bool NoColor, bool ShowHelp)
{
    public static CliOptions Parse(string[] args)
    {
        var noColor = false;
        var showHelp = false;
        var input = new List<string>();

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--no-color":
                    noColor = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                default:
                    input.Add(arg);
                    break;
            }
        }

        return new CliOptions(input.Count == 0 ? null : string.Join(" ", input), noColor, showHelp);
    }
}
