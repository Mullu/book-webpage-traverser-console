using BookWebPageScraper.src.Services;
using HtmlAgilityPack;
using System.Collections.Concurrent;

namespace BookWebPageScraper
{
    public class ScraperHandlerService : IScraperHandlerService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        private const string IndexFileName = "index.html";
        private const string ImgNodeName = "img";
        private const string HrefAttributeName = "href";
        private const string SrcAttributeName = "src";
        private const string BaseUrl = "http://books.toscrape.com/";
        private const int TotalPages = 50;

        public ScraperHandlerService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task ScrapeBookPages(string outputPath)
        {
            var urlToLocalPathMap = new ConcurrentDictionary<string, string>();

            try
            {
                CreateDirectory(outputPath);
                var htmlDocument = await FetchHtmlDocument(BaseUrl);
                SaveHtmlPage(BaseUrl, htmlDocument.DocumentNode.OuterHtml, outputPath, IndexFileName);  //Home page
                await ExtractAndDownloadBookCategoryPages(htmlDocument, BaseUrl);

                string catalogueFolder = Path.Combine(outputPath, "catalogue");
                CreateDirectory(catalogueFolder);

                string[] urls = Enumerable.Range(1, TotalPages)
                    .Select(pageNumber => $"{BaseUrl}catalogue/page-{pageNumber}.html")
                    .ToArray();

                var options = new ParallelOptions { MaxDegreeOfParallelism = 20 };

                await Parallel.ForEachAsync(urls, options, async (url, cancellationToken) =>
                {
                    var html = await GetStringAsync(url, cancellationToken);
                    var htmlDocument = new HtmlDocument();
                    htmlDocument.LoadHtml(html);
                    await ScrapeBookPage(url, htmlDocument, catalogueFolder, urlToLocalPathMap);
                });

                Console.WriteLine("Scraping completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        private static void CreateDirectory(string directoryPath)
        {
            Directory.CreateDirectory(directoryPath);
        }

        private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetStringAsync(url, cancellationToken);
        }

        private async Task<HtmlDocument> FetchHtmlDocument(string url)
        {
            var html = await GetStringAsync(url, default);
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);

            return htmlDocument;
        }

        private async Task ScrapeBookPage(
            string pageUrl,
            HtmlDocument htmlDocument,
            string catalogueFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            string bookLinksXPath = "//h3/a";
            var bookLinks = htmlDocument.DocumentNode.SelectNodes(bookLinksXPath);
            string pageFileName = GetPageFileName(pageUrl);
            SaveHtmlPage(pageUrl, htmlDocument.DocumentNode.OuterHtml, catalogueFolder, pageFileName);

            if (bookLinks != null)
            {
                var imageScrapingTasks = new List<Task>();

                foreach (var bookLink in bookLinks)
                {
                    imageScrapingTasks.Add(ScrapeBookPageImageFiles(pageUrl, bookLink, catalogueFolder, urlToLocalPathMap));
                }

                await Task.WhenAll(imageScrapingTasks);
            }
        }

        private async Task ScrapeBookPageImageFiles(
            string pageUrl,
            HtmlNode bookLink,
            string booksFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            string bookUrl = GetBookUrl(pageUrl, bookLink.Attributes[HrefAttributeName].Value);
            var bookHtml = await GetStringAsync(bookUrl, default);

            var bookHtmlDocument = new HtmlDocument();
            bookHtmlDocument.LoadHtml(bookHtml);

            string bookTitle = GetValidFileName(bookHtmlDocument.DocumentNode.SelectSingleNode("//h1").InnerText.Trim());
            int indexHtmlIndex = bookLink.Attributes[HrefAttributeName].Value.IndexOf("index.html");
            string bookPath = bookLink.Attributes[HrefAttributeName].Value.Substring(0, indexHtmlIndex);
            string bookFolder = Path.Combine(booksFolder, bookPath);

            CreateDirectory(bookFolder);

            var imageNodes = GetImageNodes(bookHtmlDocument);
            if (imageNodes != null)
            {
                var imageDownloadTasks = new List<Task>();

                foreach (var node in imageNodes)
                {
                    string src = node.Attributes[SrcAttributeName]?.Value;
                    if (src != null && !urlToLocalPathMap.ContainsKey(src))
                    {
                        Uri uri = GetAbsoluteUri(bookUrl, src);
                        string fileName = GetLocalFilePath(bookFolder, uri.LocalPath);

                        imageDownloadTasks.Add(DownloadAndSaveFile(uri, fileName)
                            .ContinueWith(task =>
                            {
                                if (task.Exception != null)
                                {
                                    Console.WriteLine($"Failed to download {uri}: {task.Exception.InnerException.Message}");
                                }
                                else
                                {
                                    urlToLocalPathMap.TryAdd(src, fileName);
                                }
                            }));
                    }
                }

                await Task.WhenAll(imageDownloadTasks);
            }
        }

        private async Task ExtractAndDownloadBookCategoryPages(HtmlDocument htmlDocument, string baseUrl)
        {
            var pageLinks = htmlDocument.DocumentNode.SelectNodes("//a[contains(@href,'index.html')]");
            if (pageLinks != null)
            {
                foreach (var pageLink in pageLinks)
                {
                    string pageUrl = GetBookUrl(baseUrl, pageLink.Attributes["href"].Value);
                    if (pageUrl.StartsWith("http://books.toscrape.com/catalogue/category"))
                    {
                        if (pageUrl.StartsWith("http://books.toscrape.com/catalogue/category/books_1"))
                        {
                            await ProcessBooks1Page(pageUrl);
                        }
                        else
                        {
                            await ProcessBookCategoryPage(pageUrl);
                        }
                    }
                }
            }
        }

        private async Task ProcessBooks1Page(string pageUrl)
        {
            var pageHtml = await GetStringAsync(pageUrl, default);

            var pageHtmlDocument = new HtmlDocument();
            pageHtmlDocument.LoadHtml(pageHtml);

            string pageFileName = GetPageFileName(pageUrl);

            string contentPath = Path.Combine("downloaded_books_files", "catalogue", "category", "books_1");
            Directory.CreateDirectory(contentPath);
            SaveHtmlPage(pageUrl, pageHtmlDocument.DocumentNode.OuterHtml, contentPath, pageFileName);
        }

        private async Task ProcessBookCategoryPage(string pageUrl)
        {
            string currentUrl = pageUrl;

            while (!string.IsNullOrEmpty(currentUrl))
            {
                var pageHtml = await GetStringAsync(currentUrl, default);
                var pageHtmlDocument = new HtmlDocument();
                pageHtmlDocument.LoadHtml(pageHtml);

                string pageFileName = GetPageFileName(currentUrl);

                string[] parts = currentUrl.Split('/');
                string middlePart = parts[parts.Length - 2];

                string contentPath = Path.Combine("downloaded_books_files", "catalogue", "category", "books", middlePart);
                Directory.CreateDirectory(contentPath);

                await SaveHtmlBookCategoryPage(currentUrl, pageHtmlDocument.DocumentNode.OuterHtml, contentPath, pageFileName);

                currentUrl = GetNextPageUrl(pageHtmlDocument, currentUrl);
            }
        }

        private static void SaveHtmlPage(string pageUrl, string htmlContent, string folderPath, string pageFileName)
        {
            File.WriteAllText(Path.Combine(folderPath, pageFileName), htmlContent);
            Console.WriteLine($"Successfully downloaded and saved {pageUrl}");
        }

        private static async Task SaveHtmlBookCategoryPage(string pageUrl, string htmlContent, string folderPath, string pageFileName)
        {
            File.WriteAllText(Path.Combine(folderPath, pageFileName), htmlContent);
            Console.WriteLine($"Successfully downloaded and saved book category {pageUrl}");
        }

        private static string GetPageFileName(string url)
        {
            string[] parts = url.Split('/');
            return parts[parts.Length - 1];
        }

        private static HtmlNodeCollection GetImageNodes(HtmlDocument htmlDocument)
        {
            return htmlDocument.DocumentNode.SelectNodes($"//{ImgNodeName}");
        }

        private async Task DownloadAndSaveFile(Uri uri, string outputPath)
        {
            var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetAsync(uri);

            if (response.IsSuccessStatusCode)
            {
                using (var fileStream = new FileStream(outputPath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fileStream);
                    Console.WriteLine($"Successfully downloaded and saved {uri}");
                }
            }
            else
            {
                Console.WriteLine($"Failed to download {uri}. Status code: {response.StatusCode}");
            }
        }

        private static string GetNextPageUrl(HtmlDocument htmlDocument, string currentUrl)
        {
            var nextPageLink = htmlDocument.DocumentNode.SelectSingleNode("//li[@class='next']/a");
            if (nextPageLink != null)
            {
                string nextPagePath = nextPageLink.Attributes[HrefAttributeName].Value;
                Uri currentUri = new Uri(currentUrl);
                Uri nextPageUri = new Uri(currentUri, nextPagePath);
                return nextPageUri.AbsoluteUri;
            }
            else
            {
                return null;
            }
        }

        private static string GetBookUrl(string baseUrl, string relativeUrl)
        {
            if (!Uri.TryCreate(relativeUrl, UriKind.Absolute, out Uri result))
            {
                return new Uri(new Uri(baseUrl), relativeUrl).AbsoluteUri;
            }
            return result.AbsoluteUri;
        }

        private static string GetValidFileName(string input)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                input = input.Replace(c, '_');
            }

            input = input.Replace("...", "_");

            input = input.TrimEnd('.');

            return input;
        }

        private static string GetLocalFilePath(string folderPath, string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            return Path.Combine(folderPath, fileName);
        }

        private static Uri GetAbsoluteUri(string baseUrl, string relativeUrl)
        {
            return new Uri(new Uri(baseUrl), relativeUrl);
        }
    }
}
