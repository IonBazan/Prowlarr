using System.Collections.Generic;
using System.Net;
using NzbDrone.Common.Http;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers
{
    public class IndexerQueryResult
    {
        public IndexerQueryResult()
        {
            Releases = new List<ReleaseInfo>();
        }

        // Keep only response metadata so the body can be released once parsed
        public IndexerQueryResult(HttpResponse response)
            : this()
        {
            Request = response?.Request;
            StatusCode = response?.StatusCode;
            ElapsedTime = response?.ElapsedTime;
        }

        public IList<ReleaseInfo> Releases { get; set; }
        public HttpRequest Request { get; set; }
        public HttpStatusCode? StatusCode { get; set; }
        public long? ElapsedTime { get; set; }
        public bool Cached { get; set; }
    }
}
