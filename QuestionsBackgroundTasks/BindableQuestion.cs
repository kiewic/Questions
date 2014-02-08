﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace QuestionsBackgroundTasks
{
    public sealed class BindableQuestion
    {
        private const string SummaryKey = "Summary";
        private PowerpuffJsonObject json;
        private string id;

        public BindableQuestion(string id, JsonObject jsonObject)
        {
            this.id = id;
            json = new PowerpuffJsonObject(jsonObject);
        }

        public JsonObject ToJsonObject()
        {
            return json.ToJsonObject();
        }

        public string Id
        {
            get
            {
                return id;
            }
        }

        public string WebsiteUrl
        {
            get
            {
                return json.GetNamedString("Website");
            }
        }

        public string Title
        {
            get
            {
                return json.GetNamedString("Title");
            }
        }

        public DateTimeOffset PubDate
        {
            get
            {
                return DateTimeOffset.Parse(json.GetNamedString("PubDate"));
            }
        }

        public Uri Link
        {
            get
            {
                return new Uri(json.GetNamedString("Link"));
            }
        }

        public string PubDateDiff
        {
            get
            {
                const double daysPerMonth = 30.43;
                const double daysPerYear = 365.25;
                TimeSpan diff = DateTime.Now - PubDate;
                if (diff.TotalSeconds < 60)
                {
                    int seconds = (int)diff.TotalSeconds;
                    return String.Format("{0} {1} ago", seconds, seconds == 1 ? "second" : "seconds");
                }
                else if (diff.TotalMinutes < 60)
                {
                    int minutes = (int)diff.TotalMinutes;
                    return String.Format("{0} {1} ago", minutes, minutes == 1 ? "minute" : "minutes");
                }
                else if (diff.TotalHours < 24)
                {
                    int hours = (int)diff.TotalHours;
                    return String.Format("{0} {1} ago", hours, hours == 1 ? "hour" : "hours");
                }
                else if (diff.TotalDays < daysPerMonth)
                {
                    int days = (int)diff.TotalDays;
                    return String.Format("{0} {1} ago", days, days == 1 ? "day" : "days");
                }
                else if (diff.TotalDays < daysPerYear)
                {
                    int months = (int)(diff.TotalDays / daysPerMonth);
                    return String.Format("{0} {1} ago", months, months == 1 ? "month" : "months");
                }

                int years = (int)(diff.TotalDays / daysPerYear);
                return String.Format("{0} {1} ago", years, years == 1 ? "year" : "years");
            }
        }

        public string FaviconUrl
        {
            get
            {
                string websiteUrl = WebsiteUrl;
                string faviconUrl = SettingsManager.GetWebsiteFaviconUrl(websiteUrl);
                return faviconUrl;
            }
        }

        public JsonObject Tags
        {
            get
            {
                return json.GetNamedObject("Categories");
            }
        }

        public string TagsStrip
        {
            get
            {
                return String.Join(", ", Tags.Keys);
            }
        }

        private string[] GetBuzzWords()
        {
            List<string> buzzWords = new List<string>();

            string websiteUrl = WebsiteUrl;
            JsonArray buzzWordsCollection = SettingsManager.GetWebsiteBuzzWords(websiteUrl);
            string title = Title.ToLower();
            string summary = json.GetNamedString(SummaryKey).ToLower();

            foreach (IJsonValue jsonValue in buzzWordsCollection)
            {
                string originalBuzzWord = jsonValue.GetString();
                string lowerCaseBuzzWord = originalBuzzWord.ToLower();
                if (summary.Contains(lowerCaseBuzzWord) && title.Contains(lowerCaseBuzzWord))
                {
                    buzzWords.Add(originalBuzzWord);
                }
            }

            return buzzWords.ToArray();
        }

        public string BuzzWordsStrip
        {
            get
            {
                return String.Join(", ", GetBuzzWords());
            }
        }
    }
}
