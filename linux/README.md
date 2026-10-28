# Nashi for Linux

A native Linux port of Nashi that follows the macOS app: the same pear artwork, file garden, rounded cards, gold actions, light/dark palette, wheel, editors, Library and Float shelves. It shares the .NET engine (`windows/Nashi.Core`) and the 27-tool / 45-format catalog with Windows. The UI uses [Avalonia](https://avaloniaui.net/) on X11; Wayland desktops run it through XWayland.

Tested on Arch-based systems such as CachyOS with KDE Plasma and GNOME.

## Install on CachyOS / Arch

Both methods build from this checkout and need the .NET 10 SDK only while building. The app is self-contained, so no .NET runtime is needed afterwards.

**As a pacman package** (recommended: updates and removal go through pacman):

```sh
sudo pacman -S --needed git dotnet-sdk
git clone https://github.com/orkunisitmak/nashi-app.git
cd nashi-app/linux/packaging
makepkg -si
```

Then install the optional tools for every feature:

```sh
sudo pacman -S --needed libreoffice-fresh tesseract tesseract-data-eng tesseract-data-tur python-opencv
```

**For your user only** (no root needed for Nashi itself; installs into `~/.local`):

```sh
sudo pacman -S --needed dotnet-sdk
linux/install.sh
```

The script builds, replaces any previous copy only after a successful build, adds Nashi to the app menu and prints a `pacman` command for any tools that are still missing. `linux/install.sh --uninstall` removes it. To update, `git pull` and run the same command again (or `makepkg -si` for the package).

## Tools

Nashi uses the copies installed by your package manager and never downloads executables itself. The **Finish setup** / **Tools ready** button at the top of Home shows what is ready and can install missing groups through `pkexec pacman`.

| Feature | Package |
| --- | --- |
| Video, audio and GIF conversion, trimming, previews | `ffmpeg` |
| Public web media downloads | `yt-dlp`, `deno` |
| Metadata view and editing | `perl-image-exiftool` |
| Office, Pages, EPUB and spreadsheet conversion and previews | `libreoffice-fresh` (or `libreoffice-still`) |
| Text recognition for redaction and searchable PDFs | `tesseract`, `tesseract-data-eng`, `tesseract-data-tur` |
| Face detection for photo redaction | `python-opencv` |
| Share to phone | `kdeconnect` (optional; email sharing uses `xdg-email`) |

Images, PDFs, QR codes, archives and subtitles work without any of them.

## Using it

Drop or paste files on Home, or launch with files: `nashi photo.heic report.pdf`. A second launch hands its files to the running window. Nashi also appears in the file manager's **Open with** menu for every supported type.

| Shortcut | Action |
| --- | --- |
| Shift / Ctrl / Alt while dragging files | Conversion / tools / file-action wheel (changeable in Settings) |
| Ctrl+Shift+D | Download a copied public link |
| Ctrl+O, Ctrl+V | Choose files, import copied files, text, images or links |
| Ctrl+Space | Tools wheel for the selected files |
| Ctrl+1 / Ctrl+2, Ctrl+F | Home / Library, search |
| Ctrl+, , Ctrl+U | Settings, check for updates |
| Space, Left/Right, I/O, Enter | Media play/pause, seek, range marks, save |

### X11 and Wayland

On an **X11** session it works as on macOS: start dragging files in Dolphin, Nautilus or any other app, then hold Shift, Ctrl or Alt and the wheel opens under the pointer. Switch keys mid-drag to switch wheels, and release over a petal. Ctrl+Shift+D works everywhere.

**Wayland** keeps other apps' drags and keystrokes private by design, so no app can open a wheel in the middle of someone else's drag. Drag the files onto the Nashi window instead: **Convert**, **Tools** and **File actions** drop zones appear, and dropping on one opens that wheel. Dropping anywhere else adds the files. For the macOS-style wheel everywhere on KDE Plasma, install `plasma-x11-session` and choose **Plasma (X11)** on the login screen. For the link shortcut, add a custom shortcut in your desktop settings that runs `nashi --open-copied-link`. The app menu entry also has a **Download copied link** action.

Everything else is identical on both. Window snapping is left to KDE/GNOME tiling.

## Where things live

- **App:** `/usr/lib/nashi` (package) or `~/.local/lib/nashi` (script).
- **Library, settings and clipboard imports:** `~/.local/share/Nashi`. The Library keeps the last 50 exports.
- **Start at login:** `~/.config/autostart/nashi.desktop`, created only when you turn it on.

Updates are checked against GitHub releases; installing them stays with you (`git pull` + rebuild). There are no analytics.

## Development

```sh
dotnet build linux/Nashi.Linux -c Release
dotnet run --project windows/Nashi.Verify -c Release          # shared engine checks
linux/Nashi.Linux/bin/Release/net10.0/linux-x64/Nashi --smoke-test   # real UI, clipboard, editor and export checks
linux/Nashi.Linux/bin/Release/net10.0/linux-x64/Nashi --ui-audit /tmp/nashi-audit   # renders every window, light/dark × en/tr
```

The smoke test and UI audit need a display; on a headless machine run them under `Xvfb`. `NASHI_SMOKE_DIRECTORY` keeps the smoke test's JSON report and fixtures.

Third-party licenses: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
