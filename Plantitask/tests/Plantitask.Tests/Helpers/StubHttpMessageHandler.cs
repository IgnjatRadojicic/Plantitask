using System.Net;
using System.Text;

namespace Plantitask.Tests.Helpers
{
    /// <summary>
    /// HttpClient takes its transport as a constructor argument, which is what makes an outbound
    /// api testable without a network or a server. Routes are matched on a fragment of the url so
    /// a test can say what the token endpoint answers and what the capture endpoint answers
    /// separately, and every request is recorded so the body and headers we sent can be asserted.
    /// </summary>
    public class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<Route> _routes = [];

        public List<RecordedRequest> Requests { get; } = [];

        public StubHttpMessageHandler Respond(
            string urlContains, HttpStatusCode status, string json)
        {
            _routes.Add(new Route(urlContains, _ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

            return this;
        }

        public StubHttpMessageHandler Throw(string urlContains, Exception exception)
        {
            _routes.Add(new Route(urlContains, _ => throw exception));
            return this;
        }

        public RecordedRequest RequestTo(string urlContains) =>
            Requests.Single(r => r.Url.Contains(urlContains, StringComparison.OrdinalIgnoreCase));

        public int CountOfRequestsTo(string urlContains) =>
            Requests.Count(r => r.Url.Contains(urlContains, StringComparison.OrdinalIgnoreCase));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.Method,
                url,
                body,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));

            // Later routes win so a test can override a default set up in the constructor.
            var route = _routes.LastOrDefault(
                r => url.Contains(r.UrlContains, StringComparison.OrdinalIgnoreCase));

            if (route is null)
                throw new InvalidOperationException($"No stubbed response for {request.Method} {url}");

            return route.Respond(request);
        }

        private sealed record Route(string UrlContains, Func<HttpRequestMessage, HttpResponseMessage> Respond);
    }

    public sealed record RecordedRequest(
        HttpMethod Method,
        string Url,
        string Body,
        string? AuthScheme,
        string? AuthParameter);
}
