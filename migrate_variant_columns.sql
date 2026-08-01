ALTER TABLE ProductVariants 
  ADD COLUMN IF NOT EXISTS IsActive TINYINT(1) NOT NULL DEFAULT 1,
  ADD COLUMN IF NOT EXISTS MaxOnlineStock INT NULL;

INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
SELECT '20260801000000_AddVariantIsActiveAndMaxOnlineStock', '9.0.0'
WHERE NOT EXISTS (
    SELECT 1 FROM __EFMigrationsHistory 
    WHERE MigrationId = '20260801000000_AddVariantIsActiveAndMaxOnlineStock'
);
