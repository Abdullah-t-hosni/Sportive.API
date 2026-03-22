-- ============================================================
-- Sportive Database - Initial Migration Script
-- Run this if you prefer SQL over EF Core migrations
-- ============================================================

-- Categories
CREATE TABLE Categories (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    NameAr        NVARCHAR(100) NOT NULL,
    NameEn        NVARCHAR(100) NOT NULL,
    DescriptionAr NVARCHAR(500),
    DescriptionEn NVARCHAR(500),
    [Type]        INT NOT NULL,   -- 1=Men, 2=Women, 3=Kids, 4=Equipment
    ImageUrl      NVARCHAR(500),
    IsActive      BIT NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt     DATETIME2,
    IsDeleted     BIT NOT NULL DEFAULT 0
);

-- Products
CREATE TABLE Products (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    NameAr         NVARCHAR(200) NOT NULL,
    NameEn         NVARCHAR(200) NOT NULL,
    DescriptionAr  NVARCHAR(MAX),
    DescriptionEn  NVARCHAR(MAX),
    Price          DECIMAL(18,2) NOT NULL,
    DiscountPrice  DECIMAL(18,2),
    SKU            NVARCHAR(50) NOT NULL,
    Brand          NVARCHAR(100),
    [Status]       INT NOT NULL DEFAULT 0,  -- 0=Active, 1=OutOfStock, 2=Discontinued
    IsFeatured     BIT NOT NULL DEFAULT 0,
    CategoryId     INT NOT NULL REFERENCES Categories(Id),
    CreatedAt      DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt      DATETIME2,
    IsDeleted      BIT NOT NULL DEFAULT 0
);

CREATE INDEX IX_Products_CategoryId ON Products(CategoryId);
CREATE INDEX IX_Products_Status     ON Products([Status]);
CREATE INDEX IX_Products_SKU        ON Products(SKU);

-- Product Variants (Size/Color/Stock)
CREATE TABLE ProductVariants (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    ProductId       INT NOT NULL REFERENCES Products(Id) ON DELETE CASCADE,
    [Size]          NVARCHAR(20),    -- XS, S, M, L, XL, XXL or numeric
    Color           NVARCHAR(50),
    ColorAr         NVARCHAR(50),
    StockQuantity   INT NOT NULL DEFAULT 0,
    PriceAdjustment DECIMAL(18,2) DEFAULT 0,
    ImageUrl        NVARCHAR(500),
    CreatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2,
    IsDeleted       BIT NOT NULL DEFAULT 0
);

-- Product Images
CREATE TABLE ProductImages (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    ProductId INT NOT NULL REFERENCES Products(Id) ON DELETE CASCADE,
    ImageUrl  NVARCHAR(500) NOT NULL,
    IsMain    BIT NOT NULL DEFAULT 0,
    SortOrder INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2,
    IsDeleted BIT NOT NULL DEFAULT 0
);

-- Customers
CREATE TABLE Customers (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    FirstName   NVARCHAR(100) NOT NULL,
    LastName    NVARCHAR(100) NOT NULL,
    Email       NVARCHAR(200) NOT NULL,
    Phone       NVARCHAR(20),
    AppUserId   NVARCHAR(450),
    DateOfBirth DATE,
    CreatedAt   DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt   DATETIME2,
    IsDeleted   BIT NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Customers_Email UNIQUE (Email)
);

-- Addresses
CREATE TABLE Addresses (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId     INT NOT NULL REFERENCES Customers(Id) ON DELETE CASCADE,
    TitleAr        NVARCHAR(100) NOT NULL,
    TitleEn        NVARCHAR(100) NOT NULL,
    Street         NVARCHAR(300) NOT NULL,
    City           NVARCHAR(100) NOT NULL,
    District       NVARCHAR(100),
    BuildingNo     NVARCHAR(20),
    Floor          NVARCHAR(10),
    ApartmentNo    NVARCHAR(10),
    AdditionalInfo NVARCHAR(500),
    Latitude       FLOAT,
    Longitude      FLOAT,
    IsDefault      BIT NOT NULL DEFAULT 0,
    CreatedAt      DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt      DATETIME2,
    IsDeleted      BIT NOT NULL DEFAULT 0
);

-- Orders
CREATE TABLE Orders (
    Id                   INT IDENTITY(1,1) PRIMARY KEY,
    OrderNumber          NVARCHAR(30) NOT NULL,
    CustomerId           INT NOT NULL REFERENCES Customers(Id),
    [Status]             INT NOT NULL DEFAULT 1,  -- OrderStatus enum
    FulfillmentType      INT NOT NULL,             -- 1=Delivery, 2=Pickup
    PaymentMethod        INT NOT NULL,             -- 1=Cash, 2=Card, 3=Vodafone, 4=InstaPay
    PaymentStatus        INT NOT NULL DEFAULT 1,
    DeliveryAddressId    INT REFERENCES Addresses(Id) ON DELETE SET NULL,
    DeliveryFee          DECIMAL(18,2) NOT NULL DEFAULT 0,
    EstimatedDeliveryDate DATETIME2,
    ActualDeliveryDate   DATETIME2,
    DeliveryNotes        NVARCHAR(500),
    PickupScheduledAt    DATETIME2,
    PickupConfirmedAt    DATETIME2,
    SubTotal             DECIMAL(18,2) NOT NULL,
    DiscountAmount       DECIMAL(18,2) NOT NULL DEFAULT 0,
    CouponCode           NVARCHAR(50),
    TotalAmount          DECIMAL(18,2) NOT NULL,
    CustomerNotes        NVARCHAR(500),
    AdminNotes           NVARCHAR(500),
    CreatedAt            DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt            DATETIME2,
    IsDeleted            BIT NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Orders_OrderNumber UNIQUE (OrderNumber)
);

CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId);
CREATE INDEX IX_Orders_Status     ON Orders([Status]);
CREATE INDEX IX_Orders_CreatedAt  ON Orders(CreatedAt);

-- Order Items
CREATE TABLE OrderItems (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    OrderId          INT NOT NULL REFERENCES Orders(Id) ON DELETE CASCADE,
    ProductId        INT NOT NULL REFERENCES Products(Id),
    ProductVariantId INT REFERENCES ProductVariants(Id),
    ProductNameAr    NVARCHAR(200) NOT NULL,
    ProductNameEn    NVARCHAR(200) NOT NULL,
    [Size]           NVARCHAR(20),
    Color            NVARCHAR(50),
    Quantity         INT NOT NULL,
    UnitPrice        DECIMAL(18,2) NOT NULL,
    TotalPrice       DECIMAL(18,2) NOT NULL,
    CreatedAt        DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt        DATETIME2,
    IsDeleted        BIT NOT NULL DEFAULT 0
);

-- Order Status History
CREATE TABLE OrderStatusHistories (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    OrderId         INT NOT NULL REFERENCES Orders(Id) ON DELETE CASCADE,
    [Status]        INT NOT NULL,
    Note            NVARCHAR(500),
    ChangedByUserId NVARCHAR(450),
    CreatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2,
    IsDeleted       BIT NOT NULL DEFAULT 0
);

-- Cart Items
CREATE TABLE CartItems (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId       INT NOT NULL REFERENCES Customers(Id) ON DELETE CASCADE,
    ProductId        INT NOT NULL REFERENCES Products(Id),
    ProductVariantId INT REFERENCES ProductVariants(Id),
    Quantity         INT NOT NULL DEFAULT 1,
    CreatedAt        DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt        DATETIME2,
    IsDeleted        BIT NOT NULL DEFAULT 0
);

-- Reviews
CREATE TABLE Reviews (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    ProductId  INT NOT NULL REFERENCES Products(Id),
    CustomerId INT NOT NULL REFERENCES Customers(Id),
    Rating     INT NOT NULL CHECK (Rating BETWEEN 1 AND 5),
    Comment    NVARCHAR(1000),
    IsApproved BIT NOT NULL DEFAULT 0,
    CreatedAt  DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt  DATETIME2,
    IsDeleted  BIT NOT NULL DEFAULT 0
);

-- ─── Seed Categories ───
INSERT INTO Categories (NameAr, NameEn, [Type], IsActive, CreatedAt) VALUES
(N'رجالي',         N'Men',              1, 1, GETUTCDATE()),
(N'حريمي',         N'Women',            2, 1, GETUTCDATE()),
(N'أطفال',         N'Kids',             3, 1, GETUTCDATE()),
(N'أدوات رياضية', N'Sports Equipment', 4, 1, GETUTCDATE());
