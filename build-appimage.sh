#!/usr/bin/env bash

set -euo pipefail

# ============================================================
# Configurazione
# ============================================================

APP_NAME="Sbroglione"
PROJECT="Sbroglione.Desktop"
RUNTIME="linux-x64"
CONFIGURATION="Release"

APPDIR="${APP_NAME}.AppDir"
PUBLISH_DIR="${APP_NAME}.publish"
APPIMAGE="${APP_NAME}-x86_64.AppImage"

# ============================================================
# Funzioni
# ============================================================

cleanup() {
    echo
    echo "Pulizia dei file temporanei..."

    rm -rf "$APPDIR"
    rm -rf "$PUBLISH_DIR"

    echo "Pulizia completata."
}

error_exit() {
    echo
    echo "ERRORE: $1"
    echo
    cleanup
    exit 1
}

# ============================================================
# Controllo prerequisiti
# ============================================================

echo "========================================"
echo "   Build AppImage - $APP_NAME"
echo "========================================"
echo

if ! command -v dotnet >/dev/null 2>&1; then
    error_exit "dotnet non trovato."
fi

if ! command -v appimagetool >/dev/null 2>&1; then
    error_exit "appimagetool non trovato nel PATH."
fi

if ! command -v convert >/dev/null 2>&1; then
    error_exit "ImageMagick non trovato. Installalo con: sudo pacman -S imagemagick"
fi

# ============================================================
# Pulizia iniziale
# ============================================================

echo "[1/7] Pulizia precedente..."

rm -rf "$APPDIR"
rm -rf "$PUBLISH_DIR"
rm -f "$APPIMAGE"

# ============================================================
# Publish .NET
# ============================================================

echo "[2/7] Pubblicazione .NET..."

dotnet publish "$PROJECT" \
    -c "$CONFIGURATION" \
    -r "$RUNTIME" \
    --self-contained true \
    -o "$PUBLISH_DIR"

# ============================================================
# Creazione AppDir
# ============================================================

echo "[3/7] Creazione AppDir..."

mkdir -p \
    "$APPDIR/usr/bin" \
    "$APPDIR/usr/share/applications" \
    "$APPDIR/usr/share/icons/hicolor/256x256/apps"

# ============================================================
# Copia applicazione
# ============================================================

echo "[4/7] Copia file pubblicati..."

cp -a "$PUBLISH_DIR"/. "$APPDIR/usr/bin/"

# ============================================================
# AppRun
# ============================================================

cat > "$APPDIR/AppRun" <<'EOF'
#!/usr/bin/env bash

HERE="$(dirname "$(readlink -f "$0")")"

exec "$HERE/usr/bin/Sbroglione.Desktop" "$@"
EOF

chmod +x "$APPDIR/AppRun"

# ============================================================
# Icona
# ============================================================

echo "[5/7] Creazione icona..."

# Crea una semplice icona 256x256.
# Puoi sostituirla successivamente con quella reale di Sbroglione.

convert \
    -size 256x256 \
    xc:"#3584e4" \
    -gravity center \
    -fill white \
    -pointsize 96 \
    -font DejaVu-Sans-Bold \
    -annotate +0+0 "S" \
    "$APPDIR/Sbroglione.png"

# Copia l'icona anche nella struttura standard
cp "$APPDIR/Sbroglione.png" \
   "$APPDIR/usr/share/icons/hicolor/256x256/apps/Sbroglione.png"

# ============================================================
# Desktop Entry
# ============================================================

cat > "$APPDIR/Sbroglione.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Sbroglione
Exec=Sbroglione.Desktop
Icon=Sbroglione
Categories=Utility;
Terminal=false
EOF

cp "$APPDIR/Sbroglione.desktop" \
   "$APPDIR/usr/share/applications/Sbroglione.desktop"

# ============================================================
# Creazione AppImage
# ============================================================

echo "[6/7] Creazione AppImage..."

appimagetool \
    "$APPDIR" \
    "$APPIMAGE"

# ============================================================
# Verifica
# ============================================================

if [[ ! -f "$APPIMAGE" ]]; then
    error_exit "appimagetool non ha creato $APPIMAGE"
fi

chmod +x "$APPIMAGE"

echo "[7/7] Verifica completata."

# ============================================================
# Pulizia finale
# ============================================================

cleanup

# ============================================================
# Risultato
# ============================================================

echo
echo "========================================"
echo "   BUILD COMPLETATA"
echo "========================================"
echo
echo "AppImage:"
echo "  $(pwd)/$APPIMAGE"
echo
echo "Dimensione:"
ls -lh "$APPIMAGE"
echo
echo "Per avviarla:"
echo "  ./$APPIMAGE"
echo