using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mavrylo.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_usage",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    KeyId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<string>(type: "text", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_usage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Emoji = table.Column<string>(type: "text", nullable: true),
                    Color = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "challenges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    KeyId = table.Column<string>(type: "text", nullable: true),
                    RequestPath = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_words",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DeviceUuid = table.Column<string>(type: "text", nullable: false),
                    ClientWordId = table.Column<string>(type: "text", nullable: true),
                    NormalizedWord = table.Column<string>(type: "text", nullable: false),
                    DisplayWord = table.Column<string>(type: "text", nullable: false),
                    NativeLanguage = table.Column<string>(type: "text", nullable: false),
                    LearningLanguage = table.Column<string>(type: "text", nullable: false),
                    Translation = table.Column<string>(type: "text", nullable: true),
                    Pronunciation = table.Column<string>(type: "text", nullable: true),
                    PartOfSpeech = table.Column<string>(type: "text", nullable: true),
                    DetailJson = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_words", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    KeyId = table.Column<string>(type: "text", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignCount = table.Column<long>(type: "bigint", nullable: false),
                    DeviceUuid = table.Column<string>(type: "text", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    OriginalTransactionId = table.Column<string>(type: "text", nullable: false),
                    DeviceUuid = table.Column<string>(type: "text", nullable: false),
                    ProductId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsTrial = table.Column<bool>(type: "boolean", nullable: false),
                    WasEverPaid = table.Column<bool>(type: "boolean", nullable: false),
                    AutoRenew = table.Column<bool>(type: "boolean", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.OriginalTransactionId);
                });

            migrationBuilder.CreateTable(
                name: "translation_cache",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    NormalizedKey = table.Column<string>(type: "text", nullable: false),
                    NativeLanguage = table.Column<string>(type: "text", nullable: false),
                    LearningLanguage = table.Column<string>(type: "text", nullable: false),
                    CacheKind = table.Column<string>(type: "text", nullable: false),
                    PromptVersion = table.Column<string>(type: "text", nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    AudioStorageKey = table.Column<string>(type: "text", nullable: true),
                    AudioContentType = table.Column<string>(type: "text", nullable: true),
                    AudioUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_translation_cache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    GoogleSub = table.Column<string>(type: "text", nullable: true),
                    AppleSub = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    NativeLanguage = table.Column<string>(type: "text", nullable: false),
                    LearningLanguage = table.Column<string>(type: "text", nullable: false),
                    DailyGoal = table.Column<int>(type: "integer", nullable: false),
                    OnboardingComplete = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewDirection = table.Column<string>(type: "text", nullable: false),
                    RemindersEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderMinutesJson = table.Column<string>(type: "text", nullable: false),
                    ExpandHistoryCards = table.Column<bool>(type: "boolean", nullable: false),
                    ThemeAppearance = table.Column<string>(type: "text", nullable: false),
                    AccentPreset = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Words",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Word = table.Column<string>(type: "text", nullable: false),
                    Translation = table.Column<string>(type: "text", nullable: true),
                    Pronunciation = table.Column<string>(type: "text", nullable: true),
                    AudioUrl = table.Column<string>(type: "text", nullable: true),
                    ExamplesJson = table.Column<string>(type: "text", nullable: false),
                    ExampleTranslationsJson = table.Column<string>(type: "text", nullable: false),
                    UserNotes = table.Column<string>(type: "text", nullable: true),
                    CategoryId = table.Column<string>(type: "text", nullable: true),
                    SourceLanguage = table.Column<string>(type: "text", nullable: false),
                    TargetLanguage = table.Column<string>(type: "text", nullable: false),
                    NextReview = table.Column<string>(type: "text", nullable: true),
                    ReviewInterval = table.Column<int>(type: "integer", nullable: false),
                    EaseFactor = table.Column<double>(type: "double precision", nullable: false),
                    Repetitions = table.Column<int>(type: "integer", nullable: false),
                    IsMastered = table.Column<bool>(type: "boolean", nullable: false),
                    PartOfSpeech = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Words", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_usage_KeyId_Date",
                table: "ai_usage",
                columns: new[] { "KeyId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId",
                table: "Categories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_challenges_ConsumedAt",
                table: "challenges",
                column: "ConsumedAt");

            migrationBuilder.CreateIndex(
                name: "IX_challenges_Kind_KeyId_RequestPath_ExpiresAt",
                table: "challenges",
                columns: new[] { "Kind", "KeyId", "RequestPath", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_device_words_DeviceUuid_ClientWordId",
                table: "device_words",
                columns: new[] { "DeviceUuid", "ClientWordId" });

            migrationBuilder.CreateIndex(
                name: "IX_device_words_DeviceUuid_IsActive",
                table: "device_words",
                columns: new[] { "DeviceUuid", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_device_words_DeviceUuid_NormalizedWord_NativeLanguage_Learn~",
                table: "device_words",
                columns: new[] { "DeviceUuid", "NormalizedWord", "NativeLanguage", "LearningLanguage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_devices_DeviceUuid",
                table: "devices",
                column: "DeviceUuid");

            migrationBuilder.CreateIndex(
                name: "IX_devices_KeyId",
                table: "devices",
                column: "KeyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_DeviceUuid",
                table: "subscriptions",
                column: "DeviceUuid");

            migrationBuilder.CreateIndex(
                name: "IX_translation_cache_lookup",
                table: "translation_cache",
                columns: new[] { "NormalizedKey", "NativeLanguage", "LearningLanguage", "CacheKind", "PromptVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_AppleSub",
                table: "Users",
                column: "AppleSub");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_GoogleSub",
                table: "Users",
                column: "GoogleSub");

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_UserId",
                table: "UserSettings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Words_UserId_UpdatedAt",
                table: "Words",
                columns: new[] { "UserId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_usage");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "challenges");

            migrationBuilder.DropTable(
                name: "device_words");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "translation_cache");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "UserSettings");

            migrationBuilder.DropTable(
                name: "Words");
        }
    }
}
