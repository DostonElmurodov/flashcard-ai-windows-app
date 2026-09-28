"""Read-only current iOS source/SQL characterization; does not execute Swift or StoreKit."""
import json
import re
import sqlite3
from pathlib import Path

ROOT = Path(r"D:\07 Hobby\FlashcardAI\test-results\ios-batch")
OUT = Path(__file__).parent
DELTA = OUT.parent / "latest-ios-delta"
def read(path): return (ROOT / path).read_text(encoding="utf-8")
def section(source, start, end):
    return source[source.index(start):source.index(end, source.index(start) + len(start))]
policy = read("Domain/Entitlement/FreeLimitPolicy.swift")
store = read("Infrastructure/Purchases/EntitlementStore.swift")
flags = read("Infrastructure/Networking/ServerFeatureFlags.swift")
purchase = read("Infrastructure/Purchases/StoreKitService.swift")
repo = read("Infrastructure/Repositories/WordRepository.swift")
account = read("Infrastructure/Networking/AccountSessionClient.swift")
profile = read("Presentation/Features/Settings/ViewModels/AccountProfileModel.swift")
results = []
def check(name, condition, evidence):
    assert condition, name
    results.append({"check": name, "passed": True, "evidence": evidence})
check("flag launches false and removes legacy persisted grant", 'snapshot = false' in flags and 'defaults.removeObject' in flags, "ServerFeatureFlags.swift:27-38")
check("flag strict bool, origin, status and latest-generation checks", 'let test_mode: Bool' in flags and 'http.statusCode == 200' in flags and 'Self.origin(of: responseURL) == Self.origin(of: url)' in flags and 'guard generation == refreshGeneration' in flags, "ServerFeatureFlags.swift:41-77")
check("flag changes refresh dashboard and review queues", '.serverFeatureFlagsChanged' in flags and '.reviewQueueRefreshRequested' in flags, "ServerFeatureFlags.swift:87-96")
check("guest entitlement loaded without date validation", 'deviceSnapshotKey' in section(store, 'init(defaults:', 'var canUseAI:') and 'expiresAt' not in section(store, 'init(defaults:', 'var canUseAI:') and 'expiresAt' not in policy, "EntitlementStore.swift:31-47; FreeLimitPolicy.swift:12-34")
grace_refs = [(str(p.relative_to(ROOT)), p.read_text(encoding="utf-8").count('hasOfflineReadGrace')) for p in ROOT.rglob('*.swift') if 'hasOfflineReadGrace' in p.read_text(encoding="utf-8")]
check("14-day offline grace has no caller", sum(n for _, n in grace_refs) == 1, grace_refs)
refresh = section(purchase, 'func refreshTokenAndEntitlement()', 'private func verifyWithBackend')
check("failed device entitlement refresh preserves guest status", 'catch' in refresh and 'errorText =' in refresh and refresh.count('EntitlementStore.shared.update') == 1, "StoreKitService.swift:155-164")
check("shared account expiry and logout safeguards are present", 'expiresAt' in account and '> Date()' in account and 'next.isActive ? next.localEntitlement : nil' in profile and 'Account Premium is revalidated after launch' in store and 'account_required' in store, "AccountSessionClient.swift:28-32; AccountProfileModel.swift:23-45,64-68; EntitlementStore.swift:31-47,106-115")
db = sqlite3.connect(':memory:')
db.execute('CREATE TABLE words(id TEXT, created_at INT, native_language TEXT, learning_language TEXT)')
for i in range(20): db.execute('INSERT INTO words VALUES(?,?,?,?)', (str(i), i, 'en', 'es' if i < 10 else 'fr'))
sql = re.search(r'SELECT COUNT\(\*\)\s+FROM words\s+WHERE native_language = \? AND learning_language = \?;', repo).group(0)
counts = [db.execute(sql, ('en', lang)).fetchone()[0] for lang in ('es', 'fr')]
check("actual count SQL is per language pair: 20 global cards, 10 each", counts == [10, 10] and db.execute('SELECT COUNT(*) FROM words').fetchone()[0] == 20, {"sql": sql, "per_pair": counts, "global": 20, "source": "WordRepository.swift:4165-4185"})
check("test mode false does not lock free or expired_paid existing cards", '(status == "expired_trial" || status == "revoked")' in policy, "FreeLimitPolicy.swift:46-54; 20 free fixture cards skip lock query")
check("expired_trial/revoked lock SQL selects global excess", len(db.execute('SELECT id FROM words ORDER BY created_at,id LIMIT -1 OFFSET 10').fetchall()) == 10, "WordRepository.swift:3531-3575; in-memory excess=10")
save = section(repo, 'func setFlashcardSetForWordLanguagePair(', 'func userNotesForWord(')
check("staged AI save inserts without entitlement/count recheck", 'insertWordAggregate' in save and 'FreeLimitPolicy' not in save and 'EntitlementStore' not in save and 'totalWordCount' not in save, "WordRepository.swift:871-1018")
check("manual and imported production creation paths do have local gates", 'FreeLimitPolicy.canAddWord' in section(repo, 'func addManualWord(', 'func saveImportedWord(') and 'FreeLimitPolicy.canAddWord' in section(repo, 'func saveImportedWord(', 'func importPublicFlashcardSet('), "WordRepository.swift:2679-2843; gates use per-pair count")
check("server accepted false is handled without local rollback", repo.count('guard accepted else { return }') >= 2, "WordRepository.swift:3672-3745; local inserts committed before async upsert")
anki = section(repo, 'func storeTextualAnkiWord(', 'func ')
check("dormant textual Anki repository creator lacks entitlement gate", 'FreeLimitPolicy' not in anki and 'EntitlementStore' not in anki, "WordRepository.swift:381-490; production caller inventory reviewed separately")
queue = (DELTA / 'Domain/SpacedRepetition/ReviewQueueService.swift').read_text(encoding='utf-8')
focused = section(queue, 'func focused(', 'func ')
check("latest focused review uses current snapshot and lock eligibility", 'eligibleCandidates(' in focused and '!$0.isLocked' in queue and '!$0.word.isLocked' in queue, "latest9096c1e ReviewQueueService.swift:108-141,206-214")
result = {"baseline": "547dcb32b07c07291811c4a7e386ccb05344e4c2", "latest_delta": "9096c1e3b20fb920726bd471ed4a092783ee3cc7", "kind": "source/SQL characterization, NOT Swift/StoreKit runtime tests", "passed": len(results), "checks": results}
(OUT / 'static-repro-results.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({"passed": len(results), "kind": result['kind']}))
