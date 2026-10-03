using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRead",
                table: "WhatsAppMessages",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "Suppliers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OnlinePriceAdjustment",
                table: "ProductVariants",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BundleDiscountType",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "BundleDiscountValue",
                table: "Products",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BundleProductIds",
                table: "Products",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "BundleTitle",
                table: "Products",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "OnlineDiscountPrice",
                table: "Products",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OnlinePrice",
                table: "Products",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcludedCategoryIds",
                table: "ProductDiscounts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ChangedByName",
                table: "OrderStatusHistories",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyDiscountAmount",
                table: "Orders",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyPointsEarned",
                table: "Orders",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyPointsRedeemed",
                table: "Orders",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "EnableDelayRules",
                table: "EmployeeShiftOverrides",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlexible",
                table: "EmployeeShiftOverrides",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ShiftEndTime",
                table: "EmployeeShiftOverrides",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "EnableDelayRules",
                table: "Employees",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlexible",
                table: "Employees",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ShiftEndTime",
                table: "Employees",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CurrentTier",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "LifetimePointsEarned",
                table: "Customers",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyPointsBalance",
                table: "Customers",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InternalChatChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    DirectKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Icon = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalChatChannels", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LoyaltyPointTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    Points = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TransactionType = table.Column<int>(type: "int", nullable: false),
                    AmountEquivalent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedBy = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyPointTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoyaltyPointTransactions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoyaltyPointTransactions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LoyaltyProgramSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PointsPerCurrencyUnit = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CurrencyUnitPerPointRedeemed = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    MinPointsToRedeem = table.Column<int>(type: "int", nullable: false),
                    MaxRedemptionPercentage = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PointsExpiryDays = table.Column<int>(type: "int", nullable: false),
                    SilverThreshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SilverMultiplier = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GoldThreshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GoldMultiplier = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PlatinumThreshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PlatinumMultiplier = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyProgramSettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InternalChatMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ChannelId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsAdmin = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    JoinedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    MutedUntil = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalChatMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternalChatMembers_InternalChatChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "InternalChatChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InternalChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ChannelId = table.Column<int>(type: "int", nullable: false),
                    SenderId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SenderName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SenderAvatarUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReplyToMessageId = table.Column<int>(type: "int", nullable: true),
                    MentionedUserIds = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LinkedEntityType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LinkedEntityId = table.Column<int>(type: "int", nullable: true),
                    LinkedEntityRef = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MediaUrl = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MediaType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsEdited = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternalChatMessages_InternalChatChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "InternalChatChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InternalChatMessages_InternalChatMessages_ReplyToMessageId",
                        column: x => x.ReplyToMessageId,
                        principalTable: "InternalChatMessages",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InternalChatReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MessageId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Emoji = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalChatReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternalChatReactions_InternalChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "InternalChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InternalChatReadReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MessageId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalChatReadReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternalChatReadReceipts_InternalChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "InternalChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_CustomerId",
                table: "Suppliers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_SupplierId",
                table: "Customers",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalChatMembers_ChannelId",
                table: "InternalChatMembers",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalChatMessages_ChannelId",
                table: "InternalChatMessages",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalChatMessages_ReplyToMessageId",
                table: "InternalChatMessages",
                column: "ReplyToMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalChatReactions_MessageId",
                table: "InternalChatReactions",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalChatReadReceipts_MessageId",
                table: "InternalChatReadReceipts",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyPointTransactions_CustomerId",
                table: "LoyaltyPointTransactions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyPointTransactions_OrderId",
                table: "LoyaltyPointTransactions",
                column: "OrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Suppliers_SupplierId",
                table: "Customers",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Suppliers_Customers_CustomerId",
                table: "Suppliers",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Suppliers_SupplierId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Customers_CustomerId",
                table: "Suppliers");

            migrationBuilder.DropTable(
                name: "InternalChatMembers");

            migrationBuilder.DropTable(
                name: "InternalChatReactions");

            migrationBuilder.DropTable(
                name: "InternalChatReadReceipts");

            migrationBuilder.DropTable(
                name: "LoyaltyPointTransactions");

            migrationBuilder.DropTable(
                name: "LoyaltyProgramSettings");

            migrationBuilder.DropTable(
                name: "InternalChatMessages");

            migrationBuilder.DropTable(
                name: "InternalChatChannels");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_CustomerId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_SupplierId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsRead",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "OnlinePriceAdjustment",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "BundleDiscountType",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BundleDiscountValue",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BundleProductIds",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BundleTitle",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OnlineDiscountPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OnlinePrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ExcludedCategoryIds",
                table: "ProductDiscounts");

            migrationBuilder.DropColumn(
                name: "ChangedByName",
                table: "OrderStatusHistories");

            migrationBuilder.DropColumn(
                name: "LoyaltyDiscountAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsEarned",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsRedeemed",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "EnableDelayRules",
                table: "EmployeeShiftOverrides");

            migrationBuilder.DropColumn(
                name: "IsFlexible",
                table: "EmployeeShiftOverrides");

            migrationBuilder.DropColumn(
                name: "ShiftEndTime",
                table: "EmployeeShiftOverrides");

            migrationBuilder.DropColumn(
                name: "EnableDelayRules",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "IsFlexible",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ShiftEndTime",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CurrentTier",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LifetimePointsEarned",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsBalance",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "Customers");
        }
    }
}
