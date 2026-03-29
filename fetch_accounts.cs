using MySqlConnector;
using System;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        string connStr = "Server=srv1787.hstgr.io;Port=3306;Database=u282618987_sportiveApi;User=u282618987_sportive;Password=Abdo01015214#;SslMode=None;AllowPublicKeyRetrieval=true;";
        
        try
        {
            using var conn = new MySqlConnection(connStr);
            conn.Open();
            string sql = "SELECT Id, Code, NameAr, Nature, Type FROM Accounts WHERE IsDeleted = 0 AND IsActive = 1 ORDER BY Code";
            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            
            Console.WriteLine("ID | Code | NameAr | Nature | Type");
            Console.WriteLine("----------------------------------");
            while (reader.Read())
            {
                Console.WriteLine($"{reader["Id"]} | {reader["Code"]} | {reader["NameAr"]} | {reader["Nature"]} | {reader["Type"]}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
