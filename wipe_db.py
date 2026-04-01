import mysql.connector

config = {
    'user': 'u282618987_sportive',
    'password': 'Abdo01015214#',
    'host': 'srv1787.hstgr.io',
    'database': 'u282618987_sportiveApi',
    'port': 3306
}

tables = [
    'OrderItems', 
    'OrderStatusHistories', 
    'Orders', 
    'CartItems', 
    'WishlistItems',
    'PurchaseInvoiceItems', 
    'SupplierPayments', 
    'PurchaseInvoices', 
    'InventoryMovements', 
    'InventoryAuditItems', 
    'InventoryAudits',
    'JournalLines', 
    'JournalEntries', 
    'ReceiptVouchers', 
    'PaymentVouchers',
    'Reviews', 
    'ProductImages', 
    'ProductVariants', 
    'Products', 
    'Addresses', 
    'Customers', 
    'Suppliers',
    'Notifications', 
    'Coupons'
]

try:
    cnx = mysql.connector.connect(**config)
    cursor = cnx.cursor()
    
    # 🛑 1. Disable Foreign Key Checks to allow Truncate
    cursor.execute("SET FOREIGN_KEY_CHECKS = 0;")
    print("Disabled Foreign Key Checks.")
    
    for table in tables:
        try:
            cursor.execute(f"TRUNCATE TABLE {table};")
            print(f"Truncated {table} successfully.")
        except Exception as e:
            print(f"Error truncating {table}: {e}")
            
    # 🛑 2. Delete Customer Users from AspNetUsers (Security Protocol)
    # We find IDs of users in the 'Customer' role and delete them
    print("Cleaning up Customer Users...")
    cursor.execute("""
        DELETE u FROM AspNetUsers u
        INNER JOIN AspNetUserRoles ur ON u.Id = ur.UserId
        INNER JOIN AspNetRoles r ON ur.RoleId = r.Id
        WHERE r.Name = 'Customer'
        AND u.Email NOT IN ('admin@sportive.com', 'abdullah@sportive.com')
    """)
    print(f"Customer Users deleted: {cursor.rowcount}")

    # 🛑 3. Enable Foreign Key Checks
    cursor.execute("SET FOREIGN_KEY_CHECKS = 1;")
    cnx.commit()
    print("Enabled Foreign Key Checks and Committed.")
    
    cursor.close()
    cnx.close()
    print("DATABASE_WIPE_COMPLETE_SUCCESSFULLY")

except Exception as err:
    print(f"FAILED: {err}")
