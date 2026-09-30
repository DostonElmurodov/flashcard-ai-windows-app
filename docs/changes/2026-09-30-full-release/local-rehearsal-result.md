# Restored-copy migration result — 2026-09-30

Actual local Docker execution passed; run `deafda65a5c276ca5b41c6a7`. The original server was not accessed by this rehearsal. The tool restored the verified PostgreSQL archive into an isolated local database, started the candidate API without provider credentials, checked health and migration invariants, and stopped its containers while retaining recovery resources.

- Backup: `20260930T175711Z-a7f928dfbe434473`, 257032 bytes; SHA-256 `3861e0d70cd81e3d73db910def8cb4fd1b2c58cecd585f9b65898859f73a2150`.
- Registry digest: `sha256:bacebddb25d6afa9771982f5ce2a5da5113a6c1ced70a9098fb7f7100c463c29`; source `aa5fda0bc9718e82540fd32f177a4e4eadbf035c`.
- Migrations: 9 before, 14 after. Devices 143, subscriptions 0, legacy usage 10, words 65: aggregate counts preserved.
- Usage migration Ready: true. All seven checked issue reasons had zero rows; no issue records were erased or Ready value manually overridden.

Backup export run 36758249239 transported only encrypted CMS data. The private key and decrypted archive stayed in a restricted WSL directory on the user's computer. The local manifest was reconstructed from verified job output; its database size is a conservative 10 MiB upper bound, not a claim of exact original database bytes.

Image export run 36758253367 produced a 108505261-byte archive with SHA-256 `6b485dcddd94fbd3e61c7ccd54992557afcd53ec558556c22e873730f0b45054`. Docker Desktop exposes the loaded OCI manifest ID `sha256:73f2d2c78aa26a9827179a8e3cde2d81d45e40fabf5762ce67a85d5fbc4b0124`, whereas the export reports config ID `sha256:2e8751992871e7a7dc583efff498bf1f16020acd4b2505c95c22cf6ec3f1bdd5`. The archive hash, manifest-to-config hash chain, revision label, and all eight root filesystem layers were verified before execution. The rehearsal output field named `rehearsal_config_image_id` contains the local OCI manifest ID in this environment.

This proves actual backup restoration and candidate startup migrations against this snapshot. It does not prove row-by-row content equality, live cutover configuration, old-image compatibility with the migrated schema, Apple purchase/device scenarios, or a published Windows release. Production configuration and coordinated deployment remain pending.
