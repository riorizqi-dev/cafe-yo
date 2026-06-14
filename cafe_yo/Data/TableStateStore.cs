using Microsoft.Data.SqlClient;
using cafe_yo.Models;

namespace cafe_yo.Data
{
    public static class TableStateStore
    {
        public static void EnsureTables(SqlConnection conn, int defaultCount = 30)
        {
            using var create = conn.CreateCommand();
            create.CommandText = @"
IF OBJECT_ID(N'dbo.Tables', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Tables (
        TableId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TableNumber INT NOT NULL UNIQUE,
        Status NVARCHAR(20) NULL,
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;";
            create.ExecuteNonQuery();

            using (var ensureUpdatedAt = conn.CreateCommand())
            {
                ensureUpdatedAt.CommandText = @"
IF COL_LENGTH('dbo.Tables', 'UpdatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Tables ADD UpdatedAt DATETIME2 NULL;
END;";
                ensureUpdatedAt.ExecuteNonQuery();
            }

            var hasUpdatedAt = false;
            using (var hasUpdatedAtCmd = conn.CreateCommand())
            {
                hasUpdatedAtCmd.CommandText = "SELECT CASE WHEN COL_LENGTH('dbo.Tables', 'UpdatedAt') IS NULL THEN 0 ELSE 1 END;";
                hasUpdatedAt = Convert.ToInt32(hasUpdatedAtCmd.ExecuteScalar() ?? 0) == 1;
            }

            if (hasUpdatedAt)
            {
                using (var fillUpdatedAt = conn.CreateCommand())
                {
                    fillUpdatedAt.CommandText = "UPDATE dbo.Tables SET UpdatedAt = SYSUTCDATETIME() WHERE UpdatedAt IS NULL;";
                    fillUpdatedAt.ExecuteNonQuery();
                }

                using (var ensureNotNull = conn.CreateCommand())
                {
                    ensureNotNull.CommandText = "ALTER TABLE dbo.Tables ALTER COLUMN UpdatedAt DATETIME2 NOT NULL;";
                    ensureNotNull.ExecuteNonQuery();
                }

                using (var ensureDefault = conn.CreateCommand())
                {
                    ensureDefault.CommandText = @"
IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    INNER JOIN sys.tables t ON t.object_id = c.object_id
    WHERE t.name = 'Tables'
      AND c.name = 'UpdatedAt'
)
BEGIN
    ALTER TABLE dbo.Tables ADD CONSTRAINT DF_Tables_UpdatedAt DEFAULT SYSUTCDATETIME() FOR UpdatedAt;
END;";
                    ensureDefault.ExecuteNonQuery();
                }
            }

            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(1) FROM dbo.Tables;";
            var count = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
            if (count > 0)
            {
                NormalizeAllStatuses(conn);
                return;
            }

            for (var i = 1; i <= defaultCount; i++)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText = "INSERT INTO dbo.Tables (TableNumber, Status, UpdatedAt) VALUES (@TableNumber, @Status, SYSUTCDATETIME());";
                ins.Parameters.AddWithValue("@TableNumber", i);
                ins.Parameters.AddWithValue("@Status", "Kosong");
                ins.ExecuteNonQuery();
            }
        }

        public static List<CafeTable> GetAll(SqlConnection conn)
        {
            var list = new List<CafeTable>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TableId, TableNumber, ISNULL(Status, 'Kosong') FROM dbo.Tables ORDER BY TableNumber ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new CafeTable
                {
                    TableId = reader.GetInt32(0),
                    TableNumber = reader.GetInt32(1),
                    Status = NormalizeStatus(reader.GetString(2))
                });
            }
            return list;
        }

        public static bool SelectTableForBooking(SqlConnection conn, int tableNumber, int? previousTable, out string error)
        {
            error = string.Empty;
            var current = GetStatus(conn, tableNumber);
            if (current == null)
            {
                error = "Meja tidak ditemukan.";
                return false;
            }

            // Customer may select only empty table, unless selecting same table again.
            if (!string.Equals(current, "Kosong", StringComparison.OrdinalIgnoreCase) &&
                !(previousTable.HasValue && previousTable.Value == tableNumber))
            {
                error = $"Meja {tableNumber} sedang {current}.";
                return false;
            }

            UpdateStatus(conn, tableNumber, "Booking");

            if (previousTable.HasValue && previousTable.Value > 0 && previousTable.Value != tableNumber)
            {
                var prevStatus = GetStatus(conn, previousTable.Value);
                if (string.Equals(prevStatus, "Booking", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus(conn, previousTable.Value, "Kosong");
                }
            }

            return true;
        }

        public static void ReleaseBooking(SqlConnection conn, int tableNumber)
        {
            if (OrderTableSync.HasActiveOrders(conn, tableNumber))
            {
                UpdateStatus(conn, tableNumber, "Isi");
                return;
            }

            var status = GetStatus(conn, tableNumber);
            if (status == null)
            {
                return;
            }
            if (string.Equals(status, "Booking", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "Isi", StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatus(conn, tableNumber, "Kosong");
            }
        }

        public static void UpdateStatus(SqlConnection conn, int tableNumber, string rawStatus)
        {
            var normalized = NormalizeStatus(rawStatus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE dbo.Tables SET Status = @Status, UpdatedAt = SYSUTCDATETIME() WHERE TableNumber = @TableNumber;";
            cmd.Parameters.AddWithValue("@Status", normalized);
            cmd.Parameters.AddWithValue("@TableNumber", tableNumber);
            cmd.ExecuteNonQuery();
        }

        public static string NormalizeStatus(string? status)
        {
            var s = (status ?? string.Empty).Trim().ToLowerInvariant();
            return s switch
            {
                "kosong" or "available" or "empty" => "Kosong",
                "booking" or "booked" or "reserved" => "Booking",
                "isi" or "occupied" or "ready" => "Isi",
                _ => "Kosong"
            };
        }

        private static string? GetStatus(SqlConnection conn, int tableNumber)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TOP 1 Status FROM dbo.Tables WHERE TableNumber = @TableNumber;";
            cmd.Parameters.AddWithValue("@TableNumber", tableNumber);
            var value = cmd.ExecuteScalar()?.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            return NormalizeStatus(value);
        }

        private static void NormalizeAllStatuses(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TableNumber, ISNULL(Status, 'Kosong') FROM dbo.Tables;";
            var items = new List<(int Number, string CurrentStatus, string NormalizedStatus)>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var current = reader.GetString(1);
                    items.Add((reader.GetInt32(0), current, NormalizeStatus(current)));
                }
            }

            foreach (var item in items)
            {
                if (string.Equals(item.CurrentStatus, item.NormalizedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var update = conn.CreateCommand();
                update.CommandText = "UPDATE dbo.Tables SET Status = @Status WHERE TableNumber = @TableNumber;";
                update.Parameters.AddWithValue("@Status", item.NormalizedStatus);
                update.Parameters.AddWithValue("@TableNumber", item.Number);
                update.ExecuteNonQuery();
            }
        }
    }
}
