using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ezZkvi.Data;

namespace ezZkvi.Controllers
{
    [Authorize(Roles = "Admin")]
    public class KvizSesijaController : Controller
    {
        private readonly ApplicationDbContext _context;

        public KvizSesijaController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: KvizSesija
        public async Task<IActionResult> Index()
        {
            return View(await _context.KvizSesije.ToListAsync());
        }

        // GET: KvizSesija/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var kvizSesija = await _context.KvizSesije
                .FirstOrDefaultAsync(m => m.ID == id);
            if (kvizSesija == null)
            {
                return NotFound();
            }

            return View(kvizSesija);
        }

        // GET: KvizSesija/Create

        // POST: KvizSesija/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.

        // GET: KvizSesija/Edit/5

        // POST: KvizSesija/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.

        // GET: KvizSesija/Delete/5

        // POST: KvizSesija/Delete/5

    }
}
