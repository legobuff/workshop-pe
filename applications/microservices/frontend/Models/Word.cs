using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Serialization;

namespace microservices.frontend
{
    [DataContract(Name="word")]
    public class Word
    {
        [DataMember(Name="name")]
        public string Name { get; set; }

        [DataMember(Name="size")]
        public float Size { get; set; }
    }


}