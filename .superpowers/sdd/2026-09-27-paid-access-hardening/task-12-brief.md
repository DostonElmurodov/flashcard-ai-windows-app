## Задача 12. Реальный iPhone, Apple Sandbox и выпуск

**Артефакт:** `docs/paid-access-release-checklist.md` с version/build/commit, сценарием, ожидаемым результатом, фактическим результатом и счётчиком provider attempts. Реальные платные production-покупки не нужны для первичного Sandbox-прогона.

- [ ] Free 9/10/11; несколько языков; true→false; холодный старт/переустановка/смена ключа; локальный cache и новый AI проверяются раздельно.
- [ ] Покупка, trial если включён, renewal, expiry, grace/end, cancellation без немедленной потери оплаченного периода, refund/revoke; правильный original ID при нескольких подписках.
- [ ] Restore на другом iPhone с тем же владельцем; другой аккаунт на том же телефоне; logout во время покупки/AI; повтор после backend timeout; удалённый аккаунт/tombstone.
- [ ] Offline, сбой Apple/backend, 401/402/403/409/429/503, параллельные запросы, исчерпание общего бюджета. Для отказа до AI подтвердить **0 новых provider calls**; для fallback — отдельный резерв каждой попытки.
- [ ] Перед публикацией сопоставить реально развёрнутый backend и оба клиента с проверенными SHA; проверить флаг false на каждом production-экземпляре, действующие квоты и выбранный бюджет. Один HTTP-ответ флага или healthcheck недостаточен.
- [ ] Подготовить конкретный релиз, результаты и способ безопасного отключения AI для отдельного решения о публикации. Запрос «сделай план» не означает публикацию или изменение рабочей конфигурации.

**Разрешение на выпуск по результатам проверок:** все B1–B6 имеют закрывающие тесты; новая модель владения доказана; migration/restore работают; бюджет ограничивает действительные provider attempts; клиентская политика согласована; реальные Apple-сценарии пройдены; нет нерешённых существенных находок. Если какой-либо пункт отсутствует — описать конкретный пробел, не писать «гарантированно безопасно».



## iOS distribution-routing preflight
Current I APIConfiguration.swift reads bundled APIBaseURL and otherwise falls back to https://api.mavrylo.com. Resources/Info.plist maps it from API_BASE_URL; both checked-in Debug and Release build configurations currently set that same production hostname. Read-only source search found no StoreKit/AppTransaction/Sandbox receipt-based routing in Infrastructure/ or App/. Therefore a Debug test pointing somewhere else does not prove actual submission/TestFlight/App Review routing to an isolated signed-Sandbox backend. Task10/12 must define and verify the real distribution path without granting Sandbox transactions Production rights or trusting an arbitrary client header as authority. No new routing policy, deployed Sandbox endpoint or physical-device verification is claimed here. The outdated APIConfiguration comment describing optional pasted JWT/MVP is not a current authorization specification; its static bearer support is inside DEBUG only and must remain absent in Release checks.
