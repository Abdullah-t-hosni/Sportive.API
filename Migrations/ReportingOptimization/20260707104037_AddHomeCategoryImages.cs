using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddHomeCategoryImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sp = @"
DROP PROCEDURE IF EXISTS `SafeAddOrAlterColumn`;
CREATE PROCEDURE `SafeAddOrAlterColumn`()
BEGIN
    DECLARE col_exists INT;
    
    SELECT COUNT(*) INTO col_exists 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'StoreSettings' AND COLUMN_NAME = 'HomeCategoryEquipmentImage' AND TABLE_SCHEMA = DATABASE();
    
    IF col_exists = 0 THEN
        ALTER TABLE `StoreSettings` ADD COLUMN `HomeCategoryEquipmentImage` longtext NULL;
    ELSE
        ALTER TABLE `StoreSettings` MODIFY COLUMN `HomeCategoryEquipmentImage` longtext NULL;
    END IF;

    SELECT COUNT(*) INTO col_exists 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'StoreSettings' AND COLUMN_NAME = 'HomeCategoryKidsImage' AND TABLE_SCHEMA = DATABASE();
    
    IF col_exists = 0 THEN
        ALTER TABLE `StoreSettings` ADD COLUMN `HomeCategoryKidsImage` longtext NULL;
    ELSE
        ALTER TABLE `StoreSettings` MODIFY COLUMN `HomeCategoryKidsImage` longtext NULL;
    END IF;

    SELECT COUNT(*) INTO col_exists 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'StoreSettings' AND COLUMN_NAME = 'HomeCategoryMenImage' AND TABLE_SCHEMA = DATABASE();
    
    IF col_exists = 0 THEN
        ALTER TABLE `StoreSettings` ADD COLUMN `HomeCategoryMenImage` longtext NULL;
    ELSE
        ALTER TABLE `StoreSettings` MODIFY COLUMN `HomeCategoryMenImage` longtext NULL;
    END IF;

    SELECT COUNT(*) INTO col_exists 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'StoreSettings' AND COLUMN_NAME = 'HomeCategorySpecialSizesImage' AND TABLE_SCHEMA = DATABASE();
    
    IF col_exists = 0 THEN
        ALTER TABLE `StoreSettings` ADD COLUMN `HomeCategorySpecialSizesImage` longtext NULL;
    ELSE
        ALTER TABLE `StoreSettings` MODIFY COLUMN `HomeCategorySpecialSizesImage` longtext NULL;
    END IF;

    SELECT COUNT(*) INTO col_exists 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'StoreSettings' AND COLUMN_NAME = 'HomeCategoryWomenImage' AND TABLE_SCHEMA = DATABASE();
    
    IF col_exists = 0 THEN
        ALTER TABLE `StoreSettings` ADD COLUMN `HomeCategoryWomenImage` longtext NULL;
    ELSE
        ALTER TABLE `StoreSettings` MODIFY COLUMN `HomeCategoryWomenImage` longtext NULL;
    END IF;

END;";

            migrationBuilder.Sql(sp);
            migrationBuilder.Sql("CALL `SafeAddOrAlterColumn`();");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS `SafeAddOrAlterColumn`;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomeCategoryEquipmentImage",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HomeCategoryKidsImage",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HomeCategoryMenImage",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HomeCategorySpecialSizesImage",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HomeCategoryWomenImage",
                table: "StoreSettings");
        }
    }
}
