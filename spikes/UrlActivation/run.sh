#!/bin/zsh
# Spike 1 harness: launch the bundled app N times via `open -a <app> <url>` (cold start every time,
# because the app terminates after receiving the URL) and print the timing log it wrote.
set -eu
ROOT="${0:A:h}/../.."
APP="$ROOT/artifacts/spikes/UrlActivation.app"
LOG="$HOME/Library/Logs/PreferisSpike/urlactivation.log"
N="${1:-5}"

if [[ ! -d "$APP" ]]; then
  echo "bundle not found: $APP (run: dotnet publish spikes/UrlActivation)" >&2
  exit 1
fi

rm -f "$LOG"
for i in $(seq 1 "$N"); do
  issued=$(perl -MTime::HiRes=time -e 'printf("%.3f", time)')
  open -a "$APP" "https://example.com/spike/$i"
  # Wait for the process to finish (it terminates itself after logging the URL, or after the watchdog fires).
  for _ in $(seq 1 100); do
    pgrep -xq UrlActivation || break
    sleep 0.05
  done
  echo "open-issued    $issued  (iteration $i)"
done

echo "----- $LOG -----"
cat "$LOG"
