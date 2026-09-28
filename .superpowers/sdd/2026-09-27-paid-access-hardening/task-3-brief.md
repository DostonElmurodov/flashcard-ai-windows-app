> SUPERSEDED A REFERENCE — DO NOT DISPATCH OR IMPLEMENT THE ACCOUNT-MANDATORY FLOW BELOW.
> User selected D1=B: iPhone purchase and paid mobile use must work without an Owl account; account login is only required for desktop use. The original A checklist below is retained as historical reference, not current instructions. Before dispatch, replace it with the concrete anonymous-owner contract derived from anonymous-purchase-design-research.md and the remaining recovery decision. A client UUID, appAccountToken or copied Apple JWS is a reference, not proof of the caller's ownership. Preserve lawful purchases, desktop claims and tombstones; no forced repurchase. Ordinary mobile logout must not remove independently proven anonymous mobile rights. Current linked-purchase protection remains until its replacement is implemented and tested. No claim that B1/B2 are closed or that a restore mechanism is approved may be inferred from the user's no-login preference alone.
## Задача 3. iOS purchase/restore с аккаунтом и безопасным завершением

**Файлы:** I `Infrastructure/Purchases/{StoreKitService,AppAccountTokenStore,EntitlementStore}.swift`, `Infrastructure/Networking/{APIClient,AccountAPIClient}.swift`, `Presentation/Features/Home/Views/PaywallView.swift`, `Presentation/Features/Account/{AccountView,AccountViewModel}.swift`; создать `FlashCardAITests/PurchaseOwnershipTests.swift` и добавить в test target.

**Interfaces:** получает purchase-context задачи 2; добавляет `AccountAPIClient.purchaseContext() async throws -> PurchaseContextDTO`, DTO `appAccountToken: UUID`; использует существующую generation-привязку аккаунта. Старый локальный token сохраняется только для явно описанного legacy-перехода и не подставляется вместо account token.

- [ ] Тесты: гость видит вход до оплаты; купивший A восстанавливает на новом устройстве; B не наследует A; отмена/pending не создают premium; Apple success + backend timeout допускают безопасный повтор без повторного списания.
- [ ] Тест покупки во время смены аккаунта: контекст захватывается для A; завершение после входа B не даёт B права и не теряет транзакцию A. Обработчик Transaction.updates также не привязывает покупку текущему случайному аккаунту.
- [ ] Получать токен с backend перед `product.purchase`; не заменять владельца при Restore. Завершать StoreKit-транзакцию только после устойчивого результата обработки/сохранения для повторения; обрабатывать отмену, pending, ошибки и конфликт понятным сообщением без чужих account ID/email.
- [ ] Реализовать отдельно утверждённый legacy-путь из 10. Если доказательства недостаточно, не предлагать повторную покупку как единственный выход; показать восстановление/поддержку.
- [ ] Выполнить новые тесты на macOS, `SharedAccountSessionTests` и существующие purchase/account тесты; коммит.

**Готово:** пользователь платит один раз, получает права нужного аккаунта и восстанавливает их без передачи владельца.


- New Task4 restore/claim error contract: HTTP503 with code subscription_reconciliation_required means a stored historical purchase needs explicit verification/repair. Treat this separately from temporary Apple/network unavailability: show plain user-facing support/recovery guidance and avoid automatic retry loops or repurchase prompts. Backend record and saved content remain preserved. Read current Task4 report for final contract before implementation.


## Deferred caller observation from Task8 scoped re-review
Task8 re-review O1 (task-8-fix1-review.md) identified unchanged StoreKitService transaction-update/purchase/restore catch blocks assigning errorText from a stale verification error after a newer token refresh succeeded and cleared it. Mutation authority and token persistence are protected at accepted58dd623, but UI can show an obsolete error. Evaluate and cover this exact caller-ordering scenario during the eventual D1=B purchase/restore integration; no runtime reproduction exists yet and no old mandatory-login design is authorized. Final whole-feature review must see this unresolved observation if not addressed here.
