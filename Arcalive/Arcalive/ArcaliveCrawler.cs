﻿using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

//TODO 아카콘 댓글 비율

namespace Arcalive
{
    public partial class ArcaliveCrawler : IBaseCrawler<Post>
    {
        public event EventHandler Print;

        public event EventHandler DumpText;

        public event EventHandler GetCrawlingProgress;

        public static int CallTimes { get; private set; }

        private string channelName = string.Empty;

        /// <summary>
        /// 키워드를 포함하는 채널의 주소 리스트를 반환합니다.
        /// </summary>
        /// <param name="keyword"></param>
        /// <returns></returns>
        public static List<string> GetChannelLinks(string keyword)
        {
            List<string> results = new List<string>();

            using (WebClient client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                string sitesource = client.DownloadString("https://arca.live/private_boards");
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(sitesource);
                var channels = doc.DocumentNode.SelectNodes("/html/body/div/div[3]/article/div[2]/div");
                foreach (var channel in channels)
                {
                    var channelName = channel.Descendants("a").First().InnerText;
                    if (channelName.Contains(keyword))
                    {
                        var address = channel.Descendants("a").First().Attributes["href"].Value;
                        results.Add("https://arca.live" + address);
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// 키워드를 포함하는 채널의 주소 하나(첫번째)를 반환합니다.
        /// </summary>
        /// <param name="keyword"></param>
        /// <returns></returns>
        public static string GetChannelLink(string keyword)
        {
            string result = string.Empty;

            using (WebClient client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                string sitesource = client.DownloadString("https://arca.live/private_boards");
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(sitesource);
                var channels = doc.DocumentNode.SelectNodes("/html/body/div/div[2]/article/div[2]/div");
                foreach (var channel in channels)
                {
                    var channelName = channel.Descendants("a").First().InnerText;
                    if (channelName.Contains(keyword))
                    {
                        var address = channel.Descendants("a").First().Attributes["href"].Value;
                        result = "https://arca.live" + address;
                        return result;
                    }
                }
            }

            return result;
        }

        public ArcaliveCrawler(string channelName)
        {
            bool isFullLink = channelName.StartsWith("https://arca.live/b/");
            if (isFullLink)
            {
                this.channelName = channelName;
            }
            else
            {
                this.channelName = "https://arca.live/b/" + channelName;
            }

            CallTimes = 0;
        }

        public List<Post> CrawlBoards(DateTime? From = null, DateTime? To = null, int startPage = 1)
        {
            if (From == null) From = DateTime.Today.AddDays(1 - DateTime.Today.Day).AddMonths(-1);
            if (To == null) To = DateTime.Today;

            int page = startPage;
            List<Post> results = new List<Post>();

            bool isEalierThanFrom = true;

            while (isEalierThanFrom == true)
            {
                HtmlDocument doc = DownloadDoc(channelName + $"?p={page}");

                if (string.IsNullOrEmpty(doc.Text))
                    return results;

                Print?.Invoke(this, new PrintCallbackArg($"{CallTimes++,5} >> CrawlBoard >> Page: {page}"));

                var posts = doc.DocumentNode.SelectNodes("//div[contains(@class, 'list-table')]/a");

                int i;
                for (i = 0; i < posts.Count; i++)
                {
                    if (posts[i].Attributes["class"].Value == "vrow")
                        // 공지사항이 아닌 글이 나올 때까지 스킵
                        break;
                }
                for (; i < posts.Count; i++)
                {
                    Post p = new Post();

                    p.time = DateTime.Parse(posts[i].SelectSingleNode(".//div[2]/span[2]/time").Attributes["datetime"].Value);

                    if (p.time >= To)
                    {
                        Print?.Invoke(this, new PrintCallbackArg($"{CallTimes++,5} >> CrawlBoard >> Skip Post"));
                        continue;
                    }
                    else if (p.time <= From)
                    {
                        isEalierThanFrom = false;
                        break;
                    }

                    var postfix = posts[i].Attributes["href"].Value;
                    p.link = "https://arca.live" + postfix.Substring(0, postfix.LastIndexOf('?'));
                    if (results.Any(e => e.link == p.link))
                    {
                        Print?.Invoke(this, new PrintCallbackArg("중복방지"));
                        continue;
                    }

                    var commentNum = posts[i].SelectSingleNode(".//div[1]/span[2]/span[3]").InnerText;
                    bool can = int.TryParse(Regex.Replace(commentNum, @"\D", ""), out int cap);
                    p.comments = new List<Comment>(cap);

                    p.id = int.Parse(posts[i].SelectSingleNode(".//div[1]/span[1]").InnerText);
                    p.badge = posts[i].SelectSingleNode(".//div[1]/span[2]/span[1]").InnerText;
                    p.title = posts[i].SelectSingleNode(".//div[1]/span[2]/span[2]").InnerText;
                    p.author = posts[i].SelectSingleNode(".//div[2]/span[1]").InnerText;

                    results.Add(p);
                }
                page++;
            }

            return results;
        }

        public List<Post> CrawlPosts(List<Post> Posts, List<string> skip = null)
        {
            List<Post> results = new List<Post>();

            for (int i = 0; i < Posts.Count; i++)
            {
                Stopwatch sp = new Stopwatch();
                sp.Start();

                if (skip.Any(x => (x == Posts[i].badge) && x != string.Empty)) continue;
                HtmlDocument doc = DownloadDoc(Posts[i].link);
                if (string.IsNullOrEmpty(doc.Text))
                {
                    Print?.Invoke(this, new PrintCallbackArg($"{CallTimes++,5} >> CrawlPosts >> Skip Post"));
                    continue;
                }
                Print?.Invoke(this, new PrintCallbackArg($"{CallTimes++,5} >> CrawlPosts >> {Posts[i].id}"));

                Post newPost = new Post();
                newPost = Posts[i];

                var articleInfoNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'article-info')]");
                var commentAreaNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'list-area')]");
                var contentNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'fr-view article-content')]");

                var commentNumNode = articleInfoNode.SelectSingleNode(".//span[8]");
                var viewNumNode = articleInfoNode.SelectSingleNode(".//span[11]");

                newPost.content = contentNode.InnerText;
                newPost.view = int.Parse(viewNumNode.InnerText);

                DumpText?.Invoke(this, new PrintCallbackArg(doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class, 'fr-view article-content')]").InnerText));

                if (int.Parse(commentNumNode.InnerText) == 0) continue; // 댓글이 없으면 스킵

                List<Comment> comments = new List<Comment>();
                try
                {
                    var commentWrappers = commentAreaNode?.Descendants(0)
                    .Where(n => n.HasClass("comment-item"));
                    foreach (var commentWrapper in commentWrappers)
                    {
                        Comment c = new Comment();
                        var author = commentWrapper.SelectSingleNode(".//div/div[1]/span").InnerText;
                        if (commentWrapper.SelectSingleNode(".//div/div[2]/div/img[@src]") != null)
                        {
                            var arcacon = commentWrapper.SelectSingleNode(".//div/div[2]/div/img[@src]").Attributes["src"].Value;
                            c.content = arcacon;
                            c.isArcacon = true;
                        }
                        else if (commentWrapper.SelectSingleNode(".//div/div[2]/div/video[@src]") != null)
                        {
                            var arcacon = commentWrapper.SelectSingleNode(".//div/div[2]/div/video[@src]").Attributes["src"].Value;
                            if (arcacon.EndsWith(".mp4"))
                                arcacon += ".gif";
                            c.content = arcacon;
                            c.isArcacon = true;
                        }
                        else
                        {
                            c.content = commentWrapper.SelectSingleNode(".//div/div[2]/div").InnerText;
                            c.isArcacon = false;
                        }
                        c.author = author;
                        //c.arcacon = arcacon;
                        comments.Add(c);
                    }
                }
                catch
                {
                    // 댓글은 분명 없는데 commentNum.InnerText의
                    // 값이 0이 아닌 경우가 있어서 try/catch문을 씀
                }
                newPost.comments = comments;

                results.Add(newPost);
                sp.Stop();
                GetCrawlingProgress?.Invoke(this, new ProgressPagesCallBack(i + 1, Posts.Count, (int)sp.Elapsed.TotalMilliseconds));
            }

            return results;
        }

        public int FindStartPage(DateTime TargetTime, int StartPage = 1, int MaxPage = 10000)
        {
            DateTime TimeofFirstPost, TimeofLastPost;
            bool isPageFound = false;
            int currentPage = -1;

            Print?.Invoke(this, new PrintCallbackArg($"{CallTimes++,5} >> FindStartPage >> Finding..."));

            while (isPageFound == false)
            {
                currentPage = (StartPage + MaxPage) / 2;

                HtmlDocument doc = DownloadDoc(channelName + $"?p={currentPage}");
                if (string.IsNullOrEmpty(doc.Text))
                    return -1;

                var posts = doc.DocumentNode.SelectNodes("//div[contains(@class, 'list-table')]/a");

                if (posts.Count <= 1)
                {
                    // 글이 없음 =
                    MaxPage = currentPage;
                    continue;
                }

                int i;
                for (i = 0; i < posts.Count; i++)
                {
                    if (posts[i].Attributes["class"].Value == "vrow")
                        // 공지사항이 아닌 글이 나올 때까지 스킵
                        break;
                }
                TimeofFirstPost = DateTime.Parse(posts[i].SelectSingleNode(".//div[2]/span[2]/time").Attributes["datetime"].Value);
                TimeofLastPost = DateTime.Parse(posts[posts.Count - 1].SelectSingleNode(".//div[2]/span[2]/time").Attributes["datetime"].Value);

                if (TargetTime >= TimeofLastPost && TargetTime <= TimeofFirstPost)
                {
                    isPageFound = true;
                }
                else if (TargetTime >= TimeofLastPost)
                {
                    MaxPage = currentPage;
                }
                else
                {
                    StartPage = currentPage;
                }
            }

            Print?.Invoke(this, new PrintCallbackArg($"{CallTimes++,5} >> FindStartPage >> Found!"));

            return currentPage;
        }

        public static void SerializePosts(List<Post> posts, string filename = "a.dat")
        {
            using (Stream ws = new FileStream(filename, FileMode.Create))
            {
                BinaryFormatter binary = new BinaryFormatter();
                binary.Serialize(ws, posts);
            }
        }

        public static List<Post> DeserializePosts(string filename = "a.dat")
        {
            using (Stream rs = new FileStream(filename, FileMode.Open))
            {
                BinaryFormatter binary = new BinaryFormatter();
                return (List<Post>)binary.Deserialize(rs);
            }
        }
    }
}