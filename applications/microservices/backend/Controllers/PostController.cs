using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace microservices.backend
{
    [Route("v1/[controller]")]
    [ApiController]
    public class PostController : ControllerBase
    {
        private readonly BloggingContext _context;

        public PostController(BloggingContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Post>>> GetPostItems()
        {
            return await _context.Posts.OrderBy(e => -e.PostId).ToListAsync();
        }

        [HttpPost]
        public async Task<ActionResult<int>> PostPostItem(Post post)
        {
            post.PostId = 0;
            await _context.Posts.AddAsync(post);
            return await _context.SaveChangesAsync();
        }

        [HttpDelete("{postID}")]
        public async Task DeletePostItem(int postID)
        {
            var p = _context.Posts.Find(postID);
            if (p != null)
            {
                _context.Posts.Remove(p);
                await _context.SaveChangesAsync();
            }
        }
    }
}
