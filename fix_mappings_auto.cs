using MySqlConnector;
using System;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        string connStr = "Server=srv1787.hstgr.io;Port=3306;Database=u282618987_sportiveApi;User=u282618987_sportive;Password=Abdo01015214#;SslMode=None;AllowPublicKeyRetrieval=true;";
        
        var mappingCodes = new Dictionary<string, string>
        {
            {"cashAccountID", "110101"},
            {"salesAccountID", "4101"},
            {"inventoryAccountID", "1106"},
            {"purchaseAccountID", "511"},
            {"supplierAccountID", "2101"},
            {"customerAccountID", "1103"},
            {"costOfGoodsSoldAccountID", "51101"},
            {"vatInputAccountID", "2105"},
            {"vatOutputAccountID", "2105"},
            {"salesDiscountAccountID", "410101"},
            {"purchaseDiscountAccountID", "51103"}
        };

        try
        {
            using var conn = new MySqlConnection(connStr);
            conn.Open();
            
            foreach (var mapping in mappingCodes)
            {
                // Find AccountId by Code
                string findSql = "SELECT Id FROM Accounts WHERE Code = @code AND IsDeleted = 0 LIMIT 1";
                int? accountId = null;
                using (var cmd = new MySqlCommand(findSql, conn))
                {
                    cmd.Parameters.AddWithValue("@code", mapping.Value);
                    var result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        accountId = Convert.ToInt32(result);
                    }
                }

                if (accountId.HasValue)
                {
                    // Update or Insert Mapping
                    string upsertSql = @"
                        INSERT INTO AccountSystemMappings (`Key`, AccountId, CreatedAt, IsDeleted) 
                        VALUES (@key, @accId, NOW(), 0)
                        ON DUPLICATE KEY UPDATE AccountId = @accId, UpdatedAt = NOW(), IsDeleted = 0";
                    using (var cmd = new MySqlCommand(upsertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@key", mapping.Key);
                        cmd.Parameters.AddWithValue("@accId", accountId.Value);
                        cmd.ExecuteNonQuery();
                    }
                    Console.WriteLine($"Mapped '{mapping.Key}' to Code '{mapping.Value}' (ID: {accountId.Value})");
                }
                else
                {
                    Console.WriteLine($"WARNING: Account with code '{mapping.Value}' not found for key '{mapping.Key}'");
                }
            }
            Console.WriteLine("Financial mapping completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
