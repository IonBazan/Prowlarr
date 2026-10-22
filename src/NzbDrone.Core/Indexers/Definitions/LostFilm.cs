using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Definitions.Cardigann;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Indexers.Settings;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers.Definitions;

public class LostFilm : TorrentIndexerBase<LostFilmSettings>
{
    public override string Name => "LostFilm.tv";
    public override string[] IndexerUrls => new[]
    {
        "https://www.lostfilm.tv/",
        "https://www.lostfilmtv5.site/",
        "https://www.lostfilmtv2.site/",
        "https://www.lostfilmtv3.site/",
        "https://www.lostfilm.today/",
        "https://www.lostfilm.download/",
        "https://www.lostfilm.run/",
        "https://lostfilm.site/",
        "https://www.lostfilm.life/",
        "https://www.lostfilm.uno/",
        "https://www.lostfilm.tw/"
    };
    public override string[] LegacyUrls => new[]
    {
        "https://lostfilm.tw/",
        "https://www.lostfilm.win/",
        "https://www.lostfilmtv.site/"
    };
    public override string Description => "LostFilm is a RUSSIAN Semi-Private site. Unique portal for foreign series";
    public override string Language => "ru-RU";
    public override Encoding Encoding => Encoding.UTF8;
    public override IndexerPrivacy Privacy => IndexerPrivacy.SemiPrivate;
    public override IndexerCapabilities Capabilities => SetCapabilities();

    // The site asks for a captcha on repeated logins, so the captcha session and the session from a connection test
    // (which runs before the indexer is saved and has no cookie storage) are kept here to avoid logging in again.
    private readonly ICached<IDictionary<string, string>> _sessionCache;

    private string CaptchaCacheKey => $"captcha|{Settings.BaseUrl}";
    private string SessionCacheKey => $"session|{Settings.BaseUrl}|{Settings.Username}";

    public LostFilm(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IConfigService configService, ICacheManager cacheManager, Logger logger)
        : base(httpClient, eventAggregator, indexerStatusService, configService, logger)
    {
        _sessionCache = cacheManager.GetCache<IDictionary<string, string>>(GetType(), "sessions");
    }

    public override IIndexerRequestGenerator GetRequestGenerator()
    {
        return new LostFilmRequestGenerator(Settings);
    }

    public override IParseIndexerResponse GetParser()
    {
        return new LostFilmParser(Definition, Settings, RateLimit, _httpClient, _logger);
    }

    protected override async Task DoLogin()
    {
        if (!Settings.Username.Contains('@'))
        {
            throw new IndexerAuthException("Username must be an e-mail address");
        }

        UpdateCookies(null, null);
        _sessionCache.Remove(SessionCacheKey);

        // The captcha text is only valid for the session that loaded the captcha image
        var captchaCookies = Settings.Captcha.IsNotNullOrWhiteSpace() ? _sessionCache.Find(CaptchaCacheKey) : null;

        var requestBuilder = new HttpRequestBuilder(Settings.BaseUrl + "ajaxik.users.php")
        {
            LogResponseContent = true,
            AllowAutoRedirect = true
        };

        requestBuilder.Post()
            .SetCookies(captchaCookies ?? new Dictionary<string, string>())
            .AddFormParameter("act", "users")
            .AddFormParameter("type", "login")
            .AddFormParameter("mail", Settings.Username)
            .AddFormParameter("pass", Settings.Password)
            .AddFormParameter("rem", "1")
            .SetHeader("Content-Type", "application/x-www-form-urlencoded")
            .SetHeader("Referer", Settings.BaseUrl + "login")
            .Accept(HttpAccept.Json);

        if (captchaCookies != null)
        {
            requestBuilder
                .AddFormParameter("need_captcha", "1")
                .AddFormParameter("captcha", Settings.Captcha);

            _sessionCache.Remove(CaptchaCacheKey);
        }

        var response = await ExecuteAuth(requestBuilder.Build());

        if (!response.Content.Contains("\"success\":true"))
        {
            var errorMessage = response.Content switch
            {
                var c when c.Contains("\"need_captcha\":true") => "Captcha is required. Open the indexer settings, enter the code from the image and save.",
                var c when c.Contains("\"error\":1") || c.Contains("\"error\":2") || c.Contains("\"error\":4") => "Captcha is incorrect. Refresh the captcha in the indexer settings and try again.",
                var c when c.Contains("\"error\":3") => "E-mail or password is incorrect",
                _ => response.Content
            };

            throw new IndexerAuthException(errorMessage);
        }

        var cookies = response.GetCookies();
        UpdateCookies(cookies, DateTime.Now.AddDays(30));
        _sessionCache.Set(SessionCacheKey, cookies, TimeSpan.FromMinutes(10));

        _logger.Debug("Authentication succeeded.");
    }

    protected override IDictionary<string, string> GetCookies()
    {
        return base.GetCookies() ?? _sessionCache.Find(SessionCacheKey);
    }

    public override object RequestAction(string action, IDictionary<string, string> query)
    {
        if (action == "checkCaptcha")
        {
            var request = new HttpRequestBuilder(Settings.BaseUrl + "simple_captcha.php")
                .SetHeader("Referer", Settings.BaseUrl + "login")
                .WithRateLimit(RateLimit.TotalSeconds)
                .Build();

            var response = _httpClient.ExecuteProxied(request, Definition);

            _sessionCache.Set(CaptchaCacheKey, response.GetCookies(), TimeSpan.FromMinutes(10));

            return new
            {
                captchaRequest = new Captcha
                {
                    ContentType = response.Headers.ContentType,
                    ImageData = response.ResponseData
                }
            };
        }

        return base.RequestAction(action, query);
    }

    protected override bool CheckIfLoginNeeded(HttpResponse httpResponse)
    {
        return Cookies == null || !Cookies.ContainsKey("lf_session") || httpResponse.Content.Contains("href=\"/login\"");
    }

    private IndexerCapabilities SetCapabilities()
    {
        var caps = new IndexerCapabilities
        {
            TvSearchParams = new List<TvSearchParam>
            {
                TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep
            },
            MovieSearchParams = new List<MovieSearchParam>
            {
                MovieSearchParam.Q
            }
        };

        caps.Categories.AddCategoryMapping(1, NewznabStandardCategory.TV);

        return caps;
    }
}

public class LostFilmRequest : IndexerRequest
{
    public int? Season { get; }
    public string Episode { get; }
    public IReadOnlyList<string> FilterKeywords { get; }

    public LostFilmRequest(HttpRequest httpRequest, int? season = null, string episode = null, IReadOnlyList<string> filterKeywords = null)
        : base(httpRequest)
    {
        Season = season;
        Episode = episode;
        FilterKeywords = filterKeywords ?? Array.Empty<string>();
    }
}

public class LostFilmRequestGenerator : IIndexerRequestGenerator
{
    private readonly LostFilmSettings _settings;

    public LostFilmRequestGenerator(LostFilmSettings settings)
    {
        _settings = settings;
    }

    public IndexerPageableRequestChain GetSearchRequests(MovieSearchCriteria searchCriteria)
    {
        return GetRequests(searchCriteria.SanitizedSearchTerm);
    }

    public IndexerPageableRequestChain GetSearchRequests(MusicSearchCriteria searchCriteria)
    {
        return new IndexerPageableRequestChain();
    }

    public IndexerPageableRequestChain GetSearchRequests(TvSearchCriteria searchCriteria)
    {
        return GetRequests(searchCriteria.SanitizedSearchTerm, searchCriteria.Season, searchCriteria.Episode);
    }

    public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
    {
        return new IndexerPageableRequestChain();
    }

    public IndexerPageableRequestChain GetSearchRequests(BasicSearchCriteria searchCriteria)
    {
        return GetRequests(searchCriteria.SanitizedSearchTerm);
    }

    private IndexerPageableRequestChain GetRequests(string term, int? season = null, string episode = null)
    {
        var pageableRequests = new IndexerPageableRequestChain();

        if (term.IsNullOrWhiteSpace())
        {
            var request = new HttpRequestBuilder(_settings.BaseUrl + "new")
                .SetHeader("Referer", _settings.BaseUrl)
                .Accept(HttpAccept.Html)
                .Build();

            pageableRequests.Add(new[] { new LostFilmRequest(request) });

            return pageableRequests;
        }

        // The site search does not match sanitized titles (e.g. "Star Wars The Clone Wars") or titles with extra words,
        // so each tier drops one word from the end and the parser uses the dropped words to filter series and episodes.
        var keywords = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var count = keywords.Length; count > 0; count--)
        {
            var request = new HttpRequestBuilder(_settings.BaseUrl + "ajaxik.php")
                .Post()
                .AddFormParameter("act", "common")
                .AddFormParameter("type", "search")
                .AddFormParameter("val", string.Join(" ", keywords.Take(count)))
                .SetHeader("Content-Type", "application/x-www-form-urlencoded")
                .SetHeader("Referer", _settings.BaseUrl)
                .Accept(HttpAccept.Json)
                .Build();

            pageableRequests.AddTier(new[] { new LostFilmRequest(request, season, episode, keywords.Skip(count).ToList()) });
        }

        return pageableRequests;
    }

    public Func<IDictionary<string, string>> GetCookies { get; set; }
    public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
}

public class LostFilmParser : IParseIndexerResponse
{
    private static readonly Regex PlayEpisodeRegex = new(@"PlayEpisode\('(?<id>\d+)(?<season>\d{3})(?<episode>\d{3})'\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NewEpisodeLinkRegex = new(@"/season_(?<season>\d+)/episode_(?<episode>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SeriesIdRegex = new(@"/Images/(?<id>\d+)/", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReleaseDetailsRegex = new(@"Видео:\ (?<quality>.+).\ Размер:\ (?<size>.+).\ Перевод", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly CultureInfo RussianCulture = new("ru-RU");

    private readonly ProviderDefinition _definition;
    private readonly LostFilmSettings _settings;
    private readonly TimeSpan _rateLimit;
    private readonly IIndexerHttpClient _httpClient;
    private readonly Logger _logger;

    private IDictionary<string, string> _cookies;

    public LostFilmParser(ProviderDefinition definition, LostFilmSettings settings, TimeSpan rateLimit, IIndexerHttpClient httpClient, Logger logger)
    {
        _definition = definition;
        _settings = settings;
        _rateLimit = rateLimit;
        _httpClient = httpClient;
        _logger = logger;
    }

    public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
    {
        _cookies = indexerResponse.HttpRequest.Cookies;

        var request = (LostFilmRequest)indexerResponse.Request;

        if (request.HttpRequest.Url.Path.EndsWith("/new"))
        {
            return ParseNewReleases(indexerResponse.Content);
        }

        return ParseSearch(indexerResponse, request);
    }

    private List<ReleaseInfo> ParseNewReleases(string content)
    {
        var releases = new List<ReleaseInfo>();

        var parser = new HtmlParser();
        using var dom = parser.ParseDocument(content);

        foreach (var row in dom.QuerySelectorAll("div.row"))
        {
            var link = row.QuerySelector("a")?.GetAttribute("href");
            var image = row.QuerySelector("img.thumb")?.GetAttribute("src");

            if (link == null || image == null)
            {
                continue;
            }

            var episodeMatch = NewEpisodeLinkRegex.Match(link);
            var seriesMatch = SeriesIdRegex.Match(image);

            if (!episodeMatch.Success || !seriesMatch.Success)
            {
                continue;
            }

            var date = ParseDate(row.QuerySelector("div.overlay > div.right-part")?.TextContent);

            releases.AddRange(FetchTrackerReleases(
                seriesMatch.Groups["id"].Value,
                episodeMatch.Groups["season"].Value,
                episodeMatch.Groups["episode"].Value,
                _settings.BaseUrl + link.TrimStart('/'),
                date));
        }

        return releases;
    }

    private List<ReleaseInfo> ParseSearch(IndexerResponse indexerResponse, LostFilmRequest request)
    {
        var releases = new List<ReleaseInfo>();

        var json = JToken.Parse(indexerResponse.Content);

        // Protect from {"data":false,"result":"ok"}
        if (json.Type != JTokenType.Object || json["data"] is not JObject data || data["series"] is not JArray jsonSeries || !jsonSeries.HasValues)
        {
            return releases;
        }

        var series = jsonSeries.ToList();
        var keywords = request.FilterKeywords;
        var seriesFilterKeywords = 0;

        if (series.Count > 1)
        {
            for (seriesFilterKeywords = keywords.Count; seriesFilterKeywords > 0; seriesFilterKeywords--)
            {
                var seriesFilter = string.Join(" ", keywords.Take(seriesFilterKeywords));
                var filteredSeries = series.Where(s => s.Value<string>("title_orig").ContainsIgnoreCase(seriesFilter)).ToList();

                if (filteredSeries.Any())
                {
                    series = filteredSeries;
                    break;
                }
            }
        }

        var episodeKeywords = keywords.Skip(seriesFilterKeywords).ToList();

        foreach (var serie in series)
        {
            var url = _settings.BaseUrl + serie.Value<string>("link").TrimStart('/');
            url += request.Season is > 0 ? $"/season_{request.Season}" : "/seasons";

            if (request.Episode.IsNotNullOrWhiteSpace())
            {
                releases.AddRange(FetchEpisodeReleases($"{url}/episode_{request.Episode}"));

                continue;
            }

            // Drop one filter word each time nothing is found, the last attempt uses an empty filter
            for (var count = episodeKeywords.Count; count >= 0; count--)
            {
                var filter = string.Join(" ", episodeKeywords.Take(count));
                var seriesReleases = FetchSeriesReleases(url, request.Season, filter);

                if (seriesReleases.Any())
                {
                    releases.AddRange(seriesReleases);
                    break;
                }
            }
        }

        return releases;
    }

    private List<ReleaseInfo> FetchEpisodeReleases(string url)
    {
        var releases = new List<ReleaseInfo>();

        var parser = new HtmlParser();
        using var dom = parser.ParseDocument(FetchPage(url));

        var playButton = dom.QuerySelector("div.external-btn");

        if (playButton == null || playButton.ClassList.Contains("inactive"))
        {
            return releases;
        }

        // '... Дата выхода eng: 09 марта 2012 г. ...' -> '09 марта 2012'
        var dateString = dom.QuerySelector("div.title-block > div.details-pane > div.left-box")?.TextContent ?? string.Empty;
        dateString = TrimString(dateString, dateString.Contains("TBA") ? "ru: " : "eng: ", " г.");

        var date = DateTime.Now;
        if (dateString?.Length == 4 && DateTime.TryParseExact(dateString, "yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedYear))
        {
            date = parsedYear;
        }
        else if (DateTime.TryParse(dateString, RussianCulture, DateTimeStyles.AssumeLocal, out var parsedDate))
        {
            date = parsedDate;
        }

        return FetchTrackerReleases(playButton, url, date);
    }

    private List<ReleaseInfo> FetchSeriesReleases(string url, int? season, string filter)
    {
        var releases = new List<ReleaseInfo>();

        var parser = new HtmlParser();
        using var dom = parser.ParseDocument(FetchPage(url));

        const string rowSelector = "table.movie-parts-list > tbody > tr";

        foreach (var seasonBlock in dom.QuerySelectorAll("div.serie-block"))
        {
            // Null for Extras blocks
            var seasonButton = seasonBlock.QuerySelector("div.movie-details-block > div.external-btn");

            if (seasonButton != null && season is > 0)
            {
                // The season button has no onclick handler when inactive, the watched button always has the season code
                var buttonCode = seasonBlock.QuerySelector("div.movie-details-block > div.haveseen-btn")?.GetAttribute("data-code") ?? string.Empty;
                var currentSeason = buttonCode.Substring(buttonCode.IndexOf('-') + 1);

                if (currentSeason != season.ToString())
                {
                    continue;
                }

                if (seasonButton.ClassList.Contains("inactive"))
                {
                    _logger.Debug("No season pack found for S{0}", season);
                    break;
                }
            }

            if (filter.IsNullOrWhiteSpace() && seasonButton != null && !seasonButton.ClassList.Contains("inactive"))
            {
                var date = ParseEpisodeColumnDate(seasonBlock.QuerySelector(rowSelector)?.QuerySelector("td.delta"));
                var seasonReleases = FetchTrackerReleases(seasonButton, url, date);

                releases.AddRange(seasonReleases);

                if (season is > 0)
                {
                    break;
                }

                if (seasonReleases.Any())
                {
                    continue;
                }
            }

            foreach (var row in seasonBlock.QuerySelectorAll(rowSelector).Where(r => !r.ClassList.Contains("not-available")))
            {
                if (filter.IsNotNullOrWhiteSpace() && !(row.QuerySelector("td.gamma > div")?.TextContent.ContainsIgnoreCase(filter) ?? false))
                {
                    continue;
                }

                var playButton = row.QuerySelector("td.zeta > div.external-btn");
                var dateColumn = row.QuerySelector("td.delta");

                if (playButton == null || dateColumn == null)
                {
                    continue;
                }

                // goTo('/series/Prison_Break/season_5/episode_9/',false)
                var link = TrimString(dateColumn.GetAttribute("onclick") ?? string.Empty, "'", "'");
                var infoUrl = link != null ? _settings.BaseUrl + link.TrimStart('/') : url;

                releases.AddRange(FetchTrackerReleases(playButton, infoUrl, ParseEpisodeColumnDate(dateColumn)));
            }
        }

        return releases;
    }

    private List<ReleaseInfo> FetchTrackerReleases(IElement playButton, string infoUrl, DateTime publishDate)
    {
        var match = PlayEpisodeRegex.Match(playButton.GetAttribute("onclick") ?? string.Empty);

        if (!match.Success)
        {
            return new List<ReleaseInfo>();
        }

        return FetchTrackerReleases(
            match.Groups["id"].Value.TrimStart('0'),
            match.Groups["season"].Value.TrimStart('0'),
            match.Groups["episode"].Value.TrimStart('0'),
            infoUrl,
            publishDate);
    }

    private List<ReleaseInfo> FetchTrackerReleases(string seriesId, string season, string episode, string infoUrl, DateTime publishDate)
    {
        var releases = new List<ReleaseInfo>();

        // 999 stands for the whole season
        var searchUrl = $"{_settings.BaseUrl}v_search.php?c={seriesId}&s={season}&e={(episode.IsNullOrWhiteSpace() ? "999" : episode)}";
        var searchContent = FetchPage(searchUrl);

        if (searchContent.Contains("log in first"))
        {
            CookiesUpdater(null, null);
            throw new IndexerAuthException("LostFilm session expired, please try again.");
        }

        // The tracker link contains a hash that can only be obtained from this redirection page
        var parser = new HtmlParser();
        using var searchDom = parser.ParseDocument(searchContent);
        var metaContent = searchDom.QuerySelector("meta[http-equiv=\"refresh\"], meta")?.GetAttribute("content");

        if (metaContent == null || !metaContent.Contains("url="))
        {
            _logger.Debug("Unexpected tracker redirection page: {0}", searchContent);
            return releases;
        }

        var redirectUrl = metaContent.Substring(metaContent.IndexOf("url=", StringComparison.Ordinal) + 4).Trim('"', '\'', ' ');
        if (!redirectUrl.StartsWith("http"))
        {
            redirectUrl = _settings.BaseUrl + redirectUrl.TrimStart('/');
        }

        using var dom = parser.ParseDocument(FetchPage(redirectUrl));

        var rows = dom.QuerySelectorAll("div.inner-box--item");

        if (!rows.Any())
        {
            _logger.Debug("No releases found on tracker page: {0}", dom.QuerySelector("p")?.TextContent);
            return releases;
        }

        var seriesTitle = dom.QuerySelector("div.inner-box--subtitle")?.TextContent ?? string.Empty;
        if (seriesTitle.Contains(','))
        {
            seriesTitle = seriesTitle.Substring(0, seriesTitle.LastIndexOf(','));
        }

        var episodeName = TrimString(dom.QuerySelector("div.inner-box--text")?.TextContent ?? string.Empty, "(", ")");
        var episodeString = GetEpisodeString(season, episode);

        foreach (var row in rows)
        {
            var downloadUrl = row.QuerySelector("div.inner-box--link > a")?.GetAttribute("href");
            var releaseDetails = ReleaseDetailsRegex.Match(row.QuerySelector("div.inner-box--desc")?.TextContent ?? string.Empty);

            if (downloadUrl == null || !releaseDetails.Success)
            {
                continue;
            }

            var quality = releaseDetails.Groups["quality"].Value.Trim();
            quality = Regex.Replace(quality, "-Rip", "Rip", RegexOptions.IgnoreCase);
            quality = Regex.Replace(quality, "WEB-DLRip", "WEBDL", RegexOptions.IgnoreCase);
            quality = Regex.Replace(quality, "WEB-DL", "WEBDL", RegexOptions.IgnoreCase);
            quality = Regex.Replace(quality, "HDTVRip", "HDTV", RegexOptions.IgnoreCase);
            quality = Regex.Replace(quality, "1080 ", "1080p ", RegexOptions.IgnoreCase);
            quality = Regex.Replace(quality, "720 ", "720p ", RegexOptions.IgnoreCase);

            var techInfo = string.Join(" ", new[] { "rus", quality, "(LostFilm)" }.Where(s => s.IsNotNullOrWhiteSpace()));
            var title = string.Join(" - ", new[] { seriesTitle.Trim(), episodeString, episodeName?.Trim(), techInfo }.Where(s => s.IsNotNullOrWhiteSpace()));

            var size = releaseDetails.Groups["size"].Value.ToUpperInvariant()
                .Replace("ТБ", "TB")
                .Replace("ГБ", "GB")
                .Replace("МБ", "MB")
                .Replace("КБ", "KB");

            releases.Add(new TorrentInfo
            {
                Guid = downloadUrl,
                InfoUrl = infoUrl,
                DownloadUrl = downloadUrl,
                Title = title,
                Categories = new List<IndexerCategory> { NewznabStandardCategory.TV },
                Size = ParseUtil.GetBytes(size),
                PublishDate = publishDate,
                Seeders = 1,
                Peers = 2,
                DownloadVolumeFactor = 0,
                UploadVolumeFactor = 1,
                MinimumRatio = 1,
                MinimumSeedTime = 172800 // 48 hours
            });
        }

        return releases;
    }

    private string FetchPage(string url)
    {
        var request = new HttpRequestBuilder(url)
            .SetCookies(_cookies ?? new Dictionary<string, string>())
            .SetHeader("Referer", _settings.BaseUrl)
            .Accept(HttpAccept.Html)
            .WithRateLimit(_rateLimit.TotalSeconds)
            .Build();

        request.SuppressHttpError = true;

        var response = _httpClient.ExecuteProxied(request, _definition);

        if (response.HasHttpError)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new TooManyRequestsException(request, response);
            }

            throw new IndexerException(new IndexerResponse(new IndexerRequest(request), response), $"HTTP Error - {response.StatusCode}. {url}");
        }

        return response.Content;
    }

    private static string GetEpisodeString(string season, string episode)
    {
        if (season.IsNullOrWhiteSpace() || season is "0" or "999")
        {
            return string.Empty;
        }

        if (episode.IsNullOrWhiteSpace() || episode is "0" or "999")
        {
            return $"S{season}";
        }

        return $"S{season}E{episode}";
    }

    private static DateTime ParseEpisodeColumnDate(IElement dateColumn)
    {
        // 'Eng: 23.05.2017' -> '23.05.2017' OR '23.05.2017' -> '23.05.2017'
        var dateString = dateColumn?.QuerySelector("span.small-text")?.TextContent;
        dateString = dateString.IsNullOrWhiteSpace() ? dateColumn?.QuerySelector("span")?.TextContent : dateString.Substring(dateString.IndexOf(':') + 1);

        return ParseDate(dateString);
    }

    private static DateTime ParseDate(string dateString)
    {
        return DateTime.TryParseExact(dateString?.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : DateTime.Now;
    }

    private static string TrimString(string s, string startString, string endString)
    {
        var start = s.IndexOf(startString, StringComparison.Ordinal);
        var end = s.LastIndexOf(endString, StringComparison.Ordinal);

        return start != -1 && end > start ? s.Substring(start + startString.Length, end - start - startString.Length) : null;
    }

    public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
}

public class LostFilmSettings : UserPassTorrentBaseSettings
{
    [FieldDefinition(4, Type = FieldType.CardigannCaptcha, Label = "Captcha", HelpText = "Only needed when the site asks for a captcha, which usually happens after several logins in a short time")]
    public string Captcha { get; set; }
}
