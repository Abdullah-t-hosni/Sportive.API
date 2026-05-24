using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using System.Linq;
using System.Threading.Tasks;

namespace Sportive.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PosHeldCartsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PosHeldCartsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var carts = await _context.PosHeldCarts
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new PosHeldCartDto
                {
                    Id = c.Id,
                    ReferenceId = c.ReferenceId,
                    Name = c.Name,
                    Phone = c.Phone,
                    ItemsJson = c.ItemsJson,
                    Total = c.Total,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            return Ok(new { items = carts });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePosHeldCartDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var cart = new PosHeldCart
            {
                ReferenceId = dto.ReferenceId ?? System.Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(),
                Name = string.IsNullOrWhiteSpace(dto.Name) ? "Customer" : dto.Name,
                Phone = dto.Phone,
                ItemsJson = dto.ItemsJson,
                Total = dto.Total
            };

            _context.PosHeldCarts.Add(cart);
            await _context.SaveChangesAsync();

            return Ok(new PosHeldCartDto
            {
                Id = cart.Id,
                ReferenceId = cart.ReferenceId,
                Name = cart.Name,
                Phone = cart.Phone,
                ItemsJson = cart.ItemsJson,
                Total = cart.Total,
                CreatedAt = cart.CreatedAt
            });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var cart = await _context.PosHeldCarts.FindAsync(id);
            if (cart == null) return NotFound();

            _context.PosHeldCarts.Remove(cart);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
