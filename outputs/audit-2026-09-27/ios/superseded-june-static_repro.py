"""Read-only iOS source characterization; no Swift/iOS runtime is executed.

Runs only Python stdlib, reads audited sources, uses an in-memory SQLite fixture.
Output is evidence of source/SQL behavior, not StoreKit/Sandbox execution.
"""
import json
import plistlib
import re
import sqlite3
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(r"D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios")
OUT = Path(__file__).parent
policy = (ROOT / "Domain/Entitlement/FreeLimitPolicy.swift").read_text(encoding="utf-8")
store = (ROOT / "Infrastructure/Purchases/EntitlementStore.swift").read_text(encoding="utf-8")
purchase = (ROOT / "Infrastructure/Purchases/StoreKitService.swift").read_text(encoding="utf-8")
repository = (ROOT / "Infrastructure/Repositories/WordRepository.swift").read_text(encoding="utf-8")
api = (ROOT / "Infrastructure/Networking/APIClient.swift").read_text(encoding="utf-8")
configuration = (ROOT / "Infrastructure/Networking/APIConfiguration.swift").read_text(encoding="utf-8")
swift_files = sorted(ROOT.rglob("*.swift"))
all_swift = "\n".join(p.read_text(encoding="utf-8") for p in swift_files)
observations = []

def record(name, **values):
    observations.append({"observation": name, **values})

def function(source, name):
    start = source.index("func " + name + "(")
    brace = source.index("{", start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]

assert not re.search(r"TestMode|test_mode", all_swift, re.I)
record("No client TestMode/test_mode references", swift_file_count=len(swift_files))

ai_policy = function(policy, "canUseAI")
active = re.search(r'case ("trial".*?):\s*return true', ai_policy).group(1)
active_statuses = re.findall(r'"([^"]+)"', active)
assert active_statuses == ["trial", "premium", "grace"]
assert "expiresAt" not in policy
record("Local unlimited policy includes trial/grace and ignores dates", statuses=active_statuses,
       supplied_expired_date_changes_result=False)

refresh = function(purchase, "refreshTokenAndEntitlement")
failure = refresh[refresh.index("} catch {"):]
assert "update(" not in failure and "clear(" not in failure
assert "stored.status" in function(store, "currentStatusSnapshot")
assert "hasOfflineReadGrace" not in all_swift.replace("var hasOfflineReadGrace", "var DEFINITION_ONLY")
record("Failed refresh preserves old snapshot; offline grace is not consulted",
       past_expiry_premium_remains_premium=True, grace_has_callers=False)

word_detail = function(repository, "wordDetail")
assert word_detail.index("return cached") < word_detail.index("FreeLimitPolicy.canUseAI")
record("Local cached detail returns before entitlement checks", network_cost=False,
       cached_locked_word_is_readable=True)

restore = function(purchase, "restore")
assert "var restored = false" in restore and "return restored" in restore
assert "update(" not in restore and "clear(" not in restore
record("Restore with no current entitlements retains prior snapshot", returns_success=False,
       clears_snapshot=False)

counter = function(repository, "totalWordCount")
sql = re.search(r'let sql = """\s*(.*?)\s*"""', counter, re.S).group(1)
db = sqlite3.connect(":memory:")
db.execute("CREATE TABLE words (id TEXT, native_language TEXT, learning_language TEXT, created_at REAL)")
db.executemany("INSERT INTO words VALUES (?, 'en', 'es', ?)", [(f"old-{i}", i) for i in range(10)])
db.execute("INSERT INTO words VALUES ('eleventh-other-pair','en','de',10)")
counts = {"en/es": db.execute(sql, ("en", "es")).fetchone()[0],
          "en/de": db.execute(sql, ("en", "de")).fetchone()[0],
          "global": db.execute("SELECT COUNT(*) FROM words").fetchone()[0]}
assert counts == {"en/es": 10, "en/de": 1, "global": 11}
record("Actual count SQL is per language pair", counts=counts,
       second_pair_local_ai_gate_allowed=counts["en/de"] < 10)

lock_sql = "SELECT id FROM words ORDER BY created_at ASC, id ASC LIMIT -1 OFFSET 10"
before = [r[0] for r in db.execute(lock_sql)]
assert before == ["eleventh-other-pair"]
db.execute("DELETE FROM words WHERE id='old-0'")
after = [r[0] for r in db.execute(lock_sql)]
assert after == []
record("Deleting an older row promotes previously locked row", before_locked=before, after_locked=after,
       server_ai_grant_proven=False)

edit = function(repository, "updateWordCard")
assert "normalized_word = ?" in edit
assert "deviceWordUpsert" not in edit and "syncDeviceWordUpsert" not in edit
record("Edit changes local identity without device-word synchronization", server_identity_updated=False)

assert "#if !DEBUG\n            guard Self.hasRequiredIntegrityHeaders(extra)" in api
assert "#if DEBUG" in configuration and "return nil" in configuration
plistlib.loads((ROOT / "Resources/Info.plist").read_bytes())
plistlib.loads((ROOT / "Resources/FlashCardAI.entitlements").read_bytes())
json.loads((ROOT / "Resources/Owl AI.storekit").read_text(encoding="utf-8"))
scheme = ET.parse(ROOT / "FlashCardAI.xcodeproj/xcshareddata/xcschemes/FlashCardAI.xcscheme").getroot()
assert scheme.find("ArchiveAction").attrib["buildConfiguration"] == "Release"
record("Release source guards require bearer/proof and archive uses Release",
       archive_configuration="Release", source_format_checks="valid plist/entitlements/StoreKit JSON/scheme XML")

result = {"kind": "source-characterization-and-sql-reproduction", "ios_runtime_executed": False,
          "observations": observations}
(OUT / "static-repro-results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result, indent=2))

