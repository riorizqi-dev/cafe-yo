using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace cafe_yo.Data
{
    public static class LegacyUsersSeeder
    {
        public static async System.Threading.Tasks.Task EnsureAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var connStr = config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr)) return;

            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Create Users table if not exists (simple legacy table to support existing CRUD controller)
            var createSql = @"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Username] NVARCHAR(256) NOT NULL,
        [FullName] NVARCHAR(256) NULL,
        [Role] NVARCHAR(100) NULL,
        [PasswordHash] NVARCHAR(512) NULL,
        [IsOnline] BIT NOT NULL DEFAULT(0)
    );
END
";
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = createSql;
                cmd.CommandType = CommandType.Text;
                await cmd.ExecuteNonQueryAsync();
            }

            // Ensure IsOnline column exists (in case table was created earlier without it)
            var ensureColSql = @"
IF COL_LENGTH('dbo.Users','IsOnline') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD IsOnline BIT NOT NULL CONSTRAINT DF_Users_IsOnline DEFAULT(0);
END
";
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = ensureColSql;
                cmd.CommandType = CommandType.Text;
                await cmd.ExecuteNonQueryAsync();
            }

            // Seed minimal data if table empty
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(1) FROM dbo.Users";
                var cnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (cnt == 0)
                {
                    using var tx = conn.BeginTransaction();
                    try
                    {
                        var insert = conn.CreateCommand();
                        insert.Transaction = tx;
                        insert.CommandText = "INSERT INTO dbo.Users (Username, FullName, Role, PasswordHash, IsOnline) VALUES (@u,@f,@r,@p,@o);";
                        insert.Parameters.AddWithValue("@u", "admin_ryu");
                        insert.Parameters.AddWithValue("@f", "Admin Ryu");
                        insert.Parameters.AddWithValue("@r", "Admin");
                        insert.Parameters.AddWithValue("@p", "");
                        insert.Parameters.AddWithValue("@o", 1);
                        insert.ExecuteNonQuery();

                        tx.Commit();
                    }
                    catch
                    {
                        try { tx.Rollback(); } catch { }
                    }
                }
            }
        }
    }
}
