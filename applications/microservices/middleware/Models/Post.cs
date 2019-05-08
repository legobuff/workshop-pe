using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Serialization;

namespace microservices.middleware
{
    [DataContract(Name="repo")]
    public class Post
    {
        [DataMember(Name="postId")]
        public int PostId { get; set; }

        [DataMember(Name="title")]
        public string Title { get; set; }

        [DataMember(Name="content")]
        public string Content { get; set; }
    }


}