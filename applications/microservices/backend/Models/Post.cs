using System.Collections.Generic;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace microservices.backend
{
    public class Post
    {
        public int PostId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
    }
}