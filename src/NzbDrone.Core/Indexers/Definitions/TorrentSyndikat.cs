using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Settings;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Definitions
{
    public class TorrentSyndikat : TorrentIndexerBase<TorrentSyndikatSettings>
    {
        public override string Name => "TorrentSyndikat";
        public override string[] IndexerUrls => new[] { "https://torrent-syndikat.org/", "https://tee-stube.org/" };
        public override string Description => "A German general tracker";
        public override string Language => "de-DE";
        public override Encoding Encoding => Encoding.UTF8;
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;
        public override IndexerCapabilities Capabilities => SetCapabilities();

        public TorrentSyndikat(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new TorrentSyndikatRequestGenerator() { Settings = Settings, Capabilities = Capabilities };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new TorrentSyndikatParser(Settings, Capabilities.Categories);
        }

        public override async Task<IndexerPageableQueryResult> Fetch(TvSearchCriteria searchCriteria)
        {
            var result = await base.Fetch(searchCriteria);

            // ID searches return every torrent of the product, so narrow them down to the requested season or episode
            if (searchCriteria.ImdbId.IsNotNullOrWhiteSpace() && searchCriteria.EpisodeSearchString.IsNotNullOrWhiteSpace())
            {
                result.Releases = result.Releases.Where(r => r.Title.Contains(searchCriteria.EpisodeSearchString, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return result;
        }

        private IndexerCapabilities SetCapabilities()
        {
            var caps = new IndexerCapabilities
            {
                TvSearchParams = new List<TvSearchParam>
                                   {
                                       TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep, TvSearchParam.ImdbId
                                   },
                MovieSearchParams = new List<MovieSearchParam>
                                   {
                                       MovieSearchParam.Q, MovieSearchParam.ImdbId
                                   },
                MusicSearchParams = new List<MusicSearchParam>
                                   {
                                       MusicSearchParam.Q
                                   },
                BookSearchParams = new List<BookSearchParam>
                                   {
                                       BookSearchParam.Q
                                   }
            };

            caps.Categories.AddCategoryMapping("apps/awindows", NewznabStandardCategory.PC, "Apps / Windows");
            caps.Categories.AddCategoryMapping("apps/alinux", NewznabStandardCategory.PC, "Apps / Linux");
            caps.Categories.AddCategoryMapping("apps/amacos", NewznabStandardCategory.PCMac, "Apps / MacOS");

            caps.Categories.AddCategoryMapping("games/gwindows", NewznabStandardCategory.PCGames, "Spiele / Windows");
            caps.Categories.AddCategoryMapping("games/glinux", NewznabStandardCategory.PCGames, "Spiele / Linux");
            caps.Categories.AddCategoryMapping("games/gmacos", NewznabStandardCategory.PCGames, "Spiele / MacOS");
            caps.Categories.AddCategoryMapping("games/playstation", NewznabStandardCategory.ConsoleOther, "Spiele / Playstation");
            caps.Categories.AddCategoryMapping("games/nintendo", NewznabStandardCategory.ConsoleOther, "Spiele / Nintendo");
            caps.Categories.AddCategoryMapping("games/xbox", NewznabStandardCategory.ConsoleOther, "Spiele / XBOX");

            caps.Categories.AddCategoryMapping("video/movie", NewznabStandardCategory.Movies, "Video / Filme");
            caps.Categories.AddCategoryMapping("video/movie", NewznabStandardCategory.MoviesUHD, "Video / Filme / UHD");
            caps.Categories.AddCategoryMapping("video/movie", NewznabStandardCategory.MoviesHD, "Video / Filme / HD");
            caps.Categories.AddCategoryMapping("video/movie", NewznabStandardCategory.MoviesSD, "Video / Filme / SD");
            caps.Categories.AddCategoryMapping("video/movie", NewznabStandardCategory.MoviesForeign, "Video / Filme / Fremdsprachig");
            caps.Categories.AddCategoryMapping("video/series", NewznabStandardCategory.TV, "Video / Serien");
            caps.Categories.AddCategoryMapping("video/series", NewznabStandardCategory.TVUHD, "Video / Serien / UHD");
            caps.Categories.AddCategoryMapping("video/series", NewznabStandardCategory.TVHD, "Video / Serien / HD");
            caps.Categories.AddCategoryMapping("video/series", NewznabStandardCategory.TVSD, "Video / Serien / SD");
            caps.Categories.AddCategoryMapping("video/series", NewznabStandardCategory.TVForeign, "Video / Serien / Fremdsprachig");
            caps.Categories.AddCategoryMapping("video/sports", NewznabStandardCategory.TVSport, "Video / Sport");
            caps.Categories.AddCategoryMapping("video/vmusic", NewznabStandardCategory.AudioVideo, "Video / Musik");

            caps.Categories.AddCategoryMapping("audio/amusic", NewznabStandardCategory.Audio, "Audio / Musik");
            caps.Categories.AddCategoryMapping("audio/amusic", NewznabStandardCategory.AudioLossless, "Audio / Musik / FLAC");
            caps.Categories.AddCategoryMapping("audio/amusic", NewznabStandardCategory.AudioMP3, "Audio / Musik / MP3");
            caps.Categories.AddCategoryMapping("audio/audiobook", NewznabStandardCategory.AudioAudiobook, "Audio / Hörbücher");
            caps.Categories.AddCategoryMapping("audio/podcast", NewznabStandardCategory.AudioOther, "Audio / Podcasts");

            caps.Categories.AddCategoryMapping("ebook/novel", NewznabStandardCategory.BooksEBook, "eBook / Romane");
            caps.Categories.AddCategoryMapping("ebook/nfiction", NewznabStandardCategory.BooksEBook, "eBook / Sachbücher");
            caps.Categories.AddCategoryMapping("ebook/comic", NewznabStandardCategory.BooksComics, "eBook / Comics");
            caps.Categories.AddCategoryMapping("ebook/npaper", NewznabStandardCategory.BooksMags, "eBook / Zeitungen");
            caps.Categories.AddCategoryMapping("ebook/magazine", NewznabStandardCategory.BooksMags, "eBook / Magazine");

            caps.Categories.AddCategoryMapping("misc/misc", NewznabStandardCategory.Other, "Misc / Misc");
            caps.Categories.AddCategoryMapping("misc/grabbel", NewznabStandardCategory.Other, "Misc / Grabbelkiste");

            return caps;
        }
    }

    public class TorrentSyndikatRequestGenerator : IIndexerRequestGenerator
    {
        public TorrentSyndikatSettings Settings { get; set; }
        public IndexerCapabilities Capabilities { get; set; }

        private IEnumerable<IndexerRequest> GetPagedRequests(string term, int[] categories, string imdbId = null)
        {
            var queryCollection = new NameValueCollection { { "apiKey", Settings.ApiKey } };

            if (imdbId.IsNotNullOrWhiteSpace())
            {
                // The uniqueId endpoint does not support any filters
                queryCollection.Add("id", $"imdb:{ParseUtil.GetImdbId(imdbId)}");

                yield return new IndexerRequest($"{Settings.ApiUrl}/torrents/uniqueId.php?{queryCollection.GetQueryString()}", HttpAccept.Json);
                yield break;
            }

            if (term.IsNotNullOrWhiteSpace())
            {
                queryCollection.Add("searchTerm", term.Trim());
            }

            var trackerCategories = Capabilities.Categories.MapTorznabCapsToTrackers(categories);
            if (trackerCategories.Count == 1)
            {
                queryCollection.Add("subCat", trackerCategories[0].Split('/')[1]);
            }
            else if (trackerCategories.Count > 1)
            {
                var cats = trackerCategories.Select(c => c.Split('/')[0]).Distinct().ToList();
                if (cats.Count == 1)
                {
                    queryCollection.Add("cat", cats[0]);
                }
            }

            var releaseTypes = Settings.ReleaseTypes.ToList();
            if (releaseTypes.Count == 1)
            {
                queryCollection.Add("releaseType", TorrentSyndikatSettings.GetReleaseTypeName(releaseTypes[0]));
            }

            yield return new IndexerRequest($"{Settings.ApiUrl}/torrents/all.php?{queryCollection.GetQueryString()}", HttpAccept.Json);
        }

        public IndexerPageableRequestChain GetSearchRequests(MovieSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories, searchCriteria.ImdbId));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(MusicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(TvSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedTvSearchString), searchCriteria.Categories, searchCriteria.ImdbId));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BasicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public Func<IDictionary<string, string>> GetCookies { get; set; }
        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class TorrentSyndikatParser : IParseIndexerResponse
    {
        private readonly TorrentSyndikatSettings _settings;
        private readonly IndexerCapabilitiesCategories _categories;

        public TorrentSyndikatParser(TorrentSyndikatSettings settings, IndexerCapabilitiesCategories categories)
        {
            _settings = settings;
            _categories = categories;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<ReleaseInfo>();

            var jsonResponse = STJson.Deserialize<TorrentSyndikatResponse>(indexerResponse.Content);

            var releaseTypes = _settings.ReleaseTypes.Select(TorrentSyndikatSettings.GetReleaseTypeName).ToList();

            foreach (var row in jsonResponse.Data)
            {
                if (releaseTypes.Any() && !releaseTypes.Contains(row.ReleaseType))
                {
                    continue;
                }

                if (_settings.ProductsOnly && row.Product == null)
                {
                    continue;
                }

                var details = $"{_settings.BaseUrl}details.php?id={row.Id}";
                var tags = row.Tags ?? new List<string>();

                var release = new TorrentInfo
                {
                    MinimumRatio = 1,
                    MinimumSeedTime = 96 * 60 * 60,
                    DownloadVolumeFactor = 1,
                    UploadVolumeFactor = 1,
                    Guid = details,
                    InfoUrl = details,
                    DownloadUrl = $"{_settings.ApiUrl}/torrents/download.php?id={row.Id}&apiKey={_settings.ApiKey}",
                    Title = row.Name,
                    Categories = MapCategories(row, tags),
                    PublishDate = DateTimeOffset.FromUnixTimeSeconds(row.Added).UtcDateTime.ToLocalTime(),
                    Size = row.Size,
                    Files = row.Files,
                    Seeders = row.Seeders,
                    Peers = row.Seeders,
                    Grabs = row.Downloads,
                    ImdbId = row.Product?.ImdbId ?? 0,
                    TvdbId = row.Product?.TvdbId ?? 0,
                    TmdbId = row.Product?.TmdbId ?? 0,
                    TvMazeId = row.Product?.TvmazeId ?? 0
                };

                var descriptions = new List<string>();
                var product = row.Product;

                if (product != null)
                {
                    if (product.Title.IsNotNullOrWhiteSpace())
                    {
                        descriptions.Add("Title: " + product.Title);
                    }

                    if (product.TitleOrigin.IsNotNullOrWhiteSpace())
                    {
                        descriptions.Add("Original Title: " + product.TitleOrigin);
                    }

                    if (product.Year > 0)
                    {
                        descriptions.Add("Year: " + product.Year);
                    }

                    descriptions.Add("Product-Link: " + _settings.BaseUrl + "product.php?pid=" + product.Pid);
                }

                if (row.Genres != null && row.Genres.Any())
                {
                    descriptions.Add("Genres: " + string.Join(", ", row.Genres.Select(g => g.Name)));
                }

                if (tags.Any())
                {
                    descriptions.Add("Tags: " + string.Join(", ", tags));
                }

                if (row.ReleaseType.IsNotNullOrWhiteSpace())
                {
                    descriptions.Add("Release Type: " + row.ReleaseType);
                }

                if (descriptions.Any())
                {
                    release.Description = string.Join(Environment.NewLine, descriptions);
                }

                torrentInfos.Add(release);
            }

            return torrentInfos.ToArray();
        }

        private ICollection<IndexerCategory> MapCategories(TorrentSyndikatTorrent row, List<string> tags)
        {
            var categories = new List<IndexerCategory>();

            switch (row.SubCat)
            {
                case "movie":
                    categories.Add(MapResolution(tags, NewznabStandardCategory.Movies, NewznabStandardCategory.MoviesUHD, NewznabStandardCategory.MoviesHD, NewznabStandardCategory.MoviesSD));
                    if (row.IsForeign == 1)
                    {
                        categories.Add(NewznabStandardCategory.MoviesForeign);
                    }

                    break;
                case "series":
                    categories.Add(MapResolution(tags, NewznabStandardCategory.TV, NewznabStandardCategory.TVUHD, NewznabStandardCategory.TVHD, NewznabStandardCategory.TVSD));
                    if (row.IsForeign == 1)
                    {
                        categories.Add(NewznabStandardCategory.TVForeign);
                    }

                    break;
                case "amusic":
                    categories.Add(tags.Contains("flac") ? NewznabStandardCategory.AudioLossless : tags.Contains("mp3") ? NewznabStandardCategory.AudioMP3 : NewznabStandardCategory.Audio);
                    break;
                default:
                    categories.AddRange(_categories.MapTrackerCatToNewznab($"{row.Cat}/{row.SubCat}"));
                    break;
            }

            return categories;
        }

        private static IndexerCategory MapResolution(List<string> tags, IndexerCategory fallback, IndexerCategory uhd, IndexerCategory hd, IndexerCategory sd)
        {
            if (tags.Contains("2160p") || tags.Contains("4320p"))
            {
                return uhd;
            }

            if (tags.Contains("1080p") || tags.Contains("1080i") || tags.Contains("720p"))
            {
                return hd;
            }

            return tags.Contains("sd") ? sd : fallback;
        }

        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class TorrentSyndikatSettingsValidator : NoAuthSettingsValidator<TorrentSyndikatSettings>
    {
        public TorrentSyndikatSettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    public class TorrentSyndikatSettings : NoAuthTorrentBaseSettings
    {
        private static readonly TorrentSyndikatSettingsValidator Validator = new();

        public TorrentSyndikatSettings()
        {
            ApiKey = "";
            ReleaseTypes = new List<int>();
        }

        public string ApiUrl => $"{BaseUrl.TrimEnd('/')}/api_9djWe8Tb2NE3p6opyqnh/v2";

        [FieldDefinition(2, Label = "ApiKey", Privacy = PrivacyLevel.ApiKey, HelpText = "IndexerTorrentSyndikatSettingsApiKeyHelpText")]
        public string ApiKey { get; set; }

        [FieldDefinition(3, Label = "Products Only", Type = FieldType.Checkbox, HelpText = "Limit search to torrents linked to a product")]
        public bool ProductsOnly { get; set; }

        [FieldDefinition(4, Label = "Release Types", Type = FieldType.Select, SelectOptions = typeof(TorrentSyndikatReleaseTypes))]
        public IEnumerable<int> ReleaseTypes { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        public static string GetReleaseTypeName(int releaseType)
        {
            return ((TorrentSyndikatReleaseTypes)releaseType).ToString().Replace("_", "").ToLowerInvariant();
        }
    }

    public enum TorrentSyndikatReleaseTypes
    {
        [FieldOption]
        P2P = 1,
        [FieldOption]
        Scene = 2,
        [FieldOption]
        O_Scene = 3
    }

    public class TorrentSyndikatResponse
    {
        public IReadOnlyCollection<TorrentSyndikatTorrent> Data { get; set; } = Array.Empty<TorrentSyndikatTorrent>();
    }

    public class TorrentSyndikatTorrent
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public long Added { get; set; }
        public int IsForeign { get; set; }
        public long Size { get; set; }
        public string Cat { get; set; }
        public string SubCat { get; set; }
        public int Files { get; set; }
        public int Downloads { get; set; }
        public int Seeders { get; set; }
        public string ReleaseType { get; set; }
        public List<TorrentSyndikatGenre> Genres { get; set; }
        public List<string> Tags { get; set; }
        public TorrentSyndikatProduct Product { get; set; }
    }

    public class TorrentSyndikatGenre
    {
        public string Name { get; set; }
    }

    public class TorrentSyndikatProduct
    {
        public int Pid { get; set; }
        public string TitleOrigin { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public int? ImdbId { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public int? TvmazeId { get; set; }
    }
}
