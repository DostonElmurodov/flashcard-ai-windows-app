## Задача 5. Одна интерпретация запроса для лимита и AI — B3

**Файлы:** B `Filters/AiProtectionFilter.cs`, `src/Mavrylo.Services/Services/DeviceWordService.cs`, `src/Mavrylo.Services/Dtos/Dtos.cs`, при необходимости небольшой отдельный `AiRequestContextReader.cs`; `tests/DeviceWordServiceTests.cs`, `tests/AiProtectionFilterTests.cs`.

**Interfaces:** оставить `TryReadWordContext(byte[] body)` совместимым либо заменить через один явно типизированный reader для конкретного endpoint. Семантика совпадает с MVC snake_case/case-insensitive настройками. Подпись App Attest всегда проверяет **исходные байты** до нормализации.

- [ ] HTTP-тесты `word`/`Word`/`WORD`, языковых полей, пустого слова, неправильного типа, отсутствующего поля, смешанных дубликатов `word` + `Word`, повреждённого JSON. Неоднозначные дубликаты отклоняются 400 до провайдера.
- [ ] Обычный и альтернативный регистр: после 10 разных слов 11-е отклоняется, `providerCalls=10`; разрешённые повторные операции с существующим словом соответствуют текущей политике и расходуют суточную квоту.
- [ ] Сделать разбор общим/эквивалентным model binding; не допускать «не понял поле → пропустил проверку → выполнил AI». Проверить все JSON AI-операции и отдельный multipart-путь, где word заранее отсутствует.
- [ ] Тест гонки при 9 словах и двух новых: максимум одно новое резервирование; подпись прежних исходных байтов остаётся валидной.
- [ ] Прогнать проверки DeviceWord/AI HTTP и коммит.

**Готово:** варианты записи одного запроса не меняют решение о лимите; неправильное тело не создаёт расход.



## Execution context supplied by controller
- Work only in B: D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend, codex/paid-access-hardening.
- D1 now explicitly B: iOS purchase/use without mandatory account; account only needed for desktop. This task must not alter ownership or introduce account requirements. D2 values pending; use synthetic test quotas only.
- Task1 introduced PaidAccessJsonAuditRegressionTests.B3_CaseInsensitiveBindingCannotBypassTenWordReservation. It uses actual HTTP and a counting fake provider; lowercase control currently passes, uppercase variant is red. Preserve the remaining known red audit tests for later tasks; don't fix unrelated ownership/lifecycle here.
- Check all three JSON operations and explicit multipart extract-words behavior. Body signatures use original bytes. MVC is snake_case and case-insensitive; do not create different parsing meanings in the limiter and controller. A missing/invalid word on JSON routes must not become an allowed no-word extraction request.
- Account JSON routes use the same DTOs; consider duplicate rejection consistently where the same interpretation can reach AI. Preserve their existing account authentication (no App Attest added).
- Inspect concurrency with separate EF contexts on the real local PostgreSQL, not an in-memory substitute. Existing serializable reservation must yield a predictable denial under contention and cannot silently reserve more than one remaining slot. Coordinate all relevant word mutation paths if they share the invariant.
- Local test server: OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests . Fixtures create and drop their own isolated database. No real provider calls/purchases.
- Run explicit simplify after every coding/fix iteration; no standalone simplify skill is installed, so log reuse/complexity/efficiency check and rerun focused tests.
- No subagents. No push/deploy. Write report to sibling task-5-report.md with commands/results, red-to-green evidence, simplify log, limitations and commit.
