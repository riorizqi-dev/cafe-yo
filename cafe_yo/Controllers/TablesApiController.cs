using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using cafe_yo.Data;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [ApiController]
    [Route("api/tables")]
    public class TablesApiController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public TablesApiController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        [AllowAnonymous]
        public IActionResult GetTables()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            TableStateStore.EnsureTables(conn);
            OrderTableSync.SyncAllTableStatuses(conn);
            var rows = TableStateStore.GetAll(conn).Select(t => new
            {
                tableNumber = t.TableNumber,
                status = t.Status ?? "Kosong"
            });
            return Ok(new { success = true, tables = rows });
        }

        public sealed class SelectTableRequest
        {
            public int TableNumber { get; set; }
            public int? PreviousTableNumber { get; set; }
        }

        [HttpPost("select")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public IActionResult Select([FromBody] SelectTableRequest req)
        {
            if (req.TableNumber <= 0)
            {
                return BadRequest(new { success = false, error = "Nomor meja tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            TableStateStore.EnsureTables(conn);
            OrderTableSync.SyncAllTableStatuses(conn);

            if (!TableStateStore.SelectTableForBooking(conn, req.TableNumber, req.PreviousTableNumber, out var error))
            {
                return Ok(new { success = false, error });
            }

            return Ok(new { success = true, tableNumber = req.TableNumber, status = "Booking" });
        }

        public sealed class ReleaseTableRequest
        {
            public int TableNumber { get; set; }
        }

        [HttpPost("release")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public IActionResult Release([FromBody] ReleaseTableRequest req)
        {
            if (req.TableNumber <= 0)
            {
                return Ok(new { success = true });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            TableStateStore.EnsureTables(conn);
            OrderTableSync.SyncAllTableStatuses(conn);
            TableStateStore.ReleaseBooking(conn, req.TableNumber);
            return Ok(new { success = true });
        }

        public sealed class UpdateStatusRequest
        {
            public int TableNumber { get; set; }
            public string? Status { get; set; }
        }

        [HttpPost("status")]
        [Authorize(Policy = "KasirOnly")]
        [IgnoreAntiforgeryToken]
        public IActionResult UpdateStatus([FromBody] UpdateStatusRequest req)
        {
            if (req.TableNumber <= 0)
            {
                return BadRequest(new { success = false, error = "Nomor meja tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            TableStateStore.EnsureTables(conn);
            OrderTableSync.SyncAllTableStatuses(conn);

            var normalized = TableStateStore.NormalizeStatus(req.Status);
            if (normalized == "Kosong" && OrderTableSync.HasActiveOrders(conn, req.TableNumber))
            {
                OrderTableSync.SyncByTableNumber(conn, req.TableNumber);
                return Ok(new { success = false, error = "Meja masih punya order aktif, tidak bisa diubah ke Kosong." });
            }

            TableStateStore.UpdateStatus(conn, req.TableNumber, req.Status ?? "Kosong");
            OrderTableSync.SyncByTableNumber(conn, req.TableNumber);

            var current = TableStateStore.GetAll(conn).FirstOrDefault(x => x.TableNumber == req.TableNumber)?.Status ?? normalized;
            return Ok(new { success = true, tableNumber = req.TableNumber, status = current });
        }
    }
}
