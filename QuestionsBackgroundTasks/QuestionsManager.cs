﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml.Controls;
using Windows.Web.Syndication;

namespace QuestionsBackgroundTasks
{
    public enum AddQuestionResult
    {
        None = 0,
        Added,
        Updated
    }

    public sealed class QuestionsManager
    {
        private const string SummaryKey = "Summary";
        private const string QuestionsKey = "Questions";
        private const string FileName = "questions.json";
        private static AutoResetEvent addEvent = new AutoResetEvent(true);
        private static StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        private static JsonObject rootObject;
        private static JsonObject questionsCollection;

        public static IAsyncAction LoadAsync()
        {
            return AsyncInfo.Run(async (cancellationToken) =>
            {
                if (rootObject != null)
                {
                    // File already loaded, there is nothing to do.
                    return;
                }

                string jsonString = await FilesManager.LoadAsync(storageFolder, FileName);

                if (!JsonObject.TryParse(jsonString, out rootObject))
                {
                    Debug.WriteLine("Invalid JSON object in {0}", FileName);
                    CreateFromScratch();
                    return;
                }

                if (!rootObject.ContainsKey(QuestionsKey))
                {
                    CreateFromScratch();
                    return;
                }

                questionsCollection = rootObject.GetNamedObject(QuestionsKey);
            });
        }

        public static void Unload()
        {
            rootObject = null;
        }

        public static IAsyncAction SaveAsync()
        {
            Task saveTask = FilesManager.SaveAsync(storageFolder, FileName, rootObject.Stringify());
            return saveTask.AsAsyncAction();
        }

        private static void CreateFromScratch()
        {
            rootObject = new JsonObject();

            questionsCollection = new JsonObject();
            rootObject.Add(QuestionsKey, questionsCollection);
        }

        // This method can be call simultaneously. Make sure only one thread is touching it.
        public static AddQuestionsResult AddQuestions(string websiteUrl, SyndicationFeed feed, bool skipLatestPubDate)
        {
            AddQuestionsResult globalResult = new AddQuestionsResult();

            DateTimeOffset latestPubDate = SettingsManager.GetLastestPubDate(websiteUrl);
            DateTimeOffset newLatestPubDate = DateTimeOffset.MinValue;
            Debug.WriteLine("{0} Current LastestPubDate is {1}.", websiteUrl, latestPubDate);

            // Wait until the event is set by another thread.
            addEvent.WaitOne();

            try
            {
                CheckSettingsAreLoaded();

                foreach (SyndicationItem item in feed.Items)
                {
                    AddQuestionResult result = AddQuestionResult.None;
                    if (skipLatestPubDate || DateTimeOffset.Compare(item.PublishedDate.DateTime, latestPubDate) > 0)
                    {
                        result = AddQuestion(websiteUrl, item);

                        // Chances are we need to update the LatestPubDate.
                        if (result == AddQuestionResult.Added && item.PublishedDate > newLatestPubDate)
                        {
                            newLatestPubDate = item.PublishedDate;
                            Debug.WriteLine("{0} New LastestPubDate is {1}.", websiteUrl, newLatestPubDate);
                        }
                    }
                    else
                    {
                        result = UpdateQuestion(websiteUrl, item);
                    }

                    switch (result)
                    {
                        case AddQuestionResult.Added:
                            globalResult.AddedQuestions++;
                            break;
                        case AddQuestionResult.Updated:
                            globalResult.UpdatedQuestions++;
                            break;
                    }
                }

                // If the quesiton list did not change, there should not be a new LatestPubDate.
                if (globalResult.AddedQuestions > 0)
                {
                    SettingsManager.SetLastestPubDate(websiteUrl, newLatestPubDate);
                }

                return globalResult;
            }
            finally
            {
                // Set the event, so other threads waiting on it can do their job.
                addEvent.Set();
            }
        }

        internal static async Task LimitTo150AndSaveAsync()
        {
            Debug.WriteLine("Questions count before limit: {0}", questionsCollection.Count);

            const int questionsLimit = 150;
            if (questionsCollection.Count > questionsLimit)
            {
                JsonObject questionsCollectionCopy = new JsonObject();
                int i = 0;

                IList<BindableQuestion> list = GetSortedQuestions();

                foreach (BindableQuestion question in list)
                {
                    questionsCollectionCopy.Add(question.Id, question.ToJsonObject());
                    if (++i == questionsLimit)
                    {
                        break;
                    }
                }

                questionsCollection = questionsCollectionCopy;
                rootObject.SetNamedValue(QuestionsKey, questionsCollectionCopy);
            }

            Debug.WriteLine("Questions count after limit: {0}", questionsCollection.Count);

            await SaveAsync();
        }

        // NOTE: Adding a single question does not load or save settings. Good for performance.
        private static AddQuestionResult AddQuestion(string websiteUrl, SyndicationItem item)
        {
            // If the latestPubDate validation was skipped, it could happend that que query returns
            // questions we already have.
            if (questionsCollection.ContainsKey(item.Id))
            {
                return UpdateQuestion(websiteUrl, item);
            }

            JsonObject questionObject = new JsonObject();

            string title = item.Title != null ? item.Title.Text : String.Empty;
            string summary = item.Summary != null ? item.Summary.Text : String.Empty;

            questionObject.Add("Website", JsonValue.CreateStringValue(websiteUrl));
            questionObject.Add("Title", JsonValue.CreateStringValue(title));
            questionObject.Add(SummaryKey, JsonValue.CreateStringValue(summary));

            // TODO: Do we need to use PublishedDate.ToLocalTime(), or can we just work with the standard time?
            questionObject.Add("PubDate", JsonValue.CreateStringValue(item.PublishedDate.ToString()));

            if (item.Links.Count > 0)
            {
                questionObject.Add("Link", JsonValue.CreateStringValue(item.Links[0].Uri.AbsoluteUri));
            }

            JsonValue nullValue = JsonValue.Parse("null");
            JsonObject categoriesCollection = new JsonObject();
            foreach (SyndicationCategory category in item.Categories)
            {
                categoriesCollection.Add(category.Term, nullValue);
            }
            questionObject.Add("Categories", categoriesCollection);

            questionsCollection.Add(item.Id, questionObject);

            Debug.WriteLine("{0} New question. PubDate is {1}.", item.Id, item.PublishedDate);
            return AddQuestionResult.Added;
        }

        private static AddQuestionResult UpdateQuestion(string website, SyndicationItem item)
        {
            if (!questionsCollection.ContainsKey(item.Id))
            {
                Debug.WriteLine("{0} Skipped question.", item.Id);
                return AddQuestionResult.None;
            }

            JsonObject questionObject = questionsCollection.GetNamedObject(item.Id);
            AddQuestionResult result = AddQuestionResult.None;

            string oldTitle = questionObject.GetNamedStringOrEmptyString("Title");
            string newTitle = item.Title != null ? item.Title.Text : String.Empty;
            if (oldTitle != newTitle)
            {
                questionObject.SetNamedValue("Title", JsonValue.CreateStringValue(newTitle));
                Debug.WriteLine("{0} Updated question. Different title.", item.Id);
                result = AddQuestionResult.Updated;
            }

            string oldSummary = questionObject.GetNamedStringOrEmptyString(SummaryKey);
            string newSummary = item.Summary != null ? item.Summary.Text : String.Empty;
            if (oldSummary != newSummary)
            {
                questionObject.SetNamedValue(SummaryKey, JsonValue.CreateStringValue(newTitle));
                Debug.WriteLine("{0} Updated question. Different summary.", item.Id);
                result = AddQuestionResult.Updated;
            }

            Debug.WriteLineIf(
                result == AddQuestionResult.None,
                String.Format("{0} Skipped question. Up to date.", item.Id));

            return result;
        }

        public static void RemoveQuestionsAndSave(string websiteUrl, string tag)
        {
            List<string> keysToDelete = new List<string>();

            foreach (var keyValuePair in questionsCollection)
            {
                BindableQuestion tempQuestion = new BindableQuestion(
                    keyValuePair.Key,
                    keyValuePair.Value.GetObject());

                // Is it the selected website?
                // A null websiteUrl matches all questions.
                if (websiteUrl == null || tempQuestion.WebsiteUrl == websiteUrl)
                {
                    // Is it the selected tag?
                    // A null tag matches any tag.
                    if (tag == null || tempQuestion.Tags.ContainsKey(tag))
                    {
                        keysToDelete.Add(keyValuePair.Key);
                    }
                }
            }

            RemoveQuestionsAndSave(keysToDelete);
        }

        public static void RemoveQuestionsAndSave(IList<string> keysToDelete)
        {
            // Remove from questions-list and add to read-questions-list.
            foreach (string key in keysToDelete)
            {
                if (questionsCollection.Remove(key))
                {
                    ReadListManager.AddReadQuestion(key);
                }
                else
                {
                    // Removing a question that was not in the questions-list.
                    Debugger.Break();
                }
            }

            // Only save if at least one question was deleted.
            if (keysToDelete.Count > 0)
            {
                ReadListManager.LimitTo450AndSave();

                // Do not wait until questions-save is completed.
                var saveOperation = QuestionsManager.SaveAsync();

                // And refresh tile and badge.
                FeedManager.UpdateTileAndBadge();
            }
        }

        // TODO: Use ListView.ScrollIntoView().
        public static void DisplayQuestions(ListView listView, IList<BindableQuestion> list)
        {
            listView.ItemsSource = list;
            //listView.ScrollIntoView(listView.Items[0]);
        }

        // TODO: I fthis is an expensive operation, maybe we should consider to cache the result.
        public static IList<BindableQuestion> GetSortedQuestions()
        {
            CheckSettingsAreLoaded();

            List<BindableQuestion> list = new List<BindableQuestion>();
            foreach (var keyValuePair in questionsCollection)
            {
                BindableQuestion tempQuestion = new BindableQuestion(
                    keyValuePair.Key,
                    keyValuePair.Value.GetObject());

                list.Add(tempQuestion);
            }

            // Sort the items!
            list.Sort((a, b) =>
            {
                // Multiply by -1 to sort in ascending order.
                return DateTimeOffset.Compare(a.PubDate, b.PubDate) * -1;
            });

            return list;
        }

        private static void CheckSettingsAreLoaded()
        {
            if (rootObject == null)
            {
                throw new Exception("Questions not loaded.");
            }
        }

        // Notice that tile update, badge update and saveing only occurs if at least one questions is removed.
        public static IAsyncAction RemoveReadQuestionsUpdateTileAndBadgeAndSaveAsync()
        {
            return AsyncInfo.Run(async (cancellationToken) =>
            {
                uint removedQuestions = await RemoveQuestionsInReadList();

                // Only save if at least one question was deleted.
                if (removedQuestions > 0)
                {
                    await QuestionsManager.SaveAsync();
                    FeedManager.UpdateTileAndBadge();
                }
            });
        }

        public static IAsyncOperation<uint> RemoveQuestionsInReadList()
        {
            return AsyncInfo.Run(async (cancellationToken) =>
            {
                CheckSettingsAreLoaded();

                JsonArray readList = await ReadListManager.GetReadListAsync();

                // Search for read-list-questions in questions-list.
                uint removedQuestions = 0;
                foreach (IJsonValue jsonValue in readList)
                {
                    string id = jsonValue.GetString();
                    if (questionsCollection.ContainsKey(id))
                    {
                        if (questionsCollection.Remove(id))
                        {
                            removedQuestions++;
                        }
                    }
                }

                return removedQuestions;
            });
        }
    }
}
