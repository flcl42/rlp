# rlp

Error-tolerant RLP parser and colored hex inspector.

`rlp` accepts `0x`-prefixed or plain hex, recovers from malformed hex and
malformed RLP lengths, and still prints a useful parse tree. The encoded input is
shown first with marker/value highlighting and inline caret diagnostics.

## Install

Release assets are unpacked, self-contained single-file executables.

Linux, bash:

```bash
repo=flcl42/rlp; arch="$(uname -m)"; asset=rlp-linux-x64; case "$arch" in aarch64|arm64) asset=rlp-linux-arm64;; esac; curl -fsSL "https://github.com/$repo/releases/latest/download/$asset" -o ./rlp; chmod +x ./rlp
```

macOS, zsh:

```zsh
repo=flcl42/rlp; arch="$(uname -m)"; asset=rlp-macos-arm64; [ "$arch" = "x86_64" ] && asset=rlp-macos-x64; curl -fsSL "https://github.com/$repo/releases/latest/download/$asset" -o ./rlp; chmod +x ./rlp
```

Windows, PowerShell:

```powershell
$repo='flcl42/rlp'; Invoke-WebRequest "https://github.com/$repo/releases/latest/download/rlp-windows-x64.exe" -OutFile ".\rlp.exe"
```

## Usage

```powershell
rlp 83636174
rlp 0x83636174
Get-Content input.hex | rlp
rlp --no-color 8g0
```

Example:

```text
Encoded
     list marker byte marker value1 value2 recovered/error byte
     83636174

Summary
  decoded bytes: 4
  top-level items: 1
  status: ok

Parsed
  stream bytes=4 top-level-items=1
  [0]     @0000..0004   value 0x636174 (length=3 + prefix=1 => total=4)
          ascii "cat"
```

## Output

The encoded line is wrapped to the console width minus ten columns and padded by
five spaces on the left. RLP list markers, byte-string markers, values, and
recovered/error bytes are colored separately. Adjacent values alternate between
two green shades so separate byte strings remain visible.

Diagnostics are printed directly under the encoded bytes they refer to:

```text
     8000
     ^~ error HEX001 char 2: Invalid hex symbol 'g'. It is interpreted as nibble 0 so parsing can continue.
       ^~ error HEX002 char 3: Odd number of hex digits. The final byte is missing its low nibble; 0 is used for recovery.
```

The parsed tree prints full values, not previews. Lists show element count and
payload/prefix/total byte counts. Values show full hex, printable ASCII, and
length/prefix/total byte counts.

## Build

On Windows, regular builds write the testable command to the current directory:

```powershell
dotnet build
.\rlp.exe 83636174
```

To publish a local self-contained executable:

```powershell
dotnet publish .\RlpTool.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\win-x64
```

## Release

Tagged commits build and publish these raw executable assets:

- `rlp-linux-x64`
- `rlp-linux-arm64`
- `rlp-windows-x64.exe`
- `rlp-windows-arm64.exe`
- `rlp-macos-x64`
- `rlp-macos-arm64`

Push a tag such as `v1.0.0` or `release/1.0.0` to create a GitHub release:

```powershell
git tag release/1.0.0
git push origin release/1.0.0
```

## License

MIT.
