> SUPERSEDED A REFERENCE — DO NOT DISPATCH OR IMPLEMENT THE ACCOUNT-MANDATORY FLOW BELOW.
> User selected D1=B: iPhone purchase and paid mobile use must work without an Owl account; account login is only required for desktop use. The original A checklist below is retained as historical reference, not current instructions. Before dispatch, replace it with the concrete anonymous-owner contract derived from anonymous-purchase-design-research.md and the remaining recovery decision. A client UUID, appAccountToken or copied Apple JWS is a reference, not proof of the caller's ownership. Preserve lawful purchases, desktop claims and tombstones; no forced repurchase. Ordinary mobile logout must not remove independently proven anonymous mobile rights. Current linked-purchase protection remains until its replacement is implemented and tested. No claim that B1/B2 are closed or that a restore mechanism is approved may be inferred from the user's no-login preference alone.
## Задача 2. Разделить установку, покупку и владельца — B1/B2

**Файлы:** существующие B `AppAttestRegistrationService`, `DeviceContextService`, `IapService`, `AccountEntitlementService`, `SubscriptionOwnershipService`, `AppStoreServerClient`, `SubscriptionEntity`, `AppDbContext`, `AccountSubscriptionController`; создать `AccountPurchaseIdentityService.cs`, модель `AccountPurchaseIdentityEntity.cs`, аддитивную миграцию и `tests/PurchaseOwnershipTests.cs`.

**Interfaces (вариант A):** новый `POST /owlai/account/subscription/apple/purchase-context`, account-auth, no-store; ответ `{ "app_account_token": "UUID" }`, без account ID из тела. `AccountPurchaseIdentityService.GetOrCreateTokenAsync(string accountId, CancellationToken ct): Task<Guid>`. В БД уникальная связь account→token и token→account; токен выдаётся сервером, не является паролем. Отдельное `SubscriptionEntity.AppAccountToken` хранит значение из проверенной транзакции, а `DeviceUuid` перестаёт быть владельцем.

- [ ] Добавить тесты: новый корректный App Attest key с чужим UUID не получает premium; повторная регистрация не меняет владельца; JWS аккаунта A нельзя впервые claim в B; два конкурентных claim имеют одного владельца; повторный claim A идемпотентен; tombstone не становится unclaimed.
- [ ] Запустить тесты и подтвердить провал на существующей реализации.
- [ ] Добавить purchase-context и атомарное создание токена; claim и verify сопоставляют проверенный Apple token с авторизованным владельцем **до** изменения subscription/device. Сохранить проверку подписи, environment, продукта, срока и точного original ID.
- [ ] Убрать выдачу платных прав через поиск произвольного `request.DeviceUuid`; не перезаписывать Apple token UUID вызывающего устройства. Для новых покупок без account-контекста возвращать понятный `account_required`/отказ до purchase в новом клиенте.
- [ ] Сохранить существующие законные owner-связи. Миграция legacy не угадывает владельца по присланному UUID — применяется задача 10. Непривязанная старая квитанция не получает нового владельца автоматически.
- [ ] Выполнить `PurchaseOwnershipTests`, `AccountClaimProofTests`, `SharedSubscriptionTests`, `SharedSubscriptionPostgresTests`; затем Release-сборку и небольшой коммит.

**Готово:** B1/B2 закрыты для новой модели; законный владелец получает те же права на другом устройстве после входа, чужой аккаунт — нет. Legacy отдельно имеет доказанный путь из задачи 10, иначе выпуск остаётся закрыт.




## Local EF tooling
Controller installed dotnet-ef10.0.9 matching the checked-in EF Core/Design10.0.9 packages, into D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/tools/dotnet-ef.exe. Verified --version10.0.9. The installation used a task-local NuGet.Config with only official nuget.org, without a global tool install or repo dependency edits. Use this explicit path for eventual additive migration scaffolding; select Data project and Web startup as appropriate, and use only the isolated local test DB. Tool availability is not evidence that any new migration/startup-copy test has run.
