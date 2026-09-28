# Task 11 backend review package
Base: 03bb0620098cdbe6d2c21b855271e2e8b968e029
Head: 7310fa4df055c5715c6974a4bb4a463ff036cac5
Workspace: D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend

## Commits
7310fa4 Add manual test-only API validation workflow

## Summary
 .github/workflows/api-validation.yml | 27 +++++++++++++++++++++++++++
 1 file changed, 27 insertions(+)

## Full diff
diff --git a/.github/workflows/api-validation.yml b/.github/workflows/api-validation.yml
new file mode 100644
index 0000000..6107795
--- /dev/null
+++ b/.github/workflows/api-validation.yml
@@ -0,0 +1,27 @@
+name: API validation
+
+on:
+  workflow_dispatch:
+
+permissions:
+  contents: read
+
+jobs:
+  test:
+    runs-on: ubuntu-latest
+    timeout-minutes: 45
+    steps:
+      - uses: actions/checkout@v4
+      - name: Record tested source
+        run: git rev-parse HEAD
+      - uses: actions/setup-dotnet@v4
+        with:
+          dotnet-version: '10.0.x'
+      - name: Require Docker for PostgreSQL 16 integration tests
+        run: docker info
+      - name: Restore
+        run: dotnet restore tests/Mavrylo.Tests.csproj
+      - name: Build Release
+        run: dotnet build Mavrylo.csproj --configuration Release --no-restore
+      - name: Test Release
+        run: dotnet test tests/Mavrylo.Tests.csproj --configuration Release --no-restore
