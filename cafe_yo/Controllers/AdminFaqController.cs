using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [Route("Admin/Faq")]
    public sealed class AdminFaqController : Controller
    {
        private readonly IConfiguration _configuration;

        public AdminFaqController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult Index()
        {
            var rows = new List<FaqRow>();
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT FaqId, Question, Answer, Keywords, IsActive, SortOrder
FROM dbo.Faqs
ORDER BY SortOrder ASC, FaqId ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new FaqRow
                {
                    FaqId = reader.GetInt32(0),
                    Question = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Answer = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Keywords = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    IsActive = !reader.IsDBNull(4) && reader.GetBoolean(4),
                    SortOrder = reader.IsDBNull(5) ? 0 : reader.GetInt32(5)
                });
            }

            return View("~/Views/Admin/Faq/Index.cshtml", rows);
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public IActionResult Create([FromForm] FaqRow model)
        {
            if (string.IsNullOrWhiteSpace(model.Question) || string.IsNullOrWhiteSpace(model.Answer))
            {
                TempData["FaqError"] = "Pertanyaan dan jawaban wajib diisi.";
                return RedirectToAction(nameof(Index));
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO dbo.Faqs (Question, Answer, Keywords, IsActive, SortOrder, CreatedAt, UpdatedAt)
VALUES (@Question, @Answer, @Keywords, @IsActive, @SortOrder, SYSUTCDATETIME(), SYSUTCDATETIME());";
            cmd.Parameters.AddWithValue("@Question", model.Question.Trim());
            cmd.Parameters.AddWithValue("@Answer", model.Answer.Trim());
            cmd.Parameters.AddWithValue("@Keywords", (object?)model.Keywords?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", true);
            cmd.Parameters.AddWithValue("@SortOrder", model.SortOrder < 0 ? 0 : model.SortOrder);
            cmd.ExecuteNonQuery();

            TempData["FaqSuccess"] = "FAQ berhasil ditambahkan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("{id:int}/Toggle")]
        [ValidateAntiForgeryToken]
        public IActionResult Toggle(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE dbo.Faqs SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END, UpdatedAt = SYSUTCDATETIME() WHERE FaqId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("{id:int}/Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM dbo.Faqs WHERE FaqId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();

            return RedirectToAction(nameof(Index));
        }

        public sealed class FaqRow
        {
            public int FaqId { get; set; }
            public string Question { get; set; } = string.Empty;
            public string Answer { get; set; } = string.Empty;
            public string? Keywords { get; set; }
            public bool IsActive { get; set; }
            public int SortOrder { get; set; }
        }
    }
}

