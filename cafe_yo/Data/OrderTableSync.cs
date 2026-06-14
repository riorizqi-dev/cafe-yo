using Microsoft.Data.SqlClient;

namespace cafe_yo.Data
{
    public static class OrderTableSync
    {
        private const int BookingTimeoutMinutes = 90;

        public static void SyncAllTableStatuses(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE t
SET t.Status = CASE
    WHEN EXISTS (
        SELECT 1
        FROM dbo.Orders o
        WHERE o.TableId = t.TableId
          AND (
            o.[Status] IS NULL
            OR LOWER(LTRIM(RTRIM(o.[Status]))) NOT IN ('completed','paid','cancelled','canceled','selesai','dibatalkan')
          )
    ) THEN 'Isi'
    WHEN LOWER(LTRIM(RTRIM(ISNULL(t.Status,'')))) = 'booking'
         AND t.UpdatedAt >= DATEADD(MINUTE, -@BookingTimeoutMinutes, SYSUTCDATETIME()) THEN 'Booking'
    ELSE 'Kosong'
END
FROM dbo.Tables t;";
            cmd.Parameters.AddWithValue("@BookingTimeoutMinutes", BookingTimeoutMinutes);
            cmd.ExecuteNonQuery();
        }

        public static void SyncByOrderId(SqlConnection conn, int orderId)
        {
            if (orderId <= 0)
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TOP 1 TableId FROM dbo.Orders WHERE OrderId = @OrderId;";
            cmd.Parameters.AddWithValue("@OrderId", orderId);
            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value)
            {
                return;
            }

            SyncByTableId(conn, Convert.ToInt32(obj));
        }

        public static void SyncByTableNumber(SqlConnection conn, int tableNumber)
        {
            if (tableNumber <= 0)
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TOP 1 TableId FROM dbo.Tables WHERE TableNumber = @TableNumber;";
            cmd.Parameters.AddWithValue("@TableNumber", tableNumber);
            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value)
            {
                return;
            }

            SyncByTableId(conn, Convert.ToInt32(obj));
        }

        private static void SyncByTableId(SqlConnection conn, int tableId)
        {
            if (tableId <= 0)
            {
                return;
            }

            using var hasActiveCmd = conn.CreateCommand();
            hasActiveCmd.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM dbo.Orders o
    WHERE o.TableId = @TableId
      AND (
        o.[Status] IS NULL
        OR LOWER(LTRIM(RTRIM(o.[Status]))) NOT IN ('completed','paid','cancelled','canceled','selesai','dibatalkan')
      )
) THEN 1 ELSE 0 END;";
            hasActiveCmd.Parameters.AddWithValue("@TableId", tableId);
            var hasActive = Convert.ToInt32(hasActiveCmd.ExecuteScalar() ?? 0) == 1;

            using var statusCmd = conn.CreateCommand();
            statusCmd.CommandText = "SELECT TOP 1 Status FROM dbo.Tables WHERE TableId = @TableId;";
            statusCmd.Parameters.AddWithValue("@TableId", tableId);
            var currentStatus = (statusCmd.ExecuteScalar()?.ToString() ?? string.Empty).Trim().ToLowerInvariant();

            using var updatedAtCmd = conn.CreateCommand();
            updatedAtCmd.CommandText = "SELECT TOP 1 UpdatedAt FROM dbo.Tables WHERE TableId = @TableId;";
            updatedAtCmd.Parameters.AddWithValue("@TableId", tableId);
            var updatedAtObj = updatedAtCmd.ExecuteScalar();
            var updatedAt = updatedAtObj is DateTime dt ? dt : DateTime.UtcNow.AddYears(-1);
            var isBookingFresh = updatedAt >= DateTime.UtcNow.AddMinutes(-BookingTimeoutMinutes);

            var nextStatus = hasActive
                ? "Isi"
                : (currentStatus == "booking" && isBookingFresh) ? "Booking" : "Kosong";

            using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = "UPDATE dbo.Tables SET Status = @Status WHERE TableId = @TableId;";
            updateCmd.Parameters.AddWithValue("@Status", nextStatus);
            updateCmd.Parameters.AddWithValue("@TableId", tableId);
            updateCmd.ExecuteNonQuery();
        }

        public static bool HasActiveOrders(SqlConnection conn, int tableNumber)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM dbo.Orders o
    INNER JOIN dbo.Tables t ON t.TableId = o.TableId
    WHERE t.TableNumber = @TableNumber
      AND (
        o.[Status] IS NULL
        OR LOWER(LTRIM(RTRIM(o.[Status]))) NOT IN ('completed','paid','cancelled','canceled','selesai','dibatalkan')
      )
) THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@TableNumber", tableNumber);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
        }
    }
}
