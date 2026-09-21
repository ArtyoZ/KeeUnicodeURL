# KeeUnicodeURL — KeePass 2.x plugin

KeePass can store and display Unicode URLs, but Auto-Type and browser
integrations may fail to match an entry when the stored URL is Unicode.
`KeeUnicodeURL` therefore keeps the standard KeePass URL field in Punycode while
providing a convenient editable Unicode representation.

For example, the standard URL field can contain:

`https://xn--e1afmkfd.xn--p1ai/`

while the additional editor field shows:

`https://пример.рф/`

## What the plugin does

### 1. Unicode URL field in the entry editor

When the URL contains a Punycode host (`xn--...`), the plugin adds an
**URL (Unicode)** field directly below KeePass's standard URL field.

- The field is editable and is **not** read-only in normal entry editing.
- Changing the standard URL field updates the Unicode field automatically.
- Changing the Unicode field converts its host to Punycode and updates the
  standard URL field automatically.
- The standard URL field therefore remains suitable for Auto-Type and browser
  matching.
- The additional field is shown only when the URL contains a Punycode host.
- The Unicode field is a derived editor view; it is not stored as a separate
  database field.

The plugin changes only the host portion. Scheme, user information, port,
path, query, fragment and KeePass placeholders are preserved.

### 2. Unicode URL column

The plugin provides an additional main-window column named **URL (Unicode)**.
It displays the Unicode representation derived from the standard URL field.
The column does not modify or duplicate the stored URL value. Double-clicking
a cell in this column performs the same action as double-clicking the
built-in **URL** column: it opens the entry's real, stored (Punycode) URL
through KeePass's standard URL-opening pipeline — not the decoded display
string (see "KeePass integration" below for why).

### 3. KeeUnicodeURL Options

The plugin adds **KeeUnicodeURL Options** to the **Tools** menu. It contains two
checkable options:

- **Show Unicode URL field in the entry editor** — controls whether the
  editable `URL (Unicode)` field is shown below KeePass's standard URL field.
- **Show Unicode URL column in the entry list** — controls whether the
  `URL (Unicode)` column is registered and displayed in the main entry list.

Both options are enabled by default and their states are stored in KeePass's
plugin configuration.

For normal use, keeping the stored URL in Punycode is recommended when
Auto-Type or browser matching depends on the URL.

## URL conversion examples

- `https://xn--e1afmkfd.xn--p1ai/`
  → `https://пример.рф/`
- `https://пример.рф/`
  → `https://xn--e1afmkfd.xn--p1ai/`

Invalid Punycode labels are left unchanged.

## KeePass integration

The plugin uses reflection to access KeePass's private `PwEntryForm.m_tbUrl`
control. It adds the Unicode editor control to the General tab without
modifying KeePass itself.

The integration is fail-safe: if the expected KeePass controls are not found,
the plugin does not interfere with normal URL editing.

The Unicode URL column is registered through KeePass's `ColumnProviderPool`.
Double-clicking a cell in that column performs exactly the same action as
double-clicking the built-in **URL** column: it opens the entry's real,
stored (Punycode) URL through KeePass's own `WinUtil.OpenUrl`, which also
handles URL Overrides, `cmd://` entries and `{PLACEHOLDER}` expansion. It
does not open the decoded Unicode string directly, since that string is a
display-only convenience, not the value the standard column would use.

### Class naming convention

KeePass's documented plugin conventions
(`https://keepass.info/help/v2_dev/plg_index.html#conventions`) require the
main plugin class to be named `<Namespace>Ext` - so, given the `KeeUnicodeURL`
namespace/assembly name, the class itself must be `KeeUnicodeURLExt`. This is
not just a style preference: getting it wrong (together with a missing
`AssemblyProduct("KeePass Plugin")` value) is exactly what caused an earlier
build of this plugin to compile fine yet never appear in KeePass's plugin
list at all, with no error shown anywhere.

### Tools menu registration

The **KeeUnicodeURL Options** item is added to the **Tools** menu through the
official `Plugin.GetMenuItem(PluginMenuType.Main)` override, which KeePass
itself calls and uses to insert the returned menu item - no reflection into
`MainForm`'s private menu fields is used for this.

### Entry-editor row layout

The `URL (Unicode)` row's position is computed at runtime from the real URL
field's own `Bounds` (and its caption `Label`, located purely by its on-screen
position relative to the URL field - not by a private field name), rather
than from fixed pixel coordinates. This keeps the row correctly positioned
across different KeePass versions, DPI settings and font sizes. If the
surrounding layout does not match what this code expects, the row is simply
not added; the entry-edit dialog itself is never left in a broken state.

## Update checking and digital signature

The plugin implements KeePass's standard signed plugin update mechanism:

- `UpdateUrl` points to `KeeUnicodeURL-version.txt`;
- the version information is signed with RSA/SHA-512;
- the corresponding public RSA key is embedded in the plugin and registered
  through `UpdateCheckEx.SetFileSigKey`;
- the release PLGX is used by the EarlyUpdateCheck configuration;
- `ExternalPluginUpdates-KeeUnicodeURL.xml` contains the corresponding
  `PlgxDirect` entry.

The private signing key is **not** included in the source archive and must
never be committed to the public repository.

`KeeUnicodeURL-version.txt` uses the KeePass signed-version format with the
signature on the same line as the initial colon:

```text
:<base64-signature>
KeeUnicodeURL:1.0.0
:
```

There must be **no line break between the first `:` and the signature**.

## Files

- `KeeUnicodeURL.cs` — main plugin class, editor integration and Tools menu.
- `IdnUrlUtil.cs` — Punycode ↔ Unicode host conversion (parses the URL and
  delegates per-label encode/decode to `Punycode.cs`).
- `Punycode.cs` — self-contained RFC 3492 Punycode encoder/decoder, used
  instead of `System.Globalization.IdnMapping`. `IdnMapping` additionally
  enforces IDNA registration-policy checks (Std3 ASCII rules, the Bidi rule,
  Unicode character allow-lists) on top of plain Punycode, and throws for a
  number of real, registered, resolvable domain names as a result — a
  documented .NET behaviour (see dotnet/runtime issues #107900, #72962,
  #110499), not something specific to this plugin.
- `UnicodeUrlColumnProvider.cs` — `URL (Unicode)` main-window column.
- `Properties/AssemblyInfo.cs` — plugin metadata and version.
- `KeeUnicodeURL.csproj` — .NET Framework 4.0 / C# 5 project.
- `Build_KeeUnicodeURL.ps1` — DLL/PLGX build and release packaging.
- `Sign-UpdateInfo.ps1` — RSA/SHA-512 signing helper.
- `KeeUnicodeURL-version.txt` — signed update information.
- `KeeUnicodeURL-update-public.xml` — public signing key.
- `ExternalPluginUpdates-KeeUnicodeURL.xml` — EarlyUpdateCheck configuration.

## Building

Use the official KeePass 2.61.1 `KeePass.exe` as the assembly reference.
The project targets .NET Framework 4.0 and C# 5 because KeePass's PLGX compiler
uses the legacy CodeDom compiler.

Build the DLL with:

```text
msbuild KeeUnicodeURL.csproj /p:Configuration=Release /p:KeePassDir="C:\Program Files\KeePass Password Safe 2"
```

Or run:

```text
Build_KeeUnicodeURL.ps1
```

The build script can create:

- `release\KeeUnicodeURL.dll`
- `release\KeeUnicodeURL.plgx`
- `release\KeeUnicodeURL-version.txt`
- `release\ExternalPluginUpdates-KeeUnicodeURL.xml`
- `release\KeeUnicodeURL-X.Y.Z-source.zip`

The PLGX build staging directory contains exactly one project file,
`KeeUnicodeURL.csproj`, as required by KeePass's PLGX project loader.

## Installation

Copy either `KeeUnicodeURL.dll` or `KeeUnicodeURL.plgx` to KeePass's `Plugins`
directory and restart KeePass.

If both are present, remove the duplicate before testing so that only one
copy of the plugin is loaded.

For automatic installation/update through EarlyUpdateCheck, the corresponding
`ExternalPluginUpdates-KeeUnicodeURL.xml` entry must be added to the
EarlyUpdateCheck configuration.

## Versioning

`AssemblyInfo.cs` is the authoritative source for the assembly/file version.
For example:

- assembly/file version: `1.0.0.0`
- public update version: `1.0.0`

The `KeeUnicodeURL-version.txt` record must contain the matching three-component
version.
