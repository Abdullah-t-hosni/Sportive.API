-- Phase 1 Migration — شغّله في phpMyAdmin بعد dotnet ef database update

ALTER TABLE `Orders`
    ADD COLUMN IF NOT EXISTS `Source` int NOT NULL DEFAULT 0;

UPDATE `Orders` SET `Source` = 0 WHERE `Source` IS NULL;

SELECT
    CASE Source WHEN 0 THEN 'Website' WHEN 1 THEN 'POS' END AS Source,
    COUNT(*) AS Count
FROM `Orders`
GROUP BY Source;

SELECT 'Phase 1 Done ✅' AS Result;
