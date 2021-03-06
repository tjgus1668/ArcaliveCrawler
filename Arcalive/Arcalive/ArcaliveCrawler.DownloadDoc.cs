﻿using HtmlAgilityPack;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Arcalive
{
    public partial class ArcaliveCrawler
    {
        /// <summary>
        /// 아카라이브 공식 앱 헤더
        /// </summary>
        public const string ArcaliveUserAgent = "live.arca.android/0.8.214";

        public static int CALL = 0;

        public HtmlDocument DownloadDoc(
            string link, string userAgent = ArcaliveUserAgent, int term = 0)
        {

            HtmlDocument doc = new HtmlDocument();
            using (WebClient client = new WebClient() { Encoding = Encoding.UTF8 })
            {
                string siteSource = string.Empty;
                client.Headers.Add("user-agent", userAgent);
                try
                {
                    siteSource = client.DownloadString(link);
                }
                catch (WebException e)
                {
                    var statusCode = ((HttpWebResponse)e.Response)?.StatusCode.ToString() ??
                                     "Report this to developer!!\n" +
                                     $"=> {string.Join(",", client.Headers.AllKeys)}";

                    Print?.Invoke(this, new PrintCallbackArg($"{CallTimes++,5} >> DownloadDoc >> HTML {statusCode} Error"));
                    //Print?.Invoke(this, new PrintCallbackArg($"{CallTimes++,5} >> DownloadDoc >> HTML Error"));
                }
                catch (Exception e)
                {
                    Print?.Invoke(this, new PrintCallbackArg($"{CallTimes++,5} >> DownloadDoc >> Error: {e.Message}"));
                }
                finally
                {
                    doc.LoadHtml(siteSource);
                }
            }

            // HTML 429 에러 방지용
            // 100 이상으로 설정하는 것을 권장
            // 하지만 공앱 헤더를 쓰면 쓸 필요가 없어짐
            Thread.Sleep(term);

            return doc;
        }

        public HtmlDocument DownloadDoc(string link, WebHeaderCollection headers, int term)
        {
            HtmlDocument doc = new HtmlDocument();
            using (WebClient client = new WebClient() { Encoding = Encoding.UTF8 })
            {
                string siteSource = string.Empty;
                client.Headers = headers;
                try
                {
                    siteSource = client.DownloadString(link);
                }
                catch (WebException e)
                {
                    int statusCode = (int)(e.Response as HttpWebResponse).StatusCode;
                    Print?.Invoke(this, new PrintCallbackArg($"{CallTimes++,5} >> DownloadDoc >> HTML {statusCode} Error"));
                }
                finally
                {
                    doc.LoadHtml(siteSource);
                }
            }

            // HTML 429 에러 방지용
            // 100 이상으로 설정하는 것을 권장
            // 하지만 공앱 헤더를 쓰면 쓸 필요가 없어짐
            Thread.Sleep(term);

            return doc;
        }

        public string GetRedirectedUrl(string baseLink, out HtmlDocument doc, string userAgent = ArcaliveUserAgent, int term = 0)
        {
            CALL++;
            Stopwatch sp = new Stopwatch();
            sp.Start();

            var request = (HttpWebRequest) WebRequest.Create(baseLink);
            request.UserAgent = userAgent;
            request.AllowAutoRedirect = false;
            var response1 = (HttpWebResponse) request.GetResponse();
            string result = response1.Headers["location"];
            response1.Close();

            request = (HttpWebRequest) WebRequest.Create("https://arca.live" + result);
            request.UserAgent = userAgent;
            string html = string.Empty;
            doc = new HtmlDocument();
            try
            {
                var response2 = (HttpWebResponse) request.GetResponse();
                using (StreamReader sr = new StreamReader(response2.GetResponseStream()))
                {
                    html = sr.ReadToEnd();
                }
            }
            catch
            {
                // Do Nothing
            }
            finally
            {
                doc.LoadHtml(html);
            }

            sp.Stop();

            int tick = (int)sp.ElapsedMilliseconds;
            Thread.Sleep(term - tick > 0 ? term - tick : 0);

            return result;
        }
    }
}