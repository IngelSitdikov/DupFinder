# Duplicate Finder (DupFinder)

**English** · [Русский](README.ru.md)

A program for Windows and macOS that finds identical files — on one drive or across two drives —
and helps you free up space: move the extra copies to the Recycle Bin (Trash) or turn them into hard
links.

Files count as identical when their **content** matches; name, folder and date don't matter.

The interface is available in English and Russian. By default it follows the system display
language; to change it, use the language list at the end of the options row (the choice applies
after a restart, which the program offers to do).

## Running

The ready-made program is a single file, `DupFinder.App.exe` (Windows 10/11, 64-bit). No installer
and no .NET runtime needed: copy it anywhere and run it. The first launch is a bit slower — the exe
unpacks its libraries into a temporary folder.

On macOS (13 or later) the program is `DupFinder.app`, packed as `DupFinder-mac-arm64.tar.gz` for
Apple Silicon (M1 and newer) and `DupFinder-mac-x64.tar.gz` for Intel Macs. Unpack it (double-click)
and move `DupFinder.app` to Applications. The app is not signed with an Apple developer certificate,
so the first launch is blocked: open **System Settings → Privacy & Security** and click **Open
Anyway**, or run once in Terminal:

```
xattr -dr com.apple.quarantine /Applications/DupFinder.app
```

macOS will ask for access to Documents, Desktop, Downloads and external drives the first time they
are scanned. To scan the whole disk, add DupFinder under **Full Disk Access** in the same settings
pane.

## How to use

1. **Drive A** — pick a drive or folder with **Browse…**.
2. **Drive B** — optional:
   - left empty — duplicates are searched within A;
   - set — A and B are compared. With **Only files present on both A and B** (on by default) only
     shared files are shown; with it off — every duplicate on either drive.
3. **At least … KB** — smaller files are skipped. Empty files are never considered.
4. **Find duplicates.** Scanning can be interrupted with **Stop**.
5. Results are shown in groups, the ones that free the most space first. The A/B badge on a file
   tells which drive it is on. Double-click a file to show it in Explorer.
6. **Mark what to get rid of** — with checkboxes or with the buttons:
   - **Keep the oldest** — each group keeps the file with the earliest modification date;
   - **Keep on A** / **Keep on B** — copies on the other drive are marked.
7. **What to do with the marked files:**
   - **Move to Recycle Bin** — the files can be restored from there;
   - **Replace with hard links** — see below.
8. **CSV report…** — a list of all groups for Excel (the separator follows your regional settings).

Anything that couldn't be read or deleted, with the reason, is listed on the **Errors** tab.

## Replacing with hard links

The marked file stays where it is and opens as before, but becomes a hard link to the copy being
kept: the data is stored on disk only once. Useful when a file is needed in several folders but the
space isn't.

- Works on NTFS (Windows) and APFS / HFS+ (macOS), and only within **one** drive: a file on B can't become a link to a file on A.
  Such files are skipped, with an explanation on the Errors tab.
- After the replacement it is **the same file** under different names: change one and all of them
  change. For a photo or video archive that's usually what you want; for documents you edit — not.

## Protection against data loss

- You can't mark every copy in a group — the program will refuse to do anything.
- Before deleting or replacing, it checks that the file hasn't changed since the scan.
- **Verify byte by byte** (on by default) — before deleting, the file is compared
  with the copy being kept, byte for byte. Slower, but doesn't rely on the hash. Replacing with hard
  links always verifies.
- USB flash drives and memory cards have no Recycle Bin: files there are deleted permanently, and the
  confirmation dialog warns about it separately.

## Hash cache

So that rescanning the same drive takes seconds rather than hours, the program remembers file hashes
in `%LOCALAPPDATA%\DupFinder\hashcache.db` (on macOS —
`~/Library/Application Support/DupFinder/hashcache.db`). A file is re-read if its size or date has changed.
Entries for files not seen for 180 days are removed automatically. The cache is turned off with
**Hash cache** and cleared with **Clear**. The interface language is stored next to it, in
`settings.json`.

## How the search works

To avoid reading every file in full, checks go from cheap to expensive:
size → hash of the first and last 64 KB → full hash (XxHash128). Most files are ruled out by size
alone.

Each drive is read by a single thread — on an HDD parallel reads just make the head jump around and
slow things down; two different drives are read at the same time. Hard links to the same file are
not counted as duplicates, junctions and symlinks are not followed, system folders (`$Recycle.Bin`,
`System Volume Information`, `.Trashes`, `.Spotlight-V100` and the like) are skipped.

## Building from source

Requires the .NET 10 SDK.

```
dotnet run --project src/DupFinder.App                        # run
dotnet test                                                   # tests
dotnet publish src/DupFinder.App -p:PublishProfile=SingleFile # single exe → publish/DupFinder.App.exe
dotnet run --project src/DupFinder.Mac                        # the Avalonia (macOS) window, runs on Windows too
dotnet run tools/publish-mac.cs                               # DupFinder.app → publish/DupFinder-mac-*.tar.gz
```

The macOS build can be made on Windows: the archive keeps the executable bit, and the SDK gives the
executable an ad-hoc signature.

| Project | Contents |
|---|---|
| `src/DupFinder.Core` | search, hashing, cache, verification and hard links — no UI |
| `src/DupFinder.Presentation` | view model, settings and strings shared by both windows (CommunityToolkit.Mvvm) |
| `src/DupFinder.App` | Windows window (WPF) |
| `src/DupFinder.Mac` | macOS window (Avalonia) |
| `tests/DupFinder.Tests` | xUnit tests on temporary directories |
