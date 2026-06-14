using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [ApiController]
    [Route("api/ingredients")]
    public class IngredientsApiController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public IngredientsApiController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        [Authorize(Roles = AppRoles.Supervisor + "," + AppRoles.Admin + "," + AppRoles.Owner)]
        public IActionResult List()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT StockItemId, Name, ISNULL(Quantity,0), ISNULL(Unit,'unit'), ISNULL(MinQuantity,0), PurchasePrice, Description
FROM dbo.StockItems
ORDER BY Name ASC;";
            var rows = new List<object>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new
                {
                    id = reader.GetInt32(0),
                    namaBahan = reader.IsDBNull(1) ? "-" : reader.GetString(1),
                    stok = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    satuan = reader.IsDBNull(3) ? "unit" : reader.GetString(3),
                    minimalStok = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    hargaBeli = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5),
                    keterangan = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
            }
            return Ok(new { success = true, ingredients = rows });
        }

        [HttpGet("{id:int}")]
        [Authorize(Roles = AppRoles.Supervisor + "," + AppRoles.Admin + "," + AppRoles.Owner)]
        public IActionResult Detail(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT StockItemId, Name, ISNULL(Quantity,0), ISNULL(Unit,'unit'), ISNULL(MinQuantity,0), PurchasePrice, Description
FROM dbo.StockItems
WHERE StockItemId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return NotFound(new { success = false, error = "Ingredient tidak ditemukan." });
            }
            return Ok(new
            {
                success = true,
                ingredient = new
                {
                    id = reader.GetInt32(0),
                    namaBahan = reader.IsDBNull(1) ? "-" : reader.GetString(1),
                    stok = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    satuan = reader.IsDBNull(3) ? "unit" : reader.GetString(3),
                    minimalStok = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    hargaBeli = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5),
                    keterangan = reader.IsDBNull(6) ? null : reader.GetString(6)
                }
            });
        }

        public sealed class UpdateStockRequest
        {
            public int Stock { get; set; }
        }

        [HttpPatch("{id:int}/stock")]
        [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Supervisor)]
        [IgnoreAntiforgeryToken]
        public IActionResult UpdateStock(int id, [FromBody] UpdateStockRequest req)
        {
            if (id <= 0 || req.Stock < 0)
            {
                return BadRequest(new { success = false, error = "Data tidak valid." });
            }
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE dbo.StockItems
SET Quantity = @Quantity
WHERE StockItemId = @Id;";
            cmd.Parameters.AddWithValue("@Quantity", req.Stock);
            cmd.Parameters.AddWithValue("@Id", id);
            var affected = cmd.ExecuteNonQuery();
            return Ok(new { success = true, updated = affected > 0 });
        }
    }
}
