using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using System.Security.Claims;

namespace Sportive.API.Controllers.HR
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResponsibilityTypesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ResponsibilityTypesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ResponsibilityType>>> GetResponsibilityTypes()
        {
            return await _context.ResponsibilityTypes
                .OrderBy(rt => rt.Name)
                .ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ResponsibilityType>> GetResponsibilityType(int id)
        {
            var responsibilityType = await _context.ResponsibilityTypes.FindAsync(id);

            if (responsibilityType == null)
            {
                return NotFound();
            }

            return responsibilityType;
        }

        [HttpPost]
        public async Task<ActionResult<ResponsibilityType>> PostResponsibilityType(ResponsibilityType responsibilityType)
        {
            _context.ResponsibilityTypes.Add(responsibilityType);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetResponsibilityType), new { id = responsibilityType.Id }, responsibilityType);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutResponsibilityType(int id, ResponsibilityType responsibilityType)
        {
            if (id != responsibilityType.Id)
            {
                return BadRequest();
            }

            _context.Entry(responsibilityType).State = EntityState.Modified;
            
            // Prevent changing the code if it's referenced programmatically (optional based on business logic)
            // _context.Entry(responsibilityType).Property(x => x.Code).IsModified = false;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ResponsibilityTypeExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteResponsibilityType(int id)
        {
            var responsibilityType = await _context.ResponsibilityTypes.FindAsync(id);
            if (responsibilityType == null)
            {
                return NotFound();
            }
            
            // Check if there are tasks tied to this type
            var hasTasks = await _context.EmployeeTasks.AnyAsync(t => t.ResponsibilityTypeId == id);
            if (hasTasks)
            {
                return BadRequest(new { message = "لا يمكن حذف هذا النوع لوجود مهام مرتبطة به. يمكنك إيقاف تفعيله بدلاً من حذفه." });
            }

            _context.ResponsibilityTypes.Remove(responsibilityType);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ResponsibilityTypeExists(int id)
        {
            return _context.ResponsibilityTypes.Any(e => e.Id == id);
        }
    }
}
