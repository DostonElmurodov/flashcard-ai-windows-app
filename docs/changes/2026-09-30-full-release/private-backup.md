# Private database backup — 2026-09-30

[Run 36755056922](https://github.com/DostonElmurodov/mavrylo/actions/runs/36755056922) succeeded at `aa5fda0bc9718e82540fd32f177a4e4eadbf035c`. All 23 focused Linux tests passed before SSH. Build and deploy jobs were skipped.

The existing API-configured PostgreSQL database was copied through its verified network namespace using a read-only snapshot. The archive remains privately on the server under the deployment directory; neither its contents nor database credentials were uploaded.

| Field | Observed value |
|---|---|
| Backup ID | `20260930T175711Z-a7f928dfbe434473` |
| Relative archive | `.owl-ai-private-backups/20260930T175711Z-a7f928dfbe434473.dump` |
| Archive bytes | 257032 |
| SHA-256 | `3861e0d70cd81e3d73db910def8cb4fd1b2c58cecd585f9b65898859f73a2150` |
| Database / dump / restore client major | 16 / 16 / 16 |
| Database size (rounded up) | 10 MiB |
| Host available / total memory | 397 / 961 MiB |
| Free disk at backup start | 17643 MiB |
| Validation | Nonempty archive, checksum, `pg_restore --list` |

Earlier runs `36752822934` and `36753940924` stopped before archive creation at the original 512 MiB memory floor. The latter measured a 10 MiB database and 400 MiB available memory. A reviewed small-database tier now requires 256 MiB for databases up to 64 MiB; larger databases still require 512 MiB. Locking, bounded dump worker, size ceiling and ongoing disk-floor checks remain active. These memory thresholds are start gates, not runtime memory containment.

Archive validation is not a restore test. The server cannot meet the separate rehearsal tool's 4.5 GiB memory requirement. Local WSL Ubuntu and Docker Desktop are available with about 31 GiB Docker memory; an encrypted transfer and local rehearsal are being prepared. No migration, application replacement or public Windows release is claimed by this report.
