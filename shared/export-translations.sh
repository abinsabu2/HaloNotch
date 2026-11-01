#!/bin/bash
# Regenerates shared/translations.json from the Mac app's Translations.swift (single source of truth).
# Run after changing Translations.swift; the Windows/Linux app embeds the JSON.
set -euo pipefail
cd "$(dirname "$0")/.."

{ cat Sources/HaloNotch/Translations.swift; cat <<'SWIFT'
import Foundation
let out: [String: Any] = ["columns": Translations.columns, "table": Translations.table]
let data = try! JSONSerialization.data(withJSONObject: out, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
FileManager.default.createFile(atPath: "shared/translations.json", contents: data + Data("\n".utf8))
SWIFT
} | swift -

echo "Wrote shared/translations.json"
