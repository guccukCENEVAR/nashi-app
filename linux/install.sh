#!/usr/bin/env bash
# Builds Nashi from this checkout and installs it for the current user (no root needed).
#   linux/install.sh              build and install into ~/.local
#   linux/install.sh --uninstall  remove it again (Library and settings in ~/.local/share/Nashi are kept)
# PREFIX overrides the destination, for example PREFIX=/opt/nashi-user.
set -euo pipefail

prefix="${PREFIX:-$HOME/.local}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
app="$prefix/lib/nashi"
launcher="$prefix/bin/nashi"
desktop="$prefix/share/applications/nashi.desktop"
icons="$prefix/share/icons/hicolor"
sizes=(32 48 64 128 256 512)

refresh_desktop() {
  command -v update-desktop-database >/dev/null && update-desktop-database -q "$prefix/share/applications" || true
}

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -rf "$app"
  rm -f "$launcher" "$desktop"
  for size in "${sizes[@]}"; do rm -f "$icons/${size}x${size}/apps/nashi.png"; done
  refresh_desktop
  echo "Nashi removed. Your Library and settings are still in ~/.local/share/Nashi."
  exit 0
elif [[ $# -gt 0 ]]; then
  sed -n '2,5p' "$0" | sed 's/^# \{0,1\}//'
  exit 64
fi

if ! command -v dotnet >/dev/null || ! dotnet --list-sdks | grep -q '^10\.'; then
  echo "The .NET 10 SDK is needed to build Nashi. On CachyOS / Arch: sudo pacman -S dotnet-sdk" >&2
  exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
echo "Building Nashi…"
dotnet publish "$root/linux/Nashi.Linux" -c Release -p:RestoreLockedMode=true \
  --artifacts-path "$work/artifacts" -o "$work/publish" -nologo -v quiet
"$work/publish/Nashi" --version >/dev/null

# Replace the previous install only after a successful build.
mkdir -p "$prefix/lib" "$prefix/bin" "$(dirname "$desktop")"
rm -rf "$app.new"
cp -a "$work/publish" "$app.new"
rm -rf "$app"
mv "$app.new" "$app"
ln -sfn "$app/Nashi" "$launcher"

# Desktop sessions often lack ~/.local/bin on PATH, so the menu entry uses the absolute launcher path.
sed "s|^Exec=nashi|Exec=\"$launcher\"|" "$root/linux/packaging/nashi.desktop" >"$desktop"
for size in "${sizes[@]}"; do
  install -Dm644 "$root/linux/packaging/icons/nashi-$size.png" "$icons/${size}x${size}/apps/nashi.png"
done
refresh_desktop
echo "Installed $("$launcher" --version) to $app"

missing=()
for tool in ffmpeg:ffmpeg ffprobe:ffmpeg yt-dlp:yt-dlp deno:deno exiftool:perl-image-exiftool soffice:libreoffice-fresh tesseract:tesseract; do
  command -v "${tool%%:*}" >/dev/null || missing+=("${tool#*:}")
done
command -v python3 >/dev/null && python3 -c 'import cv2' 2>/dev/null || missing+=(python-opencv)
if ((${#missing[@]})); then
  readarray -t missing < <(printf '%s\n' "${missing[@]}" | sort -u)
  [[ " ${missing[*]} " == *" tesseract "* ]] && missing+=(tesseract-data-eng tesseract-data-tur)
  echo
  echo "Some tools are missing. For every feature on CachyOS / Arch, run:"
  echo "  sudo pacman -S --needed ${missing[*]}"
fi
[[ ":$PATH:" == *":$prefix/bin:"* ]] || echo "Note: $prefix/bin is not on your PATH; the app menu entry works either way."
