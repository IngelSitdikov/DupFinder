# Duplicate Finder (DupFinder)

**English** · [Русский](README.ru.md)

A Windows program that finds identical files — on one drive or across two drives — and helps you
free up space: move the extra copies to the Recycle Bin or turn them into hard links.

Files count as identical when their **content** matches; name, folder and date don't matter.

The interface is available in English and Russian. By default it follows the Windows display
language; to change it, use the language list at the end of the options row (the choice applies
after a restart, which the program offers to do).

## Running

The ready-made program is a single file, `DupFinder.App.exe` (Windows 10/11, 64-bit). No installer
and no .NET runtime needed: copy it anywhere and run it. The first launch is a bit slower — the exe
unpacks its libraries into a temporary folder.

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

- Works only on NTFS and only within **one** drive: a file on B can't become a link to a file on A.
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
in `%LOCALAPPDATA%\DupFinder\hashcache.db`. A file is re-read if its size or date has changed.
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
`System Volume Information`) are skipped.

## Building from source

Requires the .NET 10 SDK.

```
dotnet run --project src/DupFinder.App                        # run
dotnet test                                                   # tests
dotnet publish src/DupFinder.App -p:PublishProfile=SingleFile # single exe → publish/DupFinder.App.exe
```

| Project | Contents |
|---|---|
| `src/DupFinder.Core` | search, hashing, cache, verification and hard links — no UI |
| `src/DupFinder.App` | WPF window (MVVM, CommunityToolkit.Mvvm) |
| `tests/DupFinder.Tests` | xUnit tests on temporary directories |

The original task description (in Russian) is in `docs/Описание.txt`.
