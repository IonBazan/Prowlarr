using System.Net;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Events;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HistoryTests
{
    [TestFixture]
    public class HistoryServiceFixture : CoreTest<HistoryService>
    {
        private HttpRequest _request;

        [SetUp]
        public void Setup()
        {
            _request = new HttpRequest("https://indexer.local/api?t=search");
        }

        private void GivenQuery(IndexerQueryResult queryResult)
        {
            Subject.Handle(new IndexerQueryEvent(1, new BasicSearchCriteria { SearchTerm = "test", Categories = new int[0] }, queryResult));
        }

        private void VerifyInserted(bool successful, string elapsedTime, string url)
        {
            Mocker.GetMock<IHistoryRepository>()
                .Verify(r => r.Insert(It.Is<History.History>(h =>
                    h.Successful == successful &&
                    h.Data["ElapsedTime"] == elapsedTime &&
                    h.Data["Url"] == url)), Times.Once());
        }

        [Test]
        public void should_record_response_metadata()
        {
            var response = new HttpResponse(_request, new HttpHeader(), new CookieCollection(), new byte[1024], 250);

            GivenQuery(new IndexerQueryResult(response));

            VerifyInserted(true, "250", "https://indexer.local/api?t=search");
        }

        [Test]
        public void should_record_failed_query()
        {
            var response = new HttpResponse(_request, new HttpHeader(), new CookieCollection(), new byte[0], 100, HttpStatusCode.InternalServerError);

            GivenQuery(new IndexerQueryResult(response));

            VerifyInserted(false, "100", "https://indexer.local/api?t=search");
        }

        [Test]
        public void should_treat_suppressed_status_code_as_successful()
        {
            _request.SuppressHttpError = true;
            _request.SuppressHttpErrorStatusCodes = new[] { HttpStatusCode.NotFound };

            var response = new HttpResponse(_request, new HttpHeader(), new CookieCollection(), new byte[0], 100, HttpStatusCode.NotFound);

            GivenQuery(new IndexerQueryResult(response));

            VerifyInserted(true, "100", "https://indexer.local/api?t=search");
        }

        [Test]
        public void should_record_query_without_response()
        {
            GivenQuery(new IndexerQueryResult());

            VerifyInserted(false, string.Empty, string.Empty);
        }
    }
}
