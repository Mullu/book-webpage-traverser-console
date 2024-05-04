using BookWebPageScraper.src.Services;
using HtmlAgilityPack;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace BookWebPageScraper
{
    public class ScraperHandlerService : IScraperHandlerService
    {
        private const string IndexFileName = "index.html";
        private const string ImgNodeName = "img";
        private const string LinkNodeName = "link";
        private const string AnchorNodeName = "a";
        private const string HrefAttributeName = "href";
        private const string SrcAttributeName = "src";
        private static readonly SemaphoreSlim semaphoreSlim = new SemaphoreSlim(4);
        private static readonly object locker = new();

        public async Task ScrapeBookPages(string baseUrl, string outputPath)
        {
            var urlToLocalPathMap = new ConcurrentDictionary<string, string>();

            try
            {
                CreateDirectory(outputPath);
                var mainHtmlDocument = await FetchHtmlDocument(baseUrl + IndexFileName);
                SaveHtmlPage(mainHtmlDocument.DocumentNode.OuterHtml, outputPath, IndexFileName);

                string booksFolder = Path.Combine(outputPath, "Books");
                CreateDirectory(booksFolder);

                var htmlDocument = await FetchHtmlDocument(baseUrl);
                SaveHtmlPage(htmlDocument.DocumentNode.OuterHtml, booksFolder, IndexFileName);

                const string xpathExpression = "//ul[@class='nav nav-list']/li/ul/li/a";
                var bookCategoryPages = htmlDocument.DocumentNode.SelectNodes(xpathExpression);

                if (bookCategoryPages != null)
                {
                    await Task.WhenAll(bookCategoryPages.Select(bookCategoryPage =>
                        ScrapeBookCategoryPage(baseUrl, bookCategoryPage, booksFolder, urlToLocalPathMap)));
                }

                UpdateHtmlLinks(urlToLocalPathMap, outputPath);
                Console.WriteLine("Download completed. You can view the original page and its resources in:");
                Console.WriteLine($"Directory: {Path.GetFullPath(outputPath)}");
                Console.WriteLine($"Original HTML page: {Path.Combine(outputPath, IndexFileName)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        private void CreateDirectory(string directoryPath)
        {
            Directory.CreateDirectory(directoryPath);
        }

        private async Task<HtmlDocument> FetchHtmlDocument(string url)
        {
            using var httpClient = new HttpClient();
            var html = await httpClient.GetStringAsync(url);
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);
            return htmlDocument;
        }

        private void SaveHtmlPage(string htmlContent, string folderPath, string fileName)
        {
            lock (locker)
            {
                File.WriteAllText(Path.Combine(folderPath, fileName), htmlContent);
            }
        }

        private string GetBookUrl(string baseUrl, string bookHref)
        {
            return baseUrl + "catalogue" + bookHref.Substring(8);
        }

        private string GetValidFileName(string fileName)
        {
            string invalidChars = new string(Path.GetInvalidFileNameChars());
            string invalidReStr = Regex.Escape(invalidChars);
            return Regex.Replace(fileName, "[" + invalidReStr + "]", "_");
        }

        private async Task ScrapeBookCategoryPage(
            string baseUrl,
            HtmlNode bookCategoryPage,
            string booksFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            string categoryUrl = baseUrl + bookCategoryPage.Attributes[HrefAttributeName].Value;
            var categoryHtmlDocument = await FetchHtmlDocument(categoryUrl);

            string categoryFolder = Path.Combine(booksFolder, bookCategoryPage.InnerText.Trim());
            CreateDirectory(categoryFolder);
            SaveHtmlPage(categoryHtmlDocument.DocumentNode.OuterHtml, categoryFolder, IndexFileName);

            string bookLinksXPath = "//h3/a";
            var bookLinks = categoryHtmlDocument.DocumentNode.SelectNodes(bookLinksXPath);

            if (bookLinks != null)
            {
                await Task.WhenAll(bookLinks.Select(bookLink =>
                    ScrapeBookPageWithSemaphore(baseUrl, bookLink, booksFolder, urlToLocalPathMap)));
            }
        }

        private async Task ScrapeBookPageWithSemaphore(
            string baseUrl,
            HtmlNode bookLink,
            string booksFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            await AcquireSemaphore();

            try
            {
                string bookUrl = GetBookUrl(baseUrl, bookLink.Attributes[HrefAttributeName].Value);
                var bookHtmlDocument = await FetchHtmlDocument(bookUrl);
                string bookTitle = GetValidFileName(bookHtmlDocument.DocumentNode.SelectSingleNode("//h1").InnerText.Trim());
                string bookFolder = Path.Combine(booksFolder, bookTitle);
                CreateDirectory(bookFolder);

                lock (locker)
                {
                    SaveHtmlPage(bookHtmlDocument.DocumentNode.OuterHtml, bookFolder, IndexFileName);
                }

                await ExtractAndDownloadImages(bookHtmlDocument, bookUrl, bookFolder, urlToLocalPathMap);
                await ExtractAndDownloadPages(bookHtmlDocument, bookUrl, bookFolder, urlToLocalPathMap);
            }
            finally
            {
                ReleaseSemaphore();
            }
        }

        private async Task ExtractAndDownloadImages(
            HtmlDocument htmlDocument,
            string baseUrl,
            string bookFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            var imageNodes = GetImageNodes(htmlDocument);
            if (imageNodes != null)
            {
                foreach (var node in imageNodes)
                {
                    string src = node.Attributes[SrcAttributeName]?.Value;
                    if (src != null && !urlToLocalPathMap.ContainsKey(src))
                    {
                        Uri uri = GetAbsoluteUri(baseUrl, src);
                        string fileName = GetLocalFilePath(bookFolder, uri.LocalPath);

                        await DownloadAndSaveFile(uri, fileName);

                        urlToLocalPathMap.TryAdd(src, fileName);
                    }
                }
            }
        }

        private HtmlNodeCollection GetImageNodes(HtmlDocument htmlDocument)
        {
            return htmlDocument.DocumentNode.SelectNodes($"//{ImgNodeName}");
        }

        private async Task ExtractAndDownloadPages(
            HtmlDocument htmlDocument,
            string baseUrl,
            string bookFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            var pageNodes = GetPageNodes(htmlDocument);
            if (pageNodes != null)
            {
                foreach (var node in pageNodes)
                {
                    string src = node.Attributes[HrefAttributeName]?.Value;
                    if (src != null && !urlToLocalPathMap.ContainsKey(src))
                    {
                        Uri uri = GetAbsoluteUri(baseUrl, src);
                        string fileName = GetLocalFilePath(bookFolder, uri.LocalPath);

                        await DownloadAndSaveFile(uri, fileName);

                        urlToLocalPathMap.TryAdd(src, fileName);
                    }
                }
            }
        }

        private HtmlNodeCollection GetPageNodes(HtmlDocument htmlDocument)
        {
            return htmlDocument.DocumentNode.SelectNodes($"//{LinkNodeName}|//{AnchorNodeName}[@{HrefAttributeName}]");
        }

        private async Task DownloadAndSaveFile(Uri uri, string outputPath)
        {
            using (var httpClient = new HttpClient())
            {
                using (var response = await httpClient.GetAsync(uri))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        using (var fileStream = new FileStream(outputPath, FileMode.Create))
                        {
                            await response.Content.CopyToAsync(fileStream);
                            Console.WriteLine($"Successfully downloaded and saved {uri}. Status code: {response.StatusCode}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Failed to download {uri}. Status code: {response.StatusCode}");
                    }
                }
            }
        }

        private Uri GetAbsoluteUri(string baseUrl, string src)
        {
            Uri uri;
            if (!Uri.TryCreate(src, UriKind.Absolute, out uri))
            {
                uri = new Uri(new Uri(baseUrl), src);
            }
            return uri;
        }

        private string GetLocalFilePath(string bookFolder, string localPath)
        {
            return Path.Combine(bookFolder, Path.GetFileName(localPath));
        }

        private void UpdateHtmlLinks(ConcurrentDictionary<string, string> urlToLocalPathMap, string outputPath)
        {
            string updatedHtml = File.ReadAllText(Path.Combine(outputPath, IndexFileName));
            foreach (var keyValuePair in urlToLocalPathMap)
            {
                updatedHtml = updatedHtml.Replace(keyValuePair.Key, Path.GetFileName(keyValuePair.Value));
            }
            File.WriteAllText(Path.Combine(outputPath, IndexFileName), updatedHtml);
        }

        private async Task AcquireSemaphore()
        {
            await semaphoreSlim.WaitAsync();
        }

        private void ReleaseSemaphore()
        {
            semaphoreSlim.Release();
        }
    }
}
