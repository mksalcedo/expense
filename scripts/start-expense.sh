#!/usr/bin/env bash
# Starts the Expense server (if not already running), opens it in a chromeless Chrome
# app window at its last known position/size, and shuts the server down automatically
# once the window is closed - so closing the window is the only shutdown step needed.
set -uo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
URL="http://127.0.0.1:5266"
PROFILE_DIR="$HOME/.local/share/expense-chrome-profile"
GEOMETRY_FILE="$HOME/.local/share/expense-chrome-window.geometry"

SERVER_PID=""
MONITOR_PID=""

cleanup() {
    if [ -n "$MONITOR_PID" ]; then
        kill "$MONITOR_PID" 2>/dev/null
    fi
    if [ -n "$SERVER_PID" ]; then
        echo "Shutting down Expense server (pid $SERVER_PID)..."
        kill "$SERVER_PID" 2>/dev/null
        wait "$SERVER_PID" 2>/dev/null
    fi
}
trap cleanup EXIT INT TERM

if curl -s -o /dev/null "$URL"; then
    echo "Expense server already running at $URL - reusing it."
else
    echo "Starting Expense server..."
    (cd "$PROJECT_DIR" && dotnet run --project src/Expense.Web --urls "$URL") &
    SERVER_PID=$!

    echo "Waiting for the server to come up..."
    for _ in $(seq 1 60); do
        if curl -s -o /dev/null "$URL"; then
            break
        fi
        sleep 1
    done

    if ! curl -s -o /dev/null "$URL"; then
        echo "Server did not come up in time - check the output above for errors." >&2
        exit 1
    fi
fi

mkdir -p "$PROFILE_DIR"

# Chrome remembers its own per-app window placement internally (Default/Preferences ->
# browser.app_window_placement) and reasserts it on every launch, so it has to be
# cleared before each launch or it fights with the position we set further down.
if command -v jq >/dev/null 2>&1 && [ -f "$PROFILE_DIR/Default/Preferences" ]; then
    jq 'del(.browser.app_window_placement)' "$PROFILE_DIR/Default/Preferences" > "$PROFILE_DIR/Default/Preferences.tmp" \
        && mv "$PROFILE_DIR/Default/Preferences.tmp" "$PROFILE_DIR/Default/Preferences"
fi

# Reuse last-known window position/size, if we've saved one before.
if [ -f "$GEOMETRY_FILE" ]; then
    # shellcheck source=/dev/null
    source "$GEOMETRY_FILE"
fi

echo "Opening Expense in its own Chrome app window..."
google-chrome --app="$URL" --user-data-dir="$PROFILE_DIR" --no-first-run --no-default-browser-check &
CHROME_PID=$!

# Find the window xdotool belongs to this Chrome process, then:
#   1) correct its size via xdotool windowsize,
#   2) correct its position via xdotool windowmove - Chrome's own --window-position flag
#      does not reliably honor the Y coordinate for --app mode windows on this system, so
#      position has to be fixed up after the fact rather than passed at launch,
#   3) keep polling its geometry every second while it's open, only ever persisting a
#      reading once it's matched the previous poll (stable for a full second) - a one-off
#      reading is never trusted, since the window manager's close animation can otherwise
#      get caught and saved as if it were the real final position.
if command -v xdotool >/dev/null 2>&1; then
    (
        WINDOW_ID=""
        for _ in $(seq 1 20); do
            WINDOW_ID=$(xdotool search --onlyvisible --pid "$CHROME_PID" 2>/dev/null | head -1)
            [ -n "$WINDOW_ID" ] && break
            sleep 0.5
        done

        if [ -n "$WINDOW_ID" ]; then
            if [ -n "${WIDTH:-}" ] && [ -n "${HEIGHT:-}" ]; then
                xdotool windowsize "$WINDOW_ID" "$WIDTH" "$HEIGHT"
            fi
            if [ -n "${X:-}" ] && [ -n "${Y:-}" ]; then
                xdotool windowmove "$WINDOW_ID" "$X" "$Y"
            fi

            PREVIOUS_READING=""
            while kill -0 "$CHROME_PID" 2>/dev/null; do
                CURRENT_READING=$(xdotool getwindowgeometry --shell "$WINDOW_ID" 2>/dev/null)
                if [ -n "$CURRENT_READING" ] && [ "$CURRENT_READING" = "$PREVIOUS_READING" ]; then
                    echo "$CURRENT_READING" | grep -v '^WINDOW=' > "$GEOMETRY_FILE.tmp" && mv "$GEOMETRY_FILE.tmp" "$GEOMETRY_FILE"
                fi
                PREVIOUS_READING="$CURRENT_READING"
                sleep 1
            done
        fi
    ) &
    MONITOR_PID=$!
fi

wait "$CHROME_PID"

echo "Window closed."
