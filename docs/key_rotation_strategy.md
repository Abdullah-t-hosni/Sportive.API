# Key Rotation Strategy for Sportive.API

This document outlines the cryptographic keys used in Sportive.API, their security implications, and the operational procedures to rotate them securely without causing downtime or breaking data integrity.

---

## 1. Cryptographic Keys Inventory

| Key Configuration Path | Purpose | Algorithm | Impact of Exposure |
| :--- | :--- | :--- | :--- |
| `Security:RefreshTokenSecret` | Keys HMAC-SHA256 hash of refresh tokens saved in user sessions | HMAC-SHA256 | Attacker can forge/modify refresh tokens to hijack user sessions |
| `Security:SearchSecret` | Generates deterministic hashes for exact-match database queries (EmailHash, PhoneHash) | HMAC-SHA256 | Attacker can perform dictionary attacks to identify specific customers |
| `Security:AuditSecret` | Signs chained audit logs to ensure immutability and detect tampering | HMAC-SHA256 | Attacker can rewrite/forge audit logs |
| `Security:BackupSecret` | Signs zip backups | HMAC-SHA256 | Attacker can forge database backups |
| `Security:EncryptionKeyV1` | Encrypts PII fields (Email, Phone, NationalId) via AES-256-GCM | AES-256-GCM (32 bytes) | Full compromise of PII data |

---

## 2. General Rotation Policies

* **Regular Rotation**: Rotate all keys at least once a year.
* **Emergency Rotation**: Perform rotation immediately if any key is leaked, checked into source control, or if a database administrator's credentials/access is compromised.
* **Storage**: Store keys securely in environment variables or a key vault (e.g. AWS Secrets Manager, Azure Key Vault, HashiCorp Vault) rather than hardcoded configuration files.

---

## 3. Key Rotation & Migration Strategies

### A. RefreshTokenSecret (User Sessions)
Since refresh tokens are short-lived (e.g., 7-30 days), rotation does not require re-encrypting historical data.
1. **Action**: Replace the secret value in the environment variables or `appsettings.json`.
2. **Impact**: Existing active sessions will fail validation upon their next refresh because the system will try to compute the hash with the new secret and it will not match the existing database hash.
3. **Graceful Strategy**:
   * Implement a two-key verification (Primary and Secondary/Fallback).
   * Keep the old secret as the Fallback secret.
   * If verification fails with the Primary secret, attempt verification with the Fallback secret.
   * If it succeeds, re-hash the token with the Primary secret and save it to the database (on-the-fly upgrade), then issue the new tokens.
   * Remove the Fallback secret after the refresh token expiration period (e.g., 30 days).

---

### B. SearchSecret (PII Query Indexing)
Search hashes (`EmailHash`, `PhoneHash`) are deterministic. Changing the secret changes all computed hashes, which breaks all queries looking up customers by email or phone.
1. **Action**: Keep a list of keys: `SearchSecretV1` (old) and `SearchSecretV2` (new).
2. **Database Migration Script**:
   * Fetch all records, decrypt the column (using the decryption key), compute the new hash using `SearchSecretV2`, and update the record.
   * Or write a script that updates `EmailHash` and `PhoneHash` in batches by re-hashing.
3. **Dual-Query Mode**:
   * During the transition, if lookup by `SearchSecretV2` fails to yield a match, fall back to querying by `SearchSecretV1`. If found, upgrade the hash in the database to the new version.

---

### C. AuditSecret (Audit Log Chain)
Audit log chain hashes are computed as `HMACSHA256(Action + UserId + CreatedAt + PreviousHash, Security:AuditSecret)`.
1. **Action**: The audit log chain must remain historically verifiable.
2. **Strategy**:
   * Add a `KeyVersion` (or `SignatureVersion`) property to the `AuditLog` entity if not already present.
   * When verifying, look up the appropriate secret matching the record's `SignatureVersion` (e.g. `v1`, `v2`).
   * When writing new logs, always use the active key version.

---

### D. BackupSecret (Backup Signing)
1. **Action**: Update `Security:BackupSecret`.
2. **Strategy**:
   * Store historical backup keys or add `SignatureVersion` to the `BackupRecord` database table.
   * When restoring an old backup, verify the signature using the key that matches the backup's `SignatureVersion` (e.g., `v1` key).

---

### E. EncryptionKeyV1 (PII AES-256-GCM Data Encryption)
To rotate the data encryption key:
1. **Define Keys**: Introduce `Security:EncryptionKeyV2`.
2. **Envelope/Key Versioning**:
   * The encrypted PII data includes a version header (first 4 bytes of ciphertext denote `KeyVersion`).
   * Read logic automatically checks the `KeyVersion` and selects the correct key for decryption.
3. **Data Backfill Job**:
   * Run a background task to read all `Customer` records where `KeyVersion == 1`.
   * Decrypt using `EncryptionKeyV1`.
   * Encrypt using `EncryptionKeyV2` and set `KeyVersion = 2`.
   * Update the record in the database.
   * Once all records are migrated, `EncryptionKeyV1` can be safely retired.
