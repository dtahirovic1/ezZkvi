using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ezZkvi.Data;

namespace ezZkvi.Controllers
{
    [Authorize(Roles = "Admin")]
    public class KvizSesijaPitanjeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public KvizSesijaPitanjeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: KvizSesijaPitanje
        public async Task<IActionResult> Index()
        {
            return View(await _context.KvizSesijaPitanja.ToListAsync());
        }

        // GET: KvizSesijaPitanje/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var kvizSesijaPitanje = await _context.KvizSesijaPitanja
                .FirstOrDefaultAsync(m => m.ID == id);
            if (kvizSesijaPitanje == null)
            {
                return NotFound();
            }

            return View(kvizSesijaPitanje);
        }

        // GET: KvizSesijaPitanje/Create

        // POST: KvizSesijaPitanje/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.

        // GET: KvizSesijaPitanje/Edit/5

        // POST: KvizSesijaPitanje/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.

        // GET: KvizSesijaPitanje/Delete/5

        // POST: KvizSesijaPitanje/Delete/5

    }
}
